using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Subjects;
using MessagePack;
using rxcypcore.Serf.Messages;

namespace rxcypcore.Serf
{
    [MessagePackObject]
    public class MemberList
    {
        public bool Add(Member member)
        {
            if (!Data.ContainsKey(member.Name))
            {
                Data.TryAdd(member.Name, new List<MemberEndpoint>());
            }

            if (Data.TryGetValue(member.Name, out var endPoints))
            {
                var endPoint = new MemberEndpoint(member);

                var memberEndpoint = endPoints.FirstOrDefault(e => e.Equals(endPoint));
                if (memberEndpoint == null)
                {
                    endPoints.Add(endPoint);
                }
                else
                {
                    memberEndpoint.Status = member.Status;
                }

                _memberEvents.OnNext(new MemberEvent(MemberEvent.EventType.Join, member));
                return true;
            }

            return false;
        }

        public bool Remove(Member member)
        {
            if (Data.TryGetValue(member.Name, out var endPoints))
            {
                var endPoint = new MemberEndpoint(member);

                endPoints.Remove(endPoint);
                if (!endPoints.Any())
                {
                    Data.TryRemove(member.Name, out _);
                }

                _memberEvents.OnNext(new MemberEvent(MemberEvent.EventType.Leave, member));
                return true;
            }

            return false;
        }

        public bool Failed(Member member)
        {
            if (Data.TryGetValue(member.Name, out var endpoints))
            {
                var endPoint = new MemberEndpoint(member);

                var memberEndpoint = endpoints.FirstOrDefault(e => e.Equals(endPoint));
                if (memberEndpoint != null)
                {
                    memberEndpoint.Status = member.Status;
                }
            }

            _memberEvents.OnNext(new MemberEvent(MemberEvent.EventType.Failed, member));
            return true;
        }

        public void Clear() => Data.Clear();

        [Key("Data")] public ConcurrentDictionary<string, List<MemberEndpoint>> Data { get; set; } = new();

        private readonly Subject<MemberEvent> _memberEvents = new();
        public IObservable<MemberEvent> MemberEvents() => _memberEvents;
    }
}