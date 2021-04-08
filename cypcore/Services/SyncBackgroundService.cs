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

        private Timer _runSyncTimer;

        public SyncBackgroundService(ISync sync, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _sync = sync;
            _logger = logger.ForContext("SourceContext", nameof(SyncBackgroundService));

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");
            _runSyncTimer?.Change(Timeout.Infinite, 0);
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
                try
                {
                    _runSyncTimer = new Timer(_ => _sync.Synchronize(), null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(10));
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
