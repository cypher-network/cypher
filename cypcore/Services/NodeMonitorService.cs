using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

using Serilog;

using CYPCore.Extensions;
using CYPCore.Helper;

namespace CYPCore.Services
{
    public class NodeMonitorService : BackgroundService
    {
        private ILogger _logger;
        private readonly INodeMonitor _nodeMonitor;

        //        private readonly NodeMonitorConfigurationOptions _configuration;
        private bool _applicationRunning = true;
        private readonly ushort _taskDelay = 10000;

        public NodeMonitorService(INodeMonitor nodeMonitor, IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(NodeMonitorService));
            _nodeMonitor = nodeMonitor;

            applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        }

        private void OnApplicationStopping()
        {
            _logger.Here().Information("Application stopping");
            _nodeMonitor.Stop();
            _applicationRunning = false;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                var nodeMonitorStarted = _nodeMonitor.Start();
                _logger.Here().Debug("Node monitor started: {@NodeMonitorStarted}", nodeMonitorStarted);
                if (!nodeMonitorStarted)
                {
                    return;
                }

                while (_applicationRunning)
                {
                    _nodeMonitor.Listen(cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error in node monitor process");
            }
        }
    }
}