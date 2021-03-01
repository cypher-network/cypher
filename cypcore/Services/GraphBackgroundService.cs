// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Consensus;
using CYPCore.Extensions;
using CYPCore.Extentions;
using CYPCore.Ledger;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CYPCore.Services
{
    public class GraphBackgroundService : BackgroundService
    {
        private readonly IGraph _graph;
        private readonly PbftOptions _pBftOptions;
        private readonly ILogger _logger;

        private bool _applicationRunning = true;

        public GraphBackgroundService(IGraph graph, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _graph = graph;
            _pBftOptions = new PbftOptions();
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
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _graph.Ready(1);

                while (_applicationRunning)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    if (!_applicationRunning) continue;

                    var interval = _pBftOptions.RoundInterval;
                    var start = DateTime.Now.Truncate(interval);
                    var workDuration = _pBftOptions.InitialWorkDuration;
                    var next = start.Add(new TimeSpan(interval));
                    var workStart = next.Add(new TimeSpan(-workDuration));
                    var timeSpan = workStart.Subtract(DateTime.Now);

                    await Task.Delay((int)Math.Abs(timeSpan.TotalMilliseconds), stoppingToken);

                    _graph.WriteAsync(100, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                _graph.StopWriter();
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error in graph process");
            }
        }
    }
}