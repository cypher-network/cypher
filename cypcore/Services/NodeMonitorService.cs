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
        private readonly ILogger _logger;
        private readonly INodeMonitor _nodeMonitor;
        private bool _applicationRunning = true;
        private const int ConnectionRetryDelay = 1000; // ms;

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
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var nodeMonitorStarted = _nodeMonitor.Start();
                _logger.Here().Debug("Node monitor started: {@NodeMonitorStarted}", nodeMonitorStarted);
                if (!nodeMonitorStarted)
                {
                    return;
                }

                while (_applicationRunning && !cancellationToken.IsCancellationRequested)
                {
                    await _nodeMonitor.Connect(cancellationToken);
                    _logger.Here().Debug("Cannot connect to tester socket, retrying in {@Delay} ms", ConnectionRetryDelay);
                    Task.Delay(ConnectionRetryDelay, cancellationToken).GetAwaiter().GetResult();
                }
            }
            catch (TaskCanceledException)
            {
                _logger.Here().Debug("NodeMonitorService canceled");
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error in node monitor process");
            }
        }
    }
}