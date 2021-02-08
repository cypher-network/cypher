// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPCore.Network
{
    public interface ILocalNode
    {
        Task BootstrapNodes();
        Task Broadcast(byte[] data, TopicType topicType);
        Task Send(byte[] data, TopicType topicType, string host);
        void Ready();
    }
}