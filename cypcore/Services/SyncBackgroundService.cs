// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;
using CYPCore.Extensions;
using CYPCore.Ledger;
using static System.Threading.Tasks.Task;

namespace CYPCore.Services
{
    public class SyncBackgroundService : BackgroundService
    {
        private readonly ISync _sync;
        private readonly ILogger _logger;

        public SyncBackgroundService(ISync sync, ILogger logger)
        {
            _sync = sync;
            _logger = logger.ForContext("SourceContext", nameof(SyncBackgroundService));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Yield();

                while (true)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        await _sync.Synchronize();
                        await Delay(600000, stoppingToken);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                    while (_sync.SyncRunning)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        await Delay(6000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Sync process error");
            }
        }
    }
}
