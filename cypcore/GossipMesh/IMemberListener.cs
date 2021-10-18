using System.Threading.Tasks;

namespace CYPCore.GossipMesh
{
    /// <summary>
    /// 
    /// </summary>
    public interface IMemberListener
    {
        Task MemberUpdatedCallback(MemberEvent memberEvent);
    }
}