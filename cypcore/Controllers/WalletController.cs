using System;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Wallet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace CYPCore.Controllers
{
    [Route("wallet")]
    [ApiController]
    public class WalletController : Controller
    {
        private readonly INodeWallet _nodeWallet;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeWallet"></param>
        /// <param name="logger"></param>
        public WalletController(INodeWallet nodeWallet, ILogger logger)
        {
            _nodeWallet = nodeWallet;
            _logger = logger;
        }
        
        [HttpPost("login", Name = "Login")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Login(string seed, string passphrase, string transactionId)
        {
            try
            {
                var (success, message) = await _nodeWallet.Login(seed, passphrase, transactionId);
                if (success)
                {
                    return new ObjectResult(new { code = StatusCodes.Status200OK });
                    
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }
            
            return new ObjectResult(new { code = StatusCodes.Status401Unauthorized });
        }
    }
}