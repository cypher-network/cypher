// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Threading.Tasks;

using CYPCore.Serf.Message;

namespace CYPCore.Services
{
    public interface IMembershipService
    {
        Task<int> GetCount();
        Task<IEnumerable<Members>> GetMembers();
        Task<byte[]> GetPublicKey();
        void Ready();
    }
}
