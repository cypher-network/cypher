using System;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.GossipMesh;
using Dawn;
using Serilog;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public class MemberListener : IMemberListener
    {
        private readonly IGossipMemberStore _gossipMemberStore;
        private readonly IGossipMemberEventsStore _gossipMemberEvents;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gossipMemberStore"></param>
        /// <param name="gossipMemberEvents"></param>
        /// <param name="logger"></param>
        public MemberListener(IGossipMemberStore gossipMemberStore, IGossipMemberEventsStore gossipMemberEvents, ILogger logger)
        {
            _gossipMemberStore = gossipMemberStore;
            _gossipMemberEvents = gossipMemberEvents;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberEvent"></param>
        public Task MemberUpdatedCallback(MemberEvent memberEvent)
        {
            Guard.Argument(memberEvent, nameof(memberEvent)).NotNull();
            try
            {
                if (memberEvent.IP.ToString() is "0.0.0.0" or "::0") return Task.CompletedTask;
                _gossipMemberEvents.Add(memberEvent);
                _gossipMemberStore.AddOrUpdateNode(memberEvent);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return Task.CompletedTask;
        }
    }
}