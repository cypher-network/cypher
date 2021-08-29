//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using Dawn;
using Serilog;
using Block = CYPCore.Models.Block;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISync
    {
        bool SyncRunning { get; }
        Task<bool> Synchronize(string host, ulong skip, int take);
        void Synchronize();
    }

    /// <summary>
    /// 
    /// </summary>
    public class Sync : ISync
    {
        private const uint SyncTimeSlotMinutes = 0x0000000A;
        private const uint SyncStartTimeDueSeconds = 0x00000005;
        
        public bool SyncRunning { get; private set; }
        private const int BatchSize = 100;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly NetworkClient _networkClient;
        private readonly bool _syncWithSeedNodes;
        private readonly ILogger _logger;

        public Sync(IUnitOfWork unitOfWork, IValidator validator, ILocalNode localNode, NetworkClient networkClient,
            bool syncWithSeedNodes, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _localNode = localNode;
            _networkClient = networkClient;
            _syncWithSeedNodes = syncWithSeedNodes;
            _logger = logger.ForContext("SourceContext", nameof(Sync));

            Observable.Timer(TimeSpan.FromSeconds(SyncStartTimeDueSeconds), TimeSpan.FromMinutes(SyncTimeSlotMinutes)).Subscribe(_ =>
            {
                Synchronize();
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public void Synchronize()
        {
            if (SyncRunning) return;
            SyncRunning = true;
            _logger.Here().Information("Trying to Synchronize");

            Task.Factory.StartNew(async () =>
            {
                try
                {
                    Dictionary<ulong, Peer> peers;
                    const int retryCount = 5;
                    var currentRetry = 0;
                    var jitter = new Random();
                    for (; ; )
                    {
                        peers = await _localNode.GetPeers();
                        if (peers == null)
                        {
                            _logger.Here().Warning("Peers are null ... Retrying");
                            continue;
                        }

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
                                         TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
                        await Task.Delay(retryDelay);
                    }

                    var localBlockHeight = await _unitOfWork.HashChainRepository.CountAsync();
                    var localLastBlock = await _unitOfWork.HashChainRepository.GetAsync(b =>
                        new ValueTask<bool>(b.Height == (ulong)localBlockHeight));

                    var localLastBlockHash = string.Empty;
                    if (localLastBlock != null)
                    {
                        localLastBlockHash = BitConverter.ToString(localLastBlock.ToHash());
                    }

                    Task<BlockHashPeer>[] networkPeerTasks;

                    if (_syncWithSeedNodes)
                    {
                        networkPeerTasks =
                            _localNode.SerfClient.SeedNodes.Seeds.Select(seed =>
                                    _networkClient.GetPeerLastBlockHashAsync(peers.First(x =>
                                            x.Value.Host.Contains(seed[..^seed.IndexOf(":", StringComparison.Ordinal)]))
                                        .Value))
                                .ToArray();
                    }
                    else
                    {
                        networkPeerTasks =
                            peers.Values.Select(peer => _networkClient.GetPeerLastBlockHashAsync(peer)).ToArray();
                    }

                    var networkBlockHashes =
                        new List<BlockHashPeer>(await Task.WhenAll(networkPeerTasks))
                            .Where(element => element != null);

                    var networkBlockHashesGrouped = networkBlockHashes
                        .Where(hash => hash != null)
                        .GroupBy(hash => new
                        {
                            Hash = BitConverter.ToString(hash.BlockHash.Hash),
                            hash.BlockHash.Height,
                        })
                        .Select(hash => new
                        {
                            hash.Key.Hash,
                            hash.Key.Height,
                            Count = hash.Count()
                        })
                        .OrderByDescending(element => element.Count)
                        .ThenBy(element => element.Height);

                    var numPeersWithSameHash = networkBlockHashesGrouped
                        .FirstOrDefault(element => element.Hash == localLastBlockHash)?
                        .Count ?? 0;

                    if (!networkBlockHashes.Any())
                    {
                        _logger.Here().Information("No remote block hashes found");
                    }
                    else if (numPeersWithSameHash > networkBlockHashes.Count() / 2.0)
                    {
                        _logger.Here().Information(
                            "Local node has same hash {@Hash} as majority of the network ({@NumSameHash} / {@NumPeers})",
                            localLastBlockHash, numPeersWithSameHash, networkBlockHashes.Count());
                    }
                    else
                    {
                        _logger.Here().Information(
                            "Local node does not have same hash {@Hash} as majority of the network ({@NumSameHash} / {@NumPeers})",
                            localLastBlockHash, numPeersWithSameHash, networkBlockHashes.Count());

                        foreach (var hash in networkBlockHashesGrouped)
                        {
                            _logger.Here().Debug("Hash {@Hash} with height {@Height} found {@Count} times", hash.Hash,
                                hash.Height, hash.Count);
                        }

                        var synchronized = false;
                        foreach (var hash in networkBlockHashesGrouped)
                        {
                            foreach (var peer in networkBlockHashes.Where(element =>
                                BitConverter.ToString(element.BlockHash.Hash) == hash.Hash &&
                                element.BlockHash.Height == hash.Height))
                            {
                                _logger.Here().Debug(
                                    "Synchronizing chain with last block hash {@Hash} and height {@Height} from {@Peer} {@Version} {@Host}",
                                    hash.Hash, hash.Height, peer.Peer.NodeName, peer.Peer.NodeVersion, peer.Peer.Host);

                                synchronized = await Synchronize(peer.Peer.Host, (ulong)localBlockHeight,
                                    (int)hash.Height);
                                if (!synchronized) continue;
                                _logger.Here().Information("Successfully synchronized with {@Peer} {@Version} {@Host}",
                                    peer.Peer.NodeName, peer.Peer.NodeVersion, peer.Peer.Host);

                                break;
                            }

                            if (synchronized)
                            {
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
                finally
                {
                    SyncRunning = false;
                    _logger.Here().Information("Finish Synchronizing");
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<bool> Synchronize(string host, ulong skip, int take)
        {
            Guard.Argument(host, nameof(host)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            try
            {
                var numberOfBatches = (int)Math.Ceiling((double)take / BatchSize);
                numberOfBatches = numberOfBatches == 0 ? 1 : numberOfBatches;
                var networkBlockTasks = new List<Task<IList<Block>>>();
                for (var i = 0; i < numberOfBatches; i++)
                {
                    networkBlockTasks.Add(_networkClient.GetBlocksAsync(host, (ulong)(i + (int)(1 * skip)), BatchSize));
                }

                var blockHeaders = await Task.WhenAll(networkBlockTasks);
                foreach (var blocks in blockHeaders)
                {
                    if (blocks.Any() != true) continue;

                    foreach (var block in blocks.OrderBy(x => x.Height))
                    {
                        try
                        {
                            var verifyBlockHeader = await _validator.VerifyBlock(block);
                            if (verifyBlockHeader != VerifyResult.Succeed)
                            {
                                return false;
                            }

                            var saved = await _unitOfWork.HashChainRepository.PutAsync(block.ToIdentifier(),
                                block);
                            if (saved) continue;

                            _logger.Here().Error("Unable to save block: {@Hash}", block.Hash);
                            return false;
                        }
                        catch (Exception ex)
                        {
                            _logger.Here().Error(ex, "Unable to save block: {@Hash}", block.Hash);
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