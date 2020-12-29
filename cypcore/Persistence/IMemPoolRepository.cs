// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPCore.Persistence
{
    public interface IMemPoolRepository : IRepository<MemPoolProto>
    {
        Task<MemPoolProto> PreviousOrDefaultAsync(ulong node, ulong round);
        Task<MemPoolProto> PreviousOrDefaultAsync(byte[] hash, ulong node, ulong round);
        Task<List<MemPoolProto>> MoreAsync(IEnumerable<MemPoolProto> memPools);
        Task IncludeAllAsync(IEnumerable<MemPoolProto> memPools, ulong currentNode);
        Task<MemPoolProto> Get(byte[] hash, ulong node, ulong round);

        string Table { get; }
    }
}
