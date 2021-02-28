using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Serilog;

using CYPCore.Extensions;
using CYPCore.Models;

namespace CYPCore.Helper
{
    public interface INodeMonitor
    {
        public bool Start();
        public void Stop();
        public void Listen(CancellationToken cancellationToken);
    }

    public class NodeMonitor : INodeMonitor
    {
        private readonly ILogger _logger;
        private readonly NodeMonitorConfigurationOptions _configuration;
        private readonly TcpListener _listener;

        public NodeMonitor(NodeMonitorConfigurationOptions configuration, ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(NodeMonitor));
            _configuration = configuration;

            if (_configuration.Enabled)
            {
                try
                {
                    var ipAddress = IPAddress.Parse(_configuration.Listening);
                    var endPoint = new IPEndPoint(ipAddress, _configuration.Port);
                    _listener = new TcpListener(endPoint);
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Cannot initialize node monitor service");
                }
            }
        }

        public bool Start()
        {
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot start TCP listener");
                return false;
            }

            return true;
        }

        public void Stop()
        {
            if (_configuration.Enabled)
            {
                try
                {
                    _listener.Stop();
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex, "Error while stopping TCP listener");
                }
            }
        }

        public async void Listen(CancellationToken cancellationToken)
        {
            var buffer = new byte[_listener.Server.ReceiveBufferSize];

            try
            {
                var client = await AcceptAsync(cancellationToken);
                _logger.Here().Information("Client connected");

                while (client.Connected)
                {
                    var bytesReceived = await client.GetStream().ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesReceived == 0)
                    {
                        _logger.Here().Debug("Received 0 bytes, connection terminated");
                        break;
                    }

                    _logger.Here().Debug("Received {@NumBytes} bytes", bytesReceived);
                }
                _logger.Here().Information("Client disconnected");

            }
            catch (OperationCanceledException)
            {
                _logger.Here().Debug("Listener interrupted");
            }
        }

        private async Task<TcpClient> AcceptAsync(CancellationToken cancellationToken)
        {
            await using (cancellationToken.Register(_listener.Stop))
            {
                try
                {
                    return await _listener.AcceptTcpClientAsync();
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    _logger.Here().Information("Operation cancelled");
                    throw new OperationCanceledException();
                }
                catch (ObjectDisposedException ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.Here().Information("Operation cancelled");
                    throw new OperationCanceledException();
                }
            }
        }
    }
}