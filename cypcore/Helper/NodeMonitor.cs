using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Serf;
using CYPCore.Terminal;
using Microsoft.CodeAnalysis;

namespace CYPCore.Helper
{
    public interface INodeMonitor
    {
        public bool Start();
        public void Stop();
        public Task<bool> Connect(CancellationToken cancellationToken);
    }

    public class NodeMonitor : INodeMonitor
    {
        private readonly ILogger _logger;
        private readonly NodeMonitorConfigurationOptions _configuration;

        private readonly IPEndPoint _endPoint;
        private readonly Socket _client;

        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly MainWindow _mainWindow;
        private readonly Thread _mainWindowThread;

        private ISerfRxClient _serfRxClient;
        private IDisposable _serfRxClientStateObserver;

        public NodeMonitor(NodeMonitorConfigurationOptions configuration, ISerfRxClient serfRxClient, ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(NodeMonitor));
            _configuration = configuration;

            _cancellationTokenSource = new CancellationTokenSource();
            _mainWindow = new MainWindow(_cancellationTokenSource);
            _mainWindowThread = new Thread(_mainWindow.Start)
            {
                Priority = ThreadPriority.AboveNormal
            };

            _serfRxClient = serfRxClient;
            _serfRxClientStateObserver = serfRxClient.State.Subscribe(
                clientState =>
                {
                    if (_mainWindow != null)
                    {
                        _mainWindow.StatusBar.Text = clientState.ToString();
                    }
                });

            if (_configuration.Tester.Enabled)
            {
                try
                {
                    var ipAddress = IPAddress.Parse(_configuration.Tester.Listening);
                    _endPoint = new IPEndPoint(ipAddress, _configuration.Tester.Port);
                    _client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Cannot initialize node monitor service");
                }
            }
        }

        public bool Start()
        {
            _mainWindowThread.Start();

            return true;
        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                _client.Disconnect(true);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while stopping TCP listener");
            }
        }

        public async Task<bool> Connect(CancellationToken cancellationToken)
        {
            try
            {
                await _client.ConnectAsync(_endPoint, cancellationToken);
                _logger.Here().Information("Client connected: {@Connected}", _client.Connected);

            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot connect to tester {@Address}:{@Port} ",
                    _endPoint.Address.ToString(),
                    _endPoint.Port);

                return false;
            }

            return true;
        }
    }
}
