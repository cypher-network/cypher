using System;
using System.Collections.Generic;
using System.Linq;
using CYPCore.Consensus.Models;
using CYPCore.Ledger;
using Moq;
using NUnit.Framework;
using Serilog;

namespace cypcore_test_unit.Ledger
{
    public class BlockGraphCollectorTests
    {
        private Mock<ILogger> _logger;
        private BlockGraphCollector _blockGraphCollector;

        private BlockGraph blockGraph_3_5 = new()
        {
            Block = new Block
            {
                Node = 3,
                Round = 5
            }
        };

        private BlockGraph blockGraph_4_6 = new()
        {
            Block = new Block
            {
                Node = 4,
                Round = 6
            }
        };

        private BlockGraph blockGraph_5_6 = new()
        {
            Block = new Block
            {
                Node = 5,
                Round = 6
            }
        };

        private BlockGraphCollectorTimedRound.CollectorConfig _config;

        [SetUp]
        public void Setup()
        {
            _logger = new Mock<ILogger>();
            _logger
                .Setup(logger => logger.ForContext(It.IsAny<string>(), It.IsAny<string>(), false))
                .Returns(_logger.Object);

            _config = new()
            {
                GraceTime = TimeSpan.FromDays(1),
                MaxBlocksPerRound = 1,
                ProcessFunc = ProcessRound,
                ProcessInNewThread = false,
            };

            _blockGraphCollector = new BlockGraphCollector(
                ProcessRound,
                _logger.Object);

            _blockGraphCollector.CollectorConfig = _config;
        }

        private struct ProcessResult
        {
            public ulong Round;
            public HashSet<BlockGraph> Blocks;
        }

        private List<ProcessResult> _processResults = new();
        private void ProcessRound(HashSet<BlockGraph> blocks, ulong round, Action<bool> finishedCallback)
        {
            _processResults.Add(new ProcessResult
            {
                Round = round,
                Blocks = blocks
            });
        }

        [Test]
        public void Instantiate_InvalidParams_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => new BlockGraphCollector(null, _logger.Object));
            Assert.Throws<ArgumentNullException>(() => new BlockGraphCollector(ProcessRound, null));
        }

        [Test]
        public void Add_InvalidParam_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => _blockGraphCollector.Add(null));
        }

        [Test]
        public void Collector_MaxBlockGraphs_StartsProcessing()
        {
            _processResults.Clear();

            _blockGraphCollector.Add(blockGraph_3_5);

            Assert.AreEqual(1, _processResults.Count);
        }

        [Test]
        public void Collector_ProcessedRound_DoesNotProcessAdditionalBlockGraphs()
        {
            _processResults.Clear();

            _blockGraphCollector.Add(blockGraph_4_6);
            Assert.AreEqual(1, _processResults.Count);

            _blockGraphCollector.Add(blockGraph_5_6);
            Assert.AreEqual(1, _processResults.Count);
        }

        [Test]
        public void Collector_AddDifferentRound_DoesNotProcess()
        {
            _processResults.Clear();

            _config.MaxBlocksPerRound = 2;
            _blockGraphCollector.CollectorConfig = _config;

            _blockGraphCollector.Add(blockGraph_3_5);
            Assert.AreEqual(0, _processResults.Count);

            _blockGraphCollector.Add(blockGraph_4_6);
            Assert.AreEqual(0, _processResults.Count);

            _blockGraphCollector.Add(blockGraph_5_6);
            Assert.AreEqual(1, _processResults.Count);

            Assert.AreEqual(6, _processResults.First().Round);
            Assert.AreEqual(2, _processResults.First().Blocks.Count);
        }

        [Test]
        public void Collector_Timeout_StartsProcessing()
        {
            _processResults.Clear();

            _config.GraceTime = TimeSpan.FromSeconds(3);
            _config.MaxBlocksPerRound = 10;
            _blockGraphCollector.CollectorConfig = _config;

            _blockGraphCollector.Add(blockGraph_4_6);
            _blockGraphCollector.Add(blockGraph_5_6);

            Assert.AreEqual(0, _processResults.Count);

            Assert.That(() => _processResults, Has.Count.EqualTo(1)
                .After((_blockGraphCollector.CollectorConfig.GraceTime * 3).Seconds).Seconds
                .PollEvery(250).MilliSeconds);

            Assert.AreEqual(2, _processResults.First().Blocks.Count);
        }
    }
}