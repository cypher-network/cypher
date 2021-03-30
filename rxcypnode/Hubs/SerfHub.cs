using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using rxcypcore.Serf;
using rxcypcore.Serf.Messages;

namespace rxcypnode.Hubs
{
    public class SerfHub : Hub
    {
        private readonly ILifetimeScope _lifetimeScope;
        private readonly IDisposable _serfClientStateSubscription;
        private readonly IDisposable _serfMemberEventSubscription;
        private ISerfClient.ClientState _clientState;

        private ISerfClient _serfClient;

        public SerfHub(ILifetimeScope lifetimeScope)
        {
            _lifetimeScope = lifetimeScope;
            _serfClient = _lifetimeScope.Resolve<ISerfClient>();

            _serfClientStateSubscription = _serfClient.State.Subscribe(
                state =>
                {
                    _clientState = state;
                    Send(state);
                });

            _serfMemberEventSubscription = _serfClient.MemberEvents
                .Subscribe(Send);
            
            Init();
        }

        private void Send(ISerfClient.ClientState clientState)
        {
            Clients?.All?.SendAsync("ClientState", clientState);
        }

        private void Init()
        {
            Clients?.All?.SendAsync("Members", _serfClient.Members);
        }

        private void Send(MemberEvent memberEvent)
        {
            Clients?.All.SendAsync("MemberEvent", memberEvent);
        }

        private void Send(IList<Member> members)
        {
            Clients?.All.SendAsync("Members", members);
        }

        public override Task OnConnectedAsync()
        {
            Send(_clientState);
            Send(_serfClient.Members.Values.ToList());
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