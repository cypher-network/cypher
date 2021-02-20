// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

using CYPCore.Extensions;
using Serilog;

using CYPCore.Network;

namespace CYPCore.Services
{
    public class LocalNodeBackgroundService : BackgroundService
    {
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;

        public LocalNodeBackgroundService(ILocalNode localNode, ILogger logger)
        {
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(LocalNodeBackgroundService));
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
                _logger.Here().Information("Bootstrapping seed nodes...");

                while (true)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await _localNode.BootstrapNodes();
                    await Task.Delay(3000, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while bootstrapping nodes");
            }
        }
    }
}
