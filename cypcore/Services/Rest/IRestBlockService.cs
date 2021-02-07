// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using Refit;

using CYPCore.Models;

namespace CYPCore.Services.Rest
{
    public interface IRestBlockService
    {
        [Get("/header/height")]
        Task<BlockHeight> GetHeight();

        [Get("/header/blocks/{skip}/{take}")]
        Task<ProtobufStream> GetBlockHeaders(int skip, int take);

        [Post("/header/block")]
        Task<WebResponse> AddBlock(byte[] payload);
    }
}
