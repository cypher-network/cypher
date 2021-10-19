//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Services;
using Microsoft.Extensions.DependencyInjection;
using NetMQ;
using NetMQ.Sockets;
using Proto;
using Proto.DependencyInjection;
using Serilog;

namespace CYPCore.Network.Commands
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TResponse"></typeparam>
    /// <typeparam name="TRequest"></typeparam>
    public class RequestSocketCommand<TResponse, TRequest> : ISocketCommand<TRequest>
    {
        private readonly ActorSystem _actorSystem;
        private readonly PID _pid;
        private DealerSocket _dealerSocket;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        public RequestSocketCommand()
        {
            using var serviceScope = ServiceActivator.GetScope();
            _actorSystem = serviceScope.ServiceProvider.GetService<ActorSystem>();
            _logger = serviceScope.ServiceProvider.GetService<ILogger>();
            _pid = _actorSystem?.Root.Spawn(_actorSystem.DI().PropsFor<ShimCommands>());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="backendAddress"></param>
        /// <param name="request"></param>
        public async Task Execute(byte[] key, string backendAddress, TRequest request)
        {
            try
            {
                _dealerSocket = new DealerSocket(backendAddress);
                _dealerSocket.Options.Identity = key;
                var response = await _actorSystem.Root.RequestAsync<TResponse>(_pid, request);
                await _actorSystem.Root.StopAsync(_pid);
                _dealerSocket.SendFrame((await Helper.Util.SerializeAsync(response)).ByteToHex());
                return;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            _dealerSocket?.SendFrame("[NULL]");
        }
    }
}