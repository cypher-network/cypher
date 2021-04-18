//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Serilog;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using Dawn;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISync
    {
        bool IsSynchronized { get; }
        bool SyncRunning { get; }
        Task Fetch(string host, long skip, long take);
        Task Synchronize();
    }

    /// <summary>
    /// 
    /// </summary>
    public class Sync : ISync
    {
        public bool IsSynchronized { get; private set; }
        public bool SyncRunning { get; private set; }
        private const int BatchSize = 20;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly NetworkClient _networkClient;
        private readonly ILogger _logger;
        
        public Sync(IUnitOfWork unitOfWork, IValidator validator, ILocalNode localNode, NetworkClient networkClient,
            ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _localNode = localNode;
            _networkClient = networkClient;
            _logger = logger.ForContext("SourceContext", nameof(Sync));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Synchronize()
        {
            if (SyncRunning) return;
            SyncRunning = true;
            _logger.Here().Information("Trying to Synchronize");
            try
            {
                Dictionary<ulong, Peer> peers;
                const int retryCount = 5;
                var currentRetry = 0;
                var jitterer = new Random();
                for (;;)
                {
                    peers = await _localNode.GetPeers();
                    if (peers.Count == 0)
                    {
                        _logger.Here().Warning("Peer count is zero. It's possible serf is busy... Retrying");
                        currentRetry++;
                    }

                    if (currentRetry > retryCount || peers.Count != 0)
                    {
                        break;
                    }

                    var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, currentRetry)) +
                                     TimeSpan.FromMilliseconds(jitterer.Next(0, 1000));
                    await Task.Delay(retryDelay);
                }
                
                var networkPeerTasks =
                    peers.Values.Select(peer => _networkClient.GetPeerBlockHeightAsync(peer)).ToArray();
                var networkBlockHeights = await Task.WhenAll(networkPeerTasks);
                var fetchTasks = new List<Task>();
                foreach (var networkBlockHeight in networkBlockHeights)
                {
                    if (networkBlockHeight == null) continue;
                    _logger.Here()
                        .Information("Local node block height ({@LocalHeight}). Network block height ({NetworkHeight})",
                            networkBlockHeight.Local.Height, networkBlockHeight.Remote.Height);
                    if (networkBlockHeight.Local.Height != networkBlockHeight.Remote.Height)
                    {
                        fetchTasks.Add(Fetch(networkBlockHeight.Remote.Host, networkBlockHeight.Local.Height,
                            networkBlockHeight.Remote.Height / 188));
                    }
                }

                await Task.WhenAll(fetchTasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while checking");
            }

            _logger.Here().Information("Finish Synchronizing");
            SyncRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Fetch(string host, long skip, long take)
        {
            Guard.Argument(host, nameof(host)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            try
            {
                var numberOfBatches = (int) Math.Ceiling((double) take / BatchSize);
                numberOfBatches = numberOfBatches == 0 ? 1 : numberOfBatches;
                var networkBlockTasks = new List<Task<IList<BlockHeaderProto>>>();
                for (var i = 0; i < numberOfBatches; i++)
                {
                    networkBlockTasks.Add(_networkClient.GetBlocksAsync(host, i + 1 * skip, BatchSize));
                }

                var blockHeaders = await Task.WhenAll(networkBlockTasks);
                foreach (var blocks in blockHeaders)
                {
                    if (blocks.Any() != true) continue;
                    var verifyForkRule = await _validator.VerifyForkRule(blocks.ToArray());
                    if (verifyForkRule == VerifyResult.UnableToVerify)
                    {
                        _logger.Here().Error("Unable to verify fork rule for: {@host}", host);
                        return;
                    }

                    foreach (var blockHeader in blocks.OrderBy(x => x.Height))
                    {
                        try
                        {
                            var verifyBlockHeader = await _validator.VerifyBlockHeader(blockHeader);
                            if (verifyBlockHeader != VerifyResult.Succeed)
                            {
                                return;
                            }

                            var saved = await _unitOfWork.HashChainRepository.PutAsync(blockHeader.ToIdentifier(),
                                blockHeader);
                            if (!saved)
                            {
                                _logger.Here().Error("Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Here().Error(ex, "Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                        }
                    }

                    var localHeight = await _unitOfWork.HashChainRepository.CountAsync();
                    _logger.Here().Information("Local node block height increased to ({@LocalHeight})", localHeight);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Failed to synchronize node");
            }
        }
    }
}