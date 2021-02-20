// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Microsoft.Extensions.Hosting;

using Serilog;

using CYPCore.Ledger;

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
        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (true)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await _sync.Check();

                    await Task.Delay(600000, stoppingToken);

                    while (_sync.SyncRunning)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        await Task.Delay(6000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Background sync service error");
            }
        }
    }
}
