using System.Linq;
using System.Threading.Tasks;
using Autofac;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Serf;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CYPCore.Services
{
    public class SerfServiceTester : ISerfService
    {
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signing;
        private IHostApplicationLifetime _hostApplicationLifetime;
        private readonly ILogger _logger;

        public SerfServiceTester(ISerfClient serfClient, ISigning signing, ILogger logger)
        {
            _serfClient = serfClient;
            _signing = signing;
            _logger = logger.ForContext("SourceContext", nameof(SerfServiceTester));
        }

        public async Task StartAsync(IHostApplicationLifetime applicationLifetime)
        {
            _logger.Here().Debug("Starting async service");

            _hostApplicationLifetime = applicationLifetime;

            // TODO: testable configuration
            _serfClient.ProcessStarted = true;
        }

        public async Task<bool> JoinSeedNodes(SeedNode seedNode)
        {
            _logger.Here().Debug("Joining seed nodes");
            foreach (var node in seedNode.Seeds)
            {
                _logger.Here().Information("Joining seed node {@Node}", node);
            }

            // TODO: testable configuration, e.g. delays, never-ending connection attempts, etc.

            return true;
        }

        public bool JoinedSeedNodes { get; }

        public void Start()
        {
            _logger.Here().Debug("Starting");
        }
    }
}