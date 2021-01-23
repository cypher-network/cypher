// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Serilog;

using CYPCore.Ledger;
using CYPNode.Services;

namespace TGMNode.Controllers
{
    [Route("api/mempool")]
    public class MempoolController : Controller
    {
        private readonly IMempoolService _mempoolService;
        private readonly ILogger _logger;

        public MempoolController(IMempoolService mempoolService, ILogger logger)
        {
            _mempoolService = mempoolService;
            _logger = logger.ForContext<MempoolController>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        //[HttpPost("blockgraph", Name = "AddBlock")]
        //[ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status500InternalServerError)]
        //public async Task<IActionResult> AddBlock([FromBody]byte[] blockGraph)
        //{
        //    try
        //    {
        //        var blockGrpahProto = CYPCore.Helper.Util.DeserializeProto<MemPoolProto>(blockGraph);
        //        var block = await _memPool.AddMemPoolTransaction(blockGrpahProto);

        //        return new ObjectResult(new { protobuf = CYPCore.Helper.Util.SerializeProto(block) });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"<<< AddBlock - Controller >>>: {ex}");
        //    }

        //    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphs"></param>
        /// <returns></returns>
        //[HttpPost("blockgraphs", Name = "AddBlocks")]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status500InternalServerError)]
        //public async Task<IActionResult> AddBlocks([FromBody]byte[] blockGraphs)
        //{
        //    try
        //    {
        //        var blockGraphProtos = TGMCore.Helper.Util.DeserializeListProto<MemPoolProto>(blockGraphs);
        //        if (blockGraphProtos?.Any() == true)
        //        {
        //            var blockInfos = new List<BlockInfoProto>();

        //            for (int i = 0; i < blockGraphProtos.Count(); i++)
        //            {
        //                var added = await _memPoolService.AddMemPoolTransaction(blockGraphProtos.ElementAt(i));
        //                if (added != null)
        //                {
        //                    var next = blockGraphProtos.ElementAt(i);
        //                    blockInfos.Add(new BlockInfoProto { Hash = next.Block.Hash, Node = next.Block.Node, Round = next.Block.Round });
        //                }
        //            }

        //            return new ObjectResult(new { protobufs = TGMCore.Helper.Util.SerializeProto(blockInfos) });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"<<< AddBlocks - Controller >>>: {ex}");
        //    }

        //    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("count", Name = "MempoolTransactionCount")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMempoolTransactionCount()
        {
            var log = _logger.ForContext("Method", "GetMempoolTransactionCount");

            try
            {
                var transactionCount = await _mempoolService.GetMempoolTransactionCount();
                return new ObjectResult(new { count = transactionCount });
            }
            catch (Exception ex)
            {
                log.Error("Cannot get transaction count {@Error}", ex);
            }

            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        //[HttpGet("networkheight", Name = "NetworkBlockHeight")]
        //[ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status500InternalServerError)]
        //public async Task<IActionResult> NetworkBlockHeight()
        //{
        //    try
        //    {
        //        var blockHeight = 0; // await _networkProvider.NetworkBlockHeight();
        //        return new ObjectResult(new { height = blockHeight });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"<<< NetworkBlockHeight - Controller >>>: {ex}");
        //    }

        //    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="round"></param>
        /// <returns></returns>
        //[HttpGet("mempool/{hash}/{round}", Name = "MemPoolBlockGraph")]
        //[ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status500InternalServerError)]
        //public async Task<IActionResult> MemPoolBlockGraph(string hash, int round)
        //{
        //    try
        //    {
        //        //var blockGraph = await unitOfWork.BlockGraph
        //        //    .GetWhere(x => x.Block.Hash.Equals(hash) && x.Block.Node.Equals(httpClientService.NodeIdentity) && x.Block.Round.Equals(round));

        //        return new ObjectResult(new { protobuf = CYPCore.Helper.Util.SerializeProto(new byte()) });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"<<< MemPoolBlockGraph - Controller >>>: {ex}");
        //    }

        //    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        //}
    }
}
