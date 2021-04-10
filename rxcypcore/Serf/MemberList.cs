using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Subjects;
using rxcypcore.Serf.Messages;

namespace rxcypcore.Serf
{
    public class MemberList : ConcurrentDictionary<string, List<IPEndPoint>>
    {
        public bool Add(Member member)
        {
            if (!ContainsKey(member.Name))
            {
                TryAdd(member.Name, new List<IPEndPoint>());
            }

            if (TryGetValue(member.Name, out var endPoints))
            {
                var endPoint = GetEndpoint(member);

                if (endPoints.FirstOrDefault(e => e.Equals(endPoint)) == null)
                {
                    endPoints.Add(endPoint);
                    _memberEvents.OnNext(new MemberEvent(MemberEvent.EventType.Join, member));
                    return true;
                }
            }

            return false;
        }

        public bool Remove(Member member)
        {
            if (TryGetValue(member.Name, out var endPoints))
            {
                var endPoint = GetEndpoint(member);

                endPoints.Remove(endPoint);
                if (!endPoints.Any())
                {
                    TryRemove(member.Name, out _);
                }

                _memberEvents.OnNext(new MemberEvent(MemberEvent.EventType.Leave, member));
                return true;
            }

            return false;
        }

        private IPEndPoint GetEndpoint(Member member)
        {
            // TODO: Find out why some IPv4 member addresses contain more than 4 bytes
            // TODO: Add IPv6 peers
            member.Address = member.Address.TakeLast(4).ToArray();
            return new IPEndPoint(new IPAddress(member.Address), member.Port);
        }

        private readonly Subject<MemberEvent> _memberEvents = new();
        public IObservable<MemberEvent> MemberEvents => _memberEvents;
    }
}