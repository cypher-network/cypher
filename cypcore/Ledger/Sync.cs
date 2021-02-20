//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using CYPCore.Services.Rest;

namespace CYPCore.Ledger
{
    public class Sync : ISync
    {
        public bool SyncRunning { get; private set; }

        private const int BatchSize = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;

        public Sync(IUnitOfWork unitOfWork, IValidator validator, ILocalNode localNode, ILogger<Sync> logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _localNode = localNode;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Check()
        {
            SyncRunning = true;

            try
            {
                _logger.LogInformation("<<< Sync.Check >>>: Checking block height.");

                var peers = await _localNode.GetPeers();
                foreach (var peer in peers)
                {
                    try
                    {
                        var local = new BlockHeight {Height = await _unitOfWork.DeliveredRepository.CountAsync()};
                        var uri = new Uri(peer.Value.Host);
                        
                        RestBlockService blockRestApi = new(uri);
                        var remote = await blockRestApi.GetHeight();

                        _logger.LogInformation(
                            $"<<< Sync.Check >>>: Local node block height ({local.Height}). Network block height ({remote.Height}).");

                        if (local.Height < remote.Height)
                        {
                            await Synchronize(uri, local.Height, remote.Height / peers.Count);
                        }
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
                _logger.LogError($"<<< Sync.Check >>>: {ex}");
            }

            SyncRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Synchronize(Uri uri, long skip, long take)
        {
            var throttler = new SemaphoreSlim(int.MaxValue);
            await throttler.WaitAsync();
            
            try
            {
                var tasks = new List<Task>();
                var numberOfBatches = (int)Math.Ceiling((double)take / BatchSize);

                for (var i = 0; i < numberOfBatches; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var blockRestApi = new RestBlockService(uri);
                            var blockHeaderStream = await blockRestApi.GetBlockHeaders((int)(i * skip), BatchSize);

                            if (blockHeaderStream.Protobufs.Any())
                            {
                                var blockHeaders =
                                    Helper.Util.DeserializeListProto<BlockHeaderProto>(blockHeaderStream.Protobufs);

                                foreach (var blockHeader in blockHeaders)
                                {
                                    try
                                    {
                                        await _validator.GetRunningDistribution();

                                        var verified = await _validator.VerifyBlockHeader(blockHeader);
                                        if (!verified)
                                        {
                                            return;
                                        }

                                        var saved = await _unitOfWork.DeliveredRepository.PutAsync(
                                            blockHeader.ToIdentifier(), blockHeader);
                                        if (!saved)
                                        {
                                            _logger.LogError(
                                                $"<<< Sync.Synchronize >>>: Unable to save block header: {blockHeader.MrklRoot}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogCritical($"<<< Sync.Synchronize >>>: {ex.Message}");
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
                _logger.LogError($"<<< Sync.Synchronize >>>: Failed to synchronize node: {ex}");
            }
            finally
            {
                throttler.Dispose();
            }
        }
    }
}