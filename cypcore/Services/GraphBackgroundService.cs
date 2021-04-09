// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Consensus;
using CYPCore.Extensions;
using CYPCore.Ledger;
using Microsoft.Extensions.Hosting;
using Serilog;
using static System.Threading.Tasks.Task;
using Timeout = System.Threading.Timeout;

namespace CYPCore.Services
{
    public class GraphBackgroundService : BackgroundService
    {
        private readonly IGraph _graph;
        private readonly PbftOptions _pBftOptions;
        private readonly ILogger _logger;

        private Timer _runGraphReadyTimer;
        private Timer _runGraphWriteTimer;
        private Timer _runGraphKeepAliveNodesTimer;

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
            _runGraphReadyTimer?.Change(Timeout.Infinite, 0);
            _runGraphWriteTimer?.Change(Timeout.Infinite, 0);
            _runGraphKeepAliveNodesTimer?.Change(Timeout.Infinite, 0);
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
                    var subscriber = _graph.StartProcessing().GetAwaiter().GetResult();

                    _runGraphReadyTimer = new Timer(_ => _graph.Ready(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
                    _runGraphWriteTimer = new Timer(_ => _graph.WriteAsync(100), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(13));
                    _runGraphKeepAliveNodesTimer = new Timer(_ => _graph.RemoveUnresponsiveNodesAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
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