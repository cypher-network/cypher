// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Gossiper = CYPCore.GossipMesh.Gossiper;
using GossiperOptions = CYPCore.GossipMesh.GossiperOptions;
using IMemberListener = CYPCore.GossipMesh.IMemberListener;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGossipServer
    {
        Task StartAsync();
    }

    /// <summary>
    /// 
    /// </summary>
    public class GossipServer : IGossipServer, IDisposable
    {
        private Gossiper _gossiper;

        private readonly IPEndPoint _nodeIp;
        private readonly IPEndPoint[] _seeds;
        private readonly IMemberListener _memberListener;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeIp"></param>
        /// <param name="seeds"></param>
        /// <param name="memberListener"></param>
        /// <param name="applicationLifetime"></param>
        /// <param name="logger"></param>
        public GossipServer(IPEndPoint nodeIp, IPEndPoint[] seeds, IMemberListener memberListener,
            IHostApplicationLifetime applicationLifetime, ILogger logger)
        {
            _nodeIp = nodeIp;
            _seeds = seeds;
            _memberListener = memberListener;
            _applicationLifetime = applicationLifetime;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task StartAsync()
        {
            try
            {
                await Task.Run(async () => { _gossiper = await StartGossiper(); },
                    _applicationLifetime.ApplicationStopping);
                var loadBalancer = new LoadBalancer($"@tcp://{_nodeIp.Address}:{_nodeIp.Port}", _applicationLifetime,
                    _logger);
                await loadBalancer.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<Gossiper> StartGossiper()
        {
            Gossiper gossiper = null;
            try
            {
                var options = new GossiperOptions
                {
                    SeedMembers = _seeds,
                    MemberListeners = new List<IMemberListener> { _memberListener }
                };
                gossiper = new Gossiper((ushort)_nodeIp.Port, 0x01, (ushort)_nodeIp.Port, options, _cancellationTokenSource.Token, _logger);
                await gossiper.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

            return gossiper;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _gossiper?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}