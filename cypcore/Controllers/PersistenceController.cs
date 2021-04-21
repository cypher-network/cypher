// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Serilog;

using CYPCore.Persistence;

namespace CYPCore.Controllers
{
    [Route("db")]
    [ApiController]
    public class PersistenceController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;

        public PersistenceController(IUnitOfWork unitOfWork, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger.ForContext("SourceContext", nameof(PersistenceController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("blockgraphs", Name = "GetBlockGraphs")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBlockGraphs(long skip = 0, int take = 100)
        {
            int limitTake = (take > 100) ? 100 : take;
            var blockGraphs = await _unitOfWork.BlockGraphRepository.RangeAsync(skip, limitTake);
            return new ObjectResult(new
            {
                total = blockGraphs.Count,
                blockGraphs
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("delivered", Name = "GetDelivered")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDelivered(long skip = 0, int take = 100)
        {
            int limitTake = (take > 100) ? 100 : take;
            var delivered = await _unitOfWork.DeliveredRepository.RangeAsync(skip, limitTake);
            return new ObjectResult(new
            {
                total = delivered.Count,
                delivered
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("hashchains", Name = "GetHashChains")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetHashChains(long skip = 0, int take = 100)
        {
            int limitTake = (take > 100) ? 100 : take;
            var hashChains = await _unitOfWork.HashChainRepository.RangeAsync(skip, limitTake);
            return new ObjectResult(new
            {
                total = hashChains.Count,
                hashChains
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        [HttpGet("hashchainsbyheight", Name = "GetHashChainsOrderByHeight")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetHashChainsOrderByHeight(int skip = 0, int take = 100)
        {
            var limitTake = take > 100 ? 100 : take;
            var hashChains = await _unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, skip, limitTake);
            return new ObjectResult(new
            {
                total = hashChains.Count,
                hashChains
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("tries", Name = "GetTries")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTries(long skip = 0, int take = 100)
        {
            int limitTake = (take > 100) ? 100 : take;
            var tries = await _unitOfWork.TrieRepository.RangeAsync(skip, limitTake);
            return new ObjectResult(new
            {
                total = tries.Count,
                tries
            });
        }
    }
}
