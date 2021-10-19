//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Network.Commands;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Dawn;
using MessagePack;
using Microsoft.Extensions.Hosting;
using NetMQ;
using NetMQ.Sockets;
using Proto;
using Proto.DependencyInjection;
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
        Task Synchronize();
        void Dispose();
    }

    /// <summary>
    /// 
    /// </summary>
    public class Sync : ISync, IDisposable
    {
        private const int SocketTryReceiveFromMilliseconds = 5000;
        private const uint SyncEveryFromMinutes = 10;
        private const uint SyncStartUpTimeFromMilliseconds = 3000;

        public bool SyncRunning { get; private set; }

        private readonly ActorSystem _actorSystem;
        private readonly PID _pidShimCommand;
        private readonly PID _pidLocalNode;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly IHostApplicationLifetime _applicationLifetime;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="validator"></param>
        /// <param name="applicationLifetime"></param>
        /// <param name="logger"></param>
        public Sync(ActorSystem actorSystem, IValidator validator,
            IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidShimCommand = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<ShimCommands>());
            _pidLocalNode = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<LocalNode>());
            _validator = validator;
            _applicationLifetime = applicationLifetime;
            _logger = logger.ForContext("SourceContext", nameof(Sync));
            Observable.Timer(TimeSpan.FromMilliseconds(SyncStartUpTimeFromMilliseconds),
                TimeSpan.FromMinutes(SyncEveryFromMinutes)).Subscribe(_ =>
            {
                async void Action()
                {
                    await Synchronize();
                }

                var task = new Task(Action);
                task.Start();
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Synchronize()
        {
            if (SyncRunning) return;
            _logger.Here().Information("SYNCHRONIZATION [STARTED]");
            await Task.Factory.StartNew(async () =>
            {
                try
                {
                    using (_lock.Write())
                    {
                        SyncRunning = true;
                    }

                    var blockCountResponse =
                        await _actorSystem.Root.RequestAsync<BlockCountResponse>(_pidShimCommand,
                            new BlockCountRequest());

                    _logger.Here().Information("OPENING block height [{@height}]", blockCountResponse.Count);

                    const int retryCount = 5;
                    var currentRetry = 0;
                    var jitter = new Random();
                    for (; ; )
                    {
                        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
                        {
                            return;
                        }

                        var forward = await WaitForPeers(currentRetry, retryCount, jitter);
                        if (forward) break;
                        currentRetry++;
                    }

                    var peersMemStoreResponse =
                        await _actorSystem.Root.RequestAsync<PeersMemStoreResponse>(_pidLocalNode,
                            new PeersMemStoreRequest(true));
                    var snapshot = peersMemStoreResponse.MemStore.GetMemSnapshot().SnapshotAsync();
                    var peers = await snapshot.ToArrayAsync();
                    if (peers.Any())
                    {
                        var synchronized = false;
                        blockCountResponse =
                            await _actorSystem.Root.RequestAsync<BlockCountResponse>(_pidShimCommand,
                                new BlockCountRequest());

                        //TODO: Divide/peer blocks into chunks.  Currently getting everything from the first (SWIM) selected peer.
                        foreach (var peer in peers.Select(x => x.Value))
                        {
                            if (peer is null)
                            {
                                _logger.Here().Error("Peer returned as null");
                                continue;
                            }

                            if (blockCountResponse.Count >= (long)peer.BlockHeight)
                            {
                                var localNodeDetailsResponse =
                                    await _actorSystem.Root.RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode,
                                        new LocalNodeDetailsRequest());
                                _logger.Here().Information(
                                    "[LOCAL node:({@localNodeId}) block height: ({@LocalHeight})] > or = [REMOTE node:({@remoteNodeId}) block height: ({@RemoteBlockHeight})])",
                                    localNodeDetailsResponse.Identifier, blockCountResponse.Count, peer.ClientId,
                                    peer.BlockHeight);
                                _logger.Here().Information("[CONTINUE]");
                                continue;
                            }

                            synchronized = await Synchronize(peer.Listening, (ulong)blockCountResponse.Count,
                                (int)peer.BlockHeight);
                            if (!synchronized) continue;
                            _logger.Here().Information("Successfully SYNCHRONIZED with {@Host}", peer.RestApi);
                            break;
                        }

                        if (!synchronized)
                        {
                            _logger.Here().Warning("Unable to SYNCHRONIZE WITH REMOTE PEER(S)");
                            blockCountResponse =
                                await _actorSystem.Root.RequestAsync<BlockCountResponse>(_pidShimCommand,
                                    new BlockCountRequest());
                            _logger.Here().Information("LOCAL NODE block height: ({@LocalHeight})",
                                blockCountResponse.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Error while checking");
                }
                finally
                {
                    using (_lock.Write())
                    {
                        SyncRunning = false;
                    }

                    _logger.Here().Information("SYNCHRONIZATION [ENDED]");
                }
            }, _applicationLifetime.ApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="currentRetry"></param>
        /// <param name="retryCount"></param>
        /// <param name="jitter"></param>
        /// <returns></returns>
        private async Task<bool> WaitForPeers(int currentRetry, int retryCount, Random jitter)
        {
            Guard.Argument(currentRetry, nameof(currentRetry)).NotNegative();
            Guard.Argument(retryCount, nameof(retryCount)).NotNegative();
            Guard.Argument(jitter, nameof(jitter)).NotNull();
            var peersMemStoreResponse = await _actorSystem.Root.RequestAsync<PeersMemStoreResponse>(_pidLocalNode,
                new PeersMemStoreRequest(true));
            var snapshot = peersMemStoreResponse.MemStore.GetMemSnapshot().SnapshotAsync();
            var peers = await snapshot.ToArrayAsync();
            if (!peers.Any())
            {
                _logger.Here().Warning("Waiting for peers... Retrying");
            }

            if (currentRetry >= retryCount || peers?.Length != 0)
            {
                return true;
            }

            var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, currentRetry)) +
                             TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
            await Task.Delay(retryDelay);
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listening"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public async Task<bool> Synchronize(string listening, ulong skip, int take)
        {
            Guard.Argument(listening, nameof(listening)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            var isSynchronized = false;
            try
            {
                var blocks = await FetchBlocks(listening, skip, take);
                if (blocks.Any() != true) return false;
                if (skip == 0)
                {
                    _logger.Here().Warning("FIRST TIME THE CHAIN IS BOOTSTRAPPING");
                }
                else
                {
                    _logger.Here().Information("CONTINUE BOOTSTRAPPING");
                    //TODO: Apply sliding window algorithm.
                    var forkingBlocks = await FetchBlocks(listening, 0, (int)skip);
                    if (forkingBlocks.Any() != true) return false;
                    blocks.AddRange(forkingBlocks);
                    _logger.Here().Information("CHECKING fork rule");
                    var verifyForkRule = await _validator.VerifyForkRule(blocks.OrderBy(x => x.Height).ToArray());
                    if (verifyForkRule == VerifyResult.UnableToVerify)
                    {
                        _logger.Here().Information("Fork rule check [UNABLE TO VERIFY]");
                        return false;
                    }

                    _logger.Here().Information("Fork rule check [OK]");
                }

                _logger.Here().Information("SYNCHRONIZING ({@blockCount}) Block(s)", blocks.Count);
                foreach (var block in blocks.OrderBy(x => x.Height).Skip((int)skip))
                {
                    try
                    {
                        _logger.Here().Information("SYNCING block height: ({@height})", block.Height);
                        var verifyBlockHeader = await _validator.VerifyBlock(block);
                        if (verifyBlockHeader != VerifyResult.Succeed)
                        {
                            return false;
                        }

                        _logger.Here().Information("SYNCHRONIZED [OK]");
                        var saveBlockResponse =
                            await _actorSystem.Root.RequestAsync<SaveBlockResponse>(_pidShimCommand,
                                new SaveBlockRequest(block));
                        if (saveBlockResponse.OK) continue;
                        _logger.Here().Error("Unable to save block: {@Hash}", block.Hash);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.Here().Error(ex, "Unable to save block: {@Hash}", block.Hash);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "SYNCHRONIZATION [FAILED]");
                return false;
            }
            finally
            {
                var blockCountResponse =
                    await _actorSystem.Root.RequestAsync<BlockCountResponse>(_pidShimCommand,
                        new BlockCountRequest());
                _logger.Here().Information("Local node block height set to ({@LocalHeight})", blockCountResponse.Count);
                if (blockCountResponse.Count == take) isSynchronized = true;
            }

            return isSynchronized;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="listening"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        private Task<List<Block>> FetchBlocks(string listening, ulong skip, int take)
        {
            Guard.Argument(listening, nameof(listening)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();
            _logger.Here().Information("Synchronizing with {@host} ({@skip})/({@take})", listening, skip, take);
            try
            {
                _logger.Here().Information("Fetching ({@range}) block(s)", take - (int)skip);
                using var dealerSocket = new DealerSocket($">tcp://{listening}");
                dealerSocket.Options.Identity = Util.RandomDealerIdentity();
                var message = new NetMQMessage();
                message.Append(CommandMessage.GetBlocks.ToString());
                message.Append(MessagePackSerializer.Serialize(new Parameter[]
                {
                    new() { Value = skip.ToBytes() }, new() { Value = take.ToBytes() }
                }));
                dealerSocket.SendMultipartMessage(message);
                if (dealerSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(SocketTryReceiveFromMilliseconds),
                    out var msg))
                {
                    var blocksResponse = MessagePackSerializer.Deserialize<BlocksResponse>(msg.HexToByte());
                    _logger.Here().Information("Finished with ({@blockCount}) block(s)", blocksResponse.Blocks.Count);
                    return Task.FromResult(blocksResponse.Blocks);
                }

                _logger.Here().Warning("Dead message {@peer}", listening);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return Task.FromResult<List<Block>>(null);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _lock?.Dispose();
            Console.WriteLine("Stopping");
        }
    }
}