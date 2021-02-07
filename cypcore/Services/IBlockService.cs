// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services
{
    public interface IBlockService
    {
        [Post("/header/block")]
        Task<bool> AddBlock(byte[] payload);

        [Post("/header/blocks")]
        Task AddBlocks(byte[] payloads);

        [Get("/header/vout/{txnid}")]
        Task<byte[]> GetVout(byte[] txnId);

        [Get("/header/blocks/{skip}/{take}")]
        Task<IEnumerable<BlockHeaderProto>> GetBlockHeaders(int skip, int take);

        [Get("/header/safeguardblocks")]
        Task<IEnumerable<BlockHeaderProto>> GetSafeguardBlocks();

        [Get("/header/height")]
        Task<long> GetHeight();
    }
}
