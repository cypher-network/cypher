using System.Collections.Generic;
using CYPCore.GossipMesh;
using Dawn;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGossipMemberEventsStore
    {
        void Add(MemberEvent memberEvent);
        MemberEvent[] GetAll();
    }

    /// <summary>
    /// 
    /// </summary>
    public class GossipMemberEventsStore : IGossipMemberEventsStore
    {
        private readonly object _memberEventsLocker = new();
        private readonly List<MemberEvent> _memberEvents = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        public void Add(MemberEvent memberEvent)
        {
            Guard.Argument(memberEvent, nameof(memberEvent)).NotNull();
            lock (_memberEventsLocker)
            {
                _memberEvents.Add(memberEvent);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public MemberEvent[] GetAll()
        {
            lock (_memberEventsLocker)
            {
                return _memberEvents.ToArray();
            }
        }
    }
}