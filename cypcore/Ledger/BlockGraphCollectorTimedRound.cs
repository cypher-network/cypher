using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using CYPCore.Consensus.Models;
using CYPCore.Extensions;
using Serilog;

namespace CYPCore.Ledger
{
    public class BlockGraphCollectorTimedRound
    {
        private readonly ILogger _logger;
        private readonly HashSet<BlockGraph> _blocks;
        private readonly ulong _round;
        private readonly CollectorConfig _config;
        private System.Timers.Timer _timeout;

        private readonly object _roundClosedGuard = new();
        private bool _roundClosed = false;

        public bool RoundFinished
        {
            get;
            private set;
        } = false;

        public class CollectorConfig
        {
            public Action<HashSet<BlockGraph>, ulong, Action<bool>> ProcessFunc;
            public bool ProcessInNewThread = true;
            public int MaxBlocksPerRound = 500;
            public TimeSpan GraceTime = TimeSpan.FromSeconds(10);
        }

        public BlockGraphCollectorTimedRound(
            CollectorConfig config,
            ulong round,
            ILogger logger)
        {
            _logger = logger.ForContext("SourceContext", $"{nameof(BlockGraphCollectorTimedRound)}:{round}");
            _round = round;
            _config = config;
            _blocks = new HashSet<BlockGraph>(config.MaxBlocksPerRound);

            _timeout = new Timer(_config.GraceTime.TotalMilliseconds);
            _timeout.AutoReset = false;
            _timeout.Elapsed += OnTimeout;
            _timeout.Enabled = true;

            ResetTimer();
        }

        private void ResetTimer()
        {
            _timeout.Stop();
            _timeout.Start();
        }

        public void Add(BlockGraph blockGraph)
        {
            lock (_roundClosedGuard)
            {
                if (!_roundClosed)
                {
                    if (_blocks.Add(blockGraph))
                    {
                        if (_blocks.Count == _config.MaxBlocksPerRound)
                        {
                            StartProcess();
                        }
                        else
                        {
                            ResetTimer();
                        }
                    }
                    else
                    {
                        _logger.Here().Error("Cannot add blockgraph from {@Node}. Possible duplicate?",
                            blockGraph.Block.Node);
                    }

                }
                else
                {
                    _logger.Here().Debug("{@Node} tried to add block to closed round {@Round}",
                        blockGraph.Block.Node,
                        blockGraph.Block.Round);
                }
            }
        }

        private void OnTimeout(object _1, ElapsedEventArgs _2)
        {
            lock (_roundClosedGuard)
            {
                StartProcess();
            }
        }

        private void StartProcess()
        {
            _timeout.Enabled = false;
            _roundClosed = true;

            if (_config.ProcessInNewThread)
            {
                Task.Run(() => _config.ProcessFunc(_blocks, _round, finished => RoundFinished = finished));
            }
            else
            {
                _config.ProcessFunc(_blocks, _round, finished => RoundFinished = finished);
            }
        }
    }
}
