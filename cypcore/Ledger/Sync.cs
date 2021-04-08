//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Reactive.Linq;
using CYPCore.Extensions;
using Serilog;
using CYPCore.Models;
using CYPCore.Network;
using CYPCore.Persistence;
using Dawn;
using FlatSharp;
using Rx.Http;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISync
    {
        bool SyncRunning { get; }
        Task Fetch(string host, long skip, long take);
        Task Synchronize();
    }

    /// <summary>
    /// 
    /// </summary>
    public class Sync : ISync
    {
        public bool SyncRunning { get; private set; }

        private const int BatchSize = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private readonly ILocalNode _localNode;
        private readonly ILogger _logger;

        public Sync(IUnitOfWork unitOfWork, IValidator validator, ILocalNode localNode, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _localNode = localNode;
            _logger = logger.ForContext("SourceContext", nameof(Sync));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task Synchronize()
        {
            if (SyncRunning) return Task.CompletedTask;

            try
            {
                var networkPeers = _localNode.ObservePeers().Select(t => t).ToArray();
                networkPeers.Subscribe(observer =>
                {
                    foreach (var peer in observer)
                    {
                        _logger.Here().Information("Looking up block height from {@Host}", peer.Host);

                        var _ = _localNode.ObservePeerBlockHeight(peer)
                            .Subscribe(async network =>
                                {
                                    SyncRunning = true;

                                    _logger.Here()
                                        .Information(
                                            "Local node block height ({@LocalHeight}). Network block height ({NetworkHeight})",
                                            network.Local.Height, network.Remote.Height);

                                    if (network.Local.Height != network.Remote.Height)
                                    {
                                        ;

                                        _logger.Here().Information("Fetching blocks");

                                        await Fetch(peer.Host, network.Local.Height, network.Remote.Height / 188);

                                        var localHeight = await _unitOfWork.HashChainRepository.CountAsync();

                                        _logger.Here()
                                            .Information(
                                                "Local node block height ({@LocalHeight}). Network block height ({NetworkHeight})",
                                                localHeight, network.Remote.Height);

                                    }

                                    SyncRunning = false;
                                },
                                exception =>
                                {
                                    _logger.Here().Error(exception, "Error while looking up network block height");
                                });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error while checking");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task Fetch(string host, long skip, long take)
        {
            Guard.Argument(host, nameof(host)).NotNull().NotEmpty().NotWhiteSpace();
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            try
            {
                var numberOfBatches = (int)Math.Ceiling((double)take / BatchSize);
                numberOfBatches = numberOfBatches == 0 ? 1 : numberOfBatches;

                for (var i = 0; i < numberOfBatches; i++)
                {
                    var http = new RxHttpClient(new HttpClient(), null);
                    http.Get<FlatBufferStream>($"{host}/chain/blocks/{(int)(i * skip)}/{BatchSize}")
                        .Subscribe(async stream =>
                        {
                            var blockHeaders =
                                FlatBufferSerializer.Default.Parse<GenericList<BlockHeaderProto>>(stream.FlatBuffer);

                            var verifyForkRule = await _validator.VerifyForkRule(blockHeaders.Data.ToArray());
                            if (verifyForkRule == VerifyResult.UnableToVerify)
                            {
                                _logger.Here().Error("Unable to verify fork rule from: {@host}", host);
                                return;
                            }

                            foreach (var blockHeader in blockHeaders.Data.OrderBy(x => x.Height))
                            {
                                try
                                {
                                    var verifyBlockHeader = await _validator.VerifyBlockHeader(blockHeader);
                                    if (verifyBlockHeader != VerifyResult.Succeed)
                                    {
                                        return;
                                    }

                                    var saved = await _unitOfWork.HashChainRepository.PutAsync(
                                        blockHeader.ToIdentifier(),
                                        blockHeader);
                                    if (!saved)
                                    {
                                        _logger.Here()
                                            .Error("Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Here()
                                        .Error(ex, "Unable to save block: {@MerkleRoot}", blockHeader.MerkelRoot);
                                }
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Failed to synchronize node");
            }

            return Task.CompletedTask;
        }
    }
}