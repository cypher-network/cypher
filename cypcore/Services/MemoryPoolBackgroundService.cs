// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Ledger;
using Microsoft.Extensions.Hosting;
using Serilog;
using static System.Threading.Tasks.Task;

namespace CYPCore.Services
{
    public class MemoryPoolBackgroundService : BackgroundService
    {
        private readonly IMemoryPool _memoryPool;
        private readonly IPosMinting _posMinting;
        private readonly ILogger _logger;

        private bool _applicationRunning = true;

        public MemoryPoolBackgroundService(IMemoryPool memoryPool, IPosMinting posMinting, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _memoryPool = memoryPool;
            _posMinting = posMinting;
            _logger = logger.ForContext("SourceContext", nameof(GraphBackgroundService));

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");
            _applicationRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                if (_posMinting.StakingConfigurationOptions.OnOff)
                {
                    return;
                }

                await Yield();

                while (_applicationRunning)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    if (!_applicationRunning) continue;

                    try
                    {
                        var subscribe = _memoryPool.ObserveTake(_posMinting.StakingConfigurationOptions.BlockTransactionCount)
                            .Subscribe(async x =>
                            {
                                var removed = _memoryPool.Remove(x.TxnId);
                                if (removed == VerifyResult.UnableToVerify)
                                {
                                    _logger.Here().Error("Unable to remove the transaction from the memory pool {@TxnId}", x.TxnId);
                                }
                            });

                        subscribe.Dispose();

                        if (_applicationRunning)
                        {
                            await Delay(10100, stoppingToken);
                        }
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "MemoryPool background service error");
            }
        }
    }
}