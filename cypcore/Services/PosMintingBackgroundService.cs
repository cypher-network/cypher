// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using CYPCore.Ledger;

using static System.Threading.Tasks.Task;

namespace CYPCore.Services
{
    public class PosMintingBackgroundService : BackgroundService
    {
        private readonly IPosMinting _posMinting;
        private readonly ILogger _logger;

        private Timer _runStakingTimer;
        private Timer _runStakingWinnerTimer;

        public PosMintingBackgroundService(IPosMinting posMinting, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _posMinting = posMinting;
            _logger = logger.ForContext("SourceContext", nameof(PosMintingBackgroundService));

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");

            _runStakingTimer?.Change(Timeout.Infinite, 0);
            _runStakingWinnerTimer?.Change(Timeout.Infinite, 0);
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
                    _runStakingWinnerTimer = new Timer(_ => _posMinting.RunStakingWinnerAsync(), null,
                        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15));

                    if (_posMinting.StakingConfigurationOptions.OnOff != true) return;

                    _runStakingTimer = new Timer(_ => _posMinting.RunStakingAsync(), null, TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10));
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
