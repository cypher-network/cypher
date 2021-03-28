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
    public class PosMintingBackgroundService : BackgroundService
    {
        private readonly IPosMinting _posMinting;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private bool _applicationRunning = true;
        private readonly ILogger _logger;

        public PosMintingBackgroundService(IPosMinting posMinting, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _posMinting = posMinting;
            _applicationLifetime = applicationLifetime;
            _logger = logger.ForContext("SourceContext", nameof(PosMintingBackgroundService));

            _applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
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
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                if (_posMinting.StakingConfigurationOptions.OnOff != true)
                {
                    return;
                }

                await Task.Yield();

                while (_applicationRunning)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        await _posMinting.RunBlockStakingAsync();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                    if (_applicationRunning)
                    {
                        await Task.Delay(10100, stoppingToken);
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Minting process error");
            }
        }
    }
}
