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

        private Timer _runMemPoolTimer;

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
            _runMemPoolTimer?.Change(Timeout.Infinite, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            void Action()
            {
                if (_posMinting.StakingConfigurationOptions.OnOff)
                {
                    _runMemPoolTimer?.Change(Timeout.Infinite, 0);
                    return;
                }

                try
                {
                    _runMemPoolTimer = new Timer(_ =>
                    {
                        var subscribe = _memoryPool
                            .ObserveTake(_posMinting.StakingConfigurationOptions.BlockTransactionCount)
                            .Subscribe(async x =>
                            {
                                var removed = _memoryPool.Remove(x);
                                if (removed == VerifyResult.UnableToVerify)
                                {
                                    _logger.Here()
                                        .Error("Unable to remove the transaction from the memory pool {@TxnId}",
                                            x.TxnId);
                                }
                            });

                        subscribe.Dispose();
                    }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10));

                }
                catch (Exception)
                {
                    // ignored
                }
            }

            Run(Action, stoppingToken);

            return CompletedTask;
        }
    }
}