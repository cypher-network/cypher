using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using rxcypcore.Extensions;
using rxcypcore.Serf;
using Serilog;

namespace rxcypcore.Services
{
    public class NodeService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly ISerfClient _serfClient;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public NodeService(IHostApplicationLifetime hostApplicationLifetime, ISerfClient serfClient, ILogger logger)
        {
            _hostApplicationLifetime = hostApplicationLifetime;
            _serfClient = serfClient;
            _logger = logger.ForContext("SourceContext", nameof(NodeService));

            _hostApplicationLifetime.ApplicationStopping.Register(() =>
            {
                _logger.Here().Information("Stopping application");
                _serfClient.Stop();
            });
        }
        
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
        {
            try
            {
                _logger.Information("Starting Serf client");
                _serfClient.Start();
            }
            // Log the exception before stack unwinding
            catch (Exception ex) when (False(() => _logger.Fatal(ex, "Non-recoverable error in node service")))
            {
                throw;
            }
            finally
            {
                _logger.Here().Information("Requesting application stop");
            }
        });

        private static bool False(Action action)
        {
            action();
            return false;
        }
    }
}