using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using MessagePack;
using rxcypcore.Serf.Messages;

namespace rxcypcore.Serf
{
    [MessagePackObject]
    public class MemberListBase<T> where T : IDictionary<string, IList<MemberEndpoint>>, new()
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
                    Data.Remove(member.Name, out _);
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

        public bool HandleMemberEvent(MemberEvent.EventType eventType, MembersResponse memberData)
        {
            if (memberData == null)
            {
                return false;
            }

            var success = true;

            foreach (var member in memberData?.Members)
            {
                switch (eventType)
                {
                    case MemberEvent.EventType.Join:
                        success &= Add(member);
                        break;

                    case MemberEvent.EventType.Leave:
                        success &= Remove(member);
                        break;

                    case MemberEvent.EventType.Failed:
                        success &= Failed(member);
                        break;

                    default:
                        success = false;
                        break;
                }
            }

            return success;
        }

        public void Clear() => Data.Clear();

        [Key("Data")] public T Data { get; set; } = new();

        private readonly Subject<MemberEvent> _memberEvents = new();
        public IObservable<MemberEvent> MemberEvents() => _memberEvents;
    }

    public class MemberListConcurrent : MemberListBase<ConcurrentDictionary<string, IList<MemberEndpoint>>>
    {
    }

    public class MemberList : MemberListBase<Dictionary<string, IList<MemberEndpoint>>>
    {
    }
}