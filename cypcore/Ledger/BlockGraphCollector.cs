using System;
using System.Collections.Generic;
using System.Linq;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using Dawn;
using Serilog;

namespace CYPCore.Ledger
{
    public class BlockGraphCollector
    {
        private readonly ILogger _logger;
        private readonly Dictionary<ulong, BlockGraphCollectorTimedRound> _rounds = new();
        private const int PurgeAfterRounds = 5;

        public BlockGraphCollector(Action<HashSet<BlockGraph>, ulong, Action<bool>> processFunc, ILogger logger)
        {
            Guard.Argument(processFunc, nameof(processFunc)).NotNull();
            Guard.Argument(logger, nameof(logger)).NotNull();

            _logger = logger.ForContext("SourceContext", nameof(BlockGraphCollector));
            CollectorConfig.ProcessFunc = processFunc;
        }

        public BlockGraphCollectorTimedRound.CollectorConfig CollectorConfig { get; set; } = new();

        public void Add(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();

            if (!_rounds.ContainsKey(blockGraph.Block.Round))
            {
                _logger.Here().Debug("First block for round {@Round}, creating timed collection", blockGraph.Block.Round);
                _rounds.Add(blockGraph.Block.Round, new BlockGraphCollectorTimedRound(
                    CollectorConfig, blockGraph.Block.Round, _logger));

                PurgeRounds(blockGraph.Block.Round);
            }

            if (_rounds.TryGetValue(blockGraph.Block.Round, out var round))
            {
                round.Add(blockGraph);
            }
            else
            {
                _logger.Here().Fatal("Cannot get blockgraphs for round {@Round}", round);
            }
        }

        private void PurgeRounds(ulong currentRound)
        {
            _rounds
                .Where(round =>
                    round.Key + PurgeAfterRounds < currentRound &&
                    round.Value.RoundFinished)
                .ForEach(round =>
                {
                    _rounds.Remove(round.Key);
                });
        }
    }
}
