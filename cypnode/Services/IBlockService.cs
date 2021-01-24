// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPNode.Services
{
    public interface IBlockService
    {
        Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take);
        Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks();
        Task<long> GetHeight();
    }
}