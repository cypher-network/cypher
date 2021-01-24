// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using CYPCore.Ledger;

namespace CYPCore.Services
{
    public class SyncBackgroundService : BackgroundService
    {
        private readonly ISync _sync;
        private readonly ILogger _logger;

        public SyncBackgroundService(ISync sync, ILogger<SyncBackgroundService> logger)
        {
            _sync = sync;
            _logger = logger;
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
                _logger.LogError($"<<< SyncService >>>: {ex}");
            }
        }
    }
}
