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
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISync
    {
        bool IsSynchronized { get; }
        bool SyncRunning { get; }
        Task<bool> Synchronize(string host, long skip, long take);
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
                for (; ; )
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

                var localBlockHeight = await _unitOfWork.HashChainRepository.CountAsync();
                var localLastBlock = await _unitOfWork.HashChainRepository.GetAsync(b =>
                    new ValueTask<bool>(b.Height == localBlockHeight));
                var localLastBlockHash = localLastBlock?.ToHash();

                var networkPeerTasks =
                    peers.Values.Select(peer => _networkClient.GetPeerLastBlockHashAsync(peer)).ToArray();


                var networkBlockHashes = new List<BlockHashPeer>(await Task.WhenAll(networkPeerTasks));

                var networkBlockHashesGrouped = new List<BlockHashPeer>(await Task.WhenAll(networkPeerTasks))
                    .GroupBy(hash => hash)
                    .Select(hash => new
                    {
                        Hash = hash.Key.BlockHash.Hash,
                        Count = hash.Count()
                    });

                var blockHashes = networkBlockHashesGrouped.ToList();
                var numPeersWithSameHash = blockHashes
                    .FirstOrDefault(element => element.Hash == localLastBlockHash)?
                    .Count ?? 0;

                if (blockHashes.Count == 0)
                {
                    _logger.Here().Information("No remote block hashes found");
                }
                else if (numPeersWithSameHash > peers.Count / 2.0)
                {
                    _logger.Here().Information("Local node has same hash (@Hash} as majority of the network ({@NumSameHash} / {@NumPeers})",
                        localLastBlockHash, numPeersWithSameHash, peers.Count);
                }
                else
                {
                    _logger.Here().Information(
                        "Local node does not have same hash {@Hash} as majority of the network ({@NumSameHash} / {@NumPeers})",
                        localLastBlockHash, numPeersWithSameHash, peers.Count);

                    foreach (var hash in blockHashes)
                    {
                        _logger.Here().Debug("Hash {@Hash} found {@Count} times", hash.Hash, hash.Count);
                    }

                    var synchronized = false;
                    var majorityHash = blockHashes.OrderByDescending(hash => hash.Count).First();
                    foreach (var blockHash in networkBlockHashes.Where(element => element.BlockHash.Hash == majorityHash.Hash))
                    {
                        _logger.Here().Debug("Synchronizing chain with last block hash {@Hash} from {@Peer} {@Host}",
                            majorityHash, blockHash.Peer.NodeName, blockHash.Peer.Host);

                        synchronized = await Synchronize(blockHash.Peer.Host, localBlockHeight, blockHash.BlockHash.Height);
                        if (synchronized)
                        {
                            _logger.Here().Information("Successfully synchronized with {@Peer} {@Host}",
                                blockHash.Peer.NodeName, blockHash.Peer.Host);

                            break;
                        }
                    }

                    if (!synchronized)
                    {
                        _logger.Here().Error("Unable to synchronize with remote peers");
                    }
                }
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
        /// <param name="host"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<bool> Synchronize(string host, long skip, long take)
        {
            Guard.Argument(host, nameof(host)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            try
            {
                var numberOfBatches = (int)Math.Ceiling((double)take / BatchSize);
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
                        _logger.Here().Error("Unable to verify fork rule for: {@Host}", host);
                        return false;
                    }

                    foreach (var blockHeader in blocks.OrderBy(x => x.Height))
                    {
                        try
                        {
                            var verifyBlockHeader = await _validator.VerifyBlockHeader(blockHeader);
                            if (verifyBlockHeader != VerifyResult.Succeed)
                            {
                                return false;
                            }

                            var saved = await _unitOfWork.HashChainRepository.PutAsync(blockHeader.ToIdentifier(),
                                blockHeader);
                            if (!saved)
                            {
                                _logger.Here().Error("Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Here().Error(ex, "Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                            return false;
                        }
                    }

                    var localHeight = await _unitOfWork.HashChainRepository.CountAsync();
                    _logger.Here().Information("Local node block height set to ({@LocalHeight})", localHeight);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Failed to synchronize node");
                return false;
            }

            return true;
        }
    }
}