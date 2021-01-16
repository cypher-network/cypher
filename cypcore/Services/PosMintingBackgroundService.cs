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
    public class PosMintingBackgroundService : BackgroundService
    {
        private readonly IPosMinting _posMinting;
        private readonly ILogger _logger;

        public PosMintingBackgroundService(IPosMinting posMinting, ILogger<PosMintingBackgroundService> logger)
        {
            _posMinting = posMinting;
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
                if (_posMinting.StakingConfigurationOptions.OnOff != true)
                {
                    return;
                }

                while (true)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await _posMinting.RunStakingBlockAsync();

                    await Task.Delay(10100, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< PosMintingBackgroundService.ExecuteAsync >>>: {ex}");
            }
        }
    }
}
