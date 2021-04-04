//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using CYPCore.Extensions;
using Serilog;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using CYPCore.Services.Rest;
using Dawn;
using FlatSharp;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISync
    {
        bool SyncRunning { get; }
        Task Fetch(Uri uri, long skip, long take);
        Task Synchronize();
    }
    /// <summary>
    /// 
    /// </summary>
    public class Sync : ISync
    {
        public bool SyncRunning { get; private set; }

        private const int BatchSize = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;

        public Sync(IUnitOfWork unitOfWork, IValidator validator, ILocalNode localNode, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(Sync));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Synchronize()
        {
            SyncRunning = true;

            try
            {
                _logger.Here().Information("Checking block height");

                var peers = await _localNode.GetPeers();
                foreach (var (_, peer) in peers)
                {
                    try
                    {
                        var networkBlockHeight = await _localNode.PeerBlockHeight(peer);

                        _logger.Here()
                            .Information(
                                "Local node block height ({@LocalHeight}). Network block height ({NetworkHeight})",
                                networkBlockHeight.Local.Height, networkBlockHeight.Remote.Height);

                        if (networkBlockHeight.Local.Height == networkBlockHeight.Remote.Height)
                        {
                            continue;
                        }

                        _logger.Here().Information("Fetching blocks");

                        await Fetch(new Uri(peer.Host), networkBlockHeight.Local.Height,
                            networkBlockHeight.Remote.Height / peers.Count);

                        var localHeight = await _unitOfWork.HashChainRepository.CountAsync();

                        _logger.Here()
                            .Information(
                                "Local node block height ({@LocalHeight}). Network block height ({NetworkHeight})",
                                localHeight, networkBlockHeight.Remote.Height);
                    }
                    catch (HttpRequestException)
                    {
                    }
                    catch (TaskCanceledException)
                    {
                    }
                    catch (Refit.ApiException)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while checking");
            }

            SyncRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Fetch(Uri uri, long skip, long take)
        {
            Guard.Argument(uri, nameof(uri)).NotNull();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            var throttler = new SemaphoreSlim(int.MaxValue);
            await throttler.WaitAsync();

            try
            {
                var tasks = new List<Task>();
                var numberOfBatches = (int)Math.Ceiling((double)take / BatchSize);
                numberOfBatches = numberOfBatches == 0 ? 1 : numberOfBatches;

                for (var i = 0; i < numberOfBatches; i++)
                {
                    var i1 = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var blockRestApi = new RestBlockService(uri, _logger);
                            var blockHeaderStream = await blockRestApi.GetBlockHeaders((int)(i1 * skip), BatchSize);

                            if (blockHeaderStream.FlatBuffer.Any())
                            {
                                var blockHeaders =
                                    FlatBufferSerializer.Default.Parse<GenericList<BlockHeaderProto>>(blockHeaderStream
                                        .FlatBuffer);

                                var verifyForkRule = await _validator.VerifyForkRule(blockHeaders.Data.ToArray());
                                if (verifyForkRule == VerifyResult.UnableToVerify)
                                {
                                    _logger.Here().Error("Unable to verify fork rule from: {@Host}", uri.Host);
                                    return;
                                }

                                foreach (var blockHeader in blockHeaders.Data.OrderBy(x => x.Height))
                                {
                                    try
                                    {
                                        var verifyBlockHeader = await _validator.VerifyBlockHeader(blockHeader);
                                        if (verifyBlockHeader != VerifyResult.Succeed)
                                        {
                                            return;
                                        }

                                        var saved = await _unitOfWork.HashChainRepository.PutAsync(
                                            blockHeader.ToIdentifier(), blockHeader);
                                        if (!saved)
                                        {
                                            _logger.Here().Error("Unable to save block: {@MerkleRoot}",
                                                blockHeader.MerkelRoot);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Here().Error(ex, "Unable to save block: {@MerkleRoot}",
                                            blockHeader.MerkelRoot);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Failed to synchronize node");
            }
            finally
            {
                throttler.Dispose();
            }
        }
    }
}