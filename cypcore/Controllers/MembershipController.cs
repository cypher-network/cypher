// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Serilog;

using CYPCore.Services;

namespace CYPCore.Controllers
{
    [Route("membership")]
    [ApiController]
    public class MembershipController : Controller
    {
        private readonly IMembershipService _membershipService;
        private readonly ILogger _logger;

        public MembershipController(IMembershipService membershipService, ILogger logger)
        {
            _membershipService = membershipService;
            _logger = logger.ForContext("SourceContext", nameof(MembershipController));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("members", Name = "GetMembers")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMembers()
        {
            return new ObjectResult(new { members = await _membershipService.GetMembers() });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("publickey", Name = "GetPublicKey")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPublicKey()
        {
            return new ObjectResult(new { publicKey = await _membershipService.GetPublicKey() });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("count", Name = "GetMemberCount")]
        [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMemberCount()
        {
            return new ObjectResult(new { count = await _membershipService.GetCount() });
        }
    }
}
