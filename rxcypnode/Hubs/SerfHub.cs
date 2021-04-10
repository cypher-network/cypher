using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.SignalR;
using rxcypcore.Extensions;
using rxcypcore.Serf;
using rxcypcore.Serf.Messages;
using Serilog;

namespace rxcypnode.Hubs
{
    public class SerfHub : Hub
    {
        private readonly ILifetimeScope _lifetimeScope;
        private readonly IDisposable _serfClientStateSubscription;
        private readonly IDisposable _serfMemberEventSubscription;
        private ISerfClient.ClientState _clientState;
        private readonly ILogger _logger;

        private readonly ISerfClient _serfClient;

        public SerfHub(ILifetimeScope lifetimeScope, ILogger logger)
        {
            _lifetimeScope = lifetimeScope;
            _logger = logger.ForContext("SourceContext", nameof(SerfHub));
            _logger.Here().Information("Registering hub for member events");
            _serfClient = _lifetimeScope.Resolve<ISerfClient>();

            _serfClientStateSubscription = _serfClient.State.Subscribe(
                state =>
                {
                    _clientState = state;
                    Send(state);
                });

            _serfMemberEventSubscription = _serfClient.Members.MemberEvents
                .Subscribe(Send);

            Init();
        }

        private async void Send(ISerfClient.ClientState clientState)
        {
            if (Clients == null) return;
            _logger.Here().Information("Sending client state");
            await Clients.All.SendAsync("ClientState", clientState);
        }

        private async void Init()
        {
            if (Clients == null) return;
            await Clients.All.SendAsync("Members", _serfClient.Members);
        }

        private async void Send(MemberEvent memberEvent)
        {
            if (Clients == null) return;
            _logger.Here().Information("Sending member event");
            await Clients.All.SendAsync("MemberEvent", memberEvent);
        }

        private async void Send(MemberList members)
        {
            if (Clients == null) return;
            _logger.Here().Information("Sending members");
            await Clients.All.SendAsync("Members", members);
        }

        public override Task OnConnectedAsync()
        {
            Send(_clientState);
            Send(_serfClient.Members);
            return base.OnConnectedAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serfClientStateSubscription?.Dispose();
                _serfMemberEventSubscription?.Dispose();
                _lifetimeScope?.Dispose();
            }

            base.Dispose(disposing);
        }

        public void Serf(SerfMethod method)
        {
            Console.WriteLine("Serf executed");
            switch (method)
            {
                case SerfMethod.Join:
                    Task.Run(() => _serfClient.Join());
                    break;
                case SerfMethod.Leave:
                    Task.Run(() => _serfClient.Leave());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(method), method, null);
            }
        }

        public enum SerfMethod
        {
            Join,
            Leave
        }
    }
}