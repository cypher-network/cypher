// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Ledger;
using CYPCore.Models;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Dawn;
using Microsoft.Extensions.DependencyInjection;
using Proto;
using Proto.DependencyInjection;
using Serilog;

namespace CYPCore.Network.Commands
{
    /// <summary>
    /// 
    /// </summary>
    public class ShimCommands : IActor
    {
        private readonly ActorSystem _actorSystem;
        private readonly PID _pidLocalNode;
        private readonly PID _pidCryptoKeySign;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="serviceScopeFactory"></param>
        /// <param name="logger"></param>
        public ShimCommands(ActorSystem actorSystem, IServiceScopeFactory serviceScopeFactory, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidLocalNode = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<LocalNode>());
            _pidCryptoKeySign = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<CryptoKeySign>());
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task ReceiveAsync(IContext context) => context.Message switch
        {
            CoinstakePropagatingRequest coinstakePropagatingRequest => OnGetCoinstakePropagating(coinstakePropagatingRequest, context),
            TransactionBlockIndexRequest transactionBlockIndexRequest => OnGetTransactionBlockIndex(transactionBlockIndexRequest, context),
            BlockCountRequest => OnGetBlockCount(context),
            BlocksRequest getBlocksRequest => OnGetBlocks(getBlocksRequest, context),
            BlockHeightRequest => OnGetBlockHeight(context),
            MemoryPoolTransactionRequest memoryPoolTransactionRequest => OnGetMemoryPoolTransaction(memoryPoolTransactionRequest, context),
            PosPoolTransactionRequest posPoolTransactionRequest => OnGetPoSPoolTransaction(posPoolTransactionRequest, context),
            NewBlockGraphRequest newBlockGraphRequest => OnNewBlockGraph(newBlockGraphRequest, context),
            NewTransactionRequest newTransactionRequest => OnNewTransaction(newTransactionRequest, context),
            SafeguardBlocksRequest safeguardBlocksRequest => OnGetSafeguardBlocks(safeguardBlocksRequest, context),
            SaveBlockRequest saveBlockRequest => OnSaveBlock(saveBlockRequest, context),
            TransactionRequest transactionRequest => OnGetTransaction(transactionRequest, context),
            LastBlockRequest => OnGetLastBlock(context),
            PeerRequest => OnGetPeer(context),
            _ => Task.CompletedTask
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinstakePropagatingRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetCoinstakePropagating(CoinstakePropagatingRequest coinstakePropagatingRequest,
            IContext context)
        {
            Guard.Argument(coinstakePropagatingRequest, nameof(coinstakePropagatingRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var graph = scope.ServiceProvider.GetRequiredService<IGraph>();
                    var block = await unitOfWork.HashChainRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(coinstakePropagatingRequest.TransactionId))));
                    if (block is { }) context.Respond(new CoinstakePropagatingResponse(false));
                    var transaction = graph.Get(coinstakePropagatingRequest.TransactionId);
                    if (transaction is { })
                    {
                        context.Respond(new CoinstakePropagatingResponse(true));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new CoinstakePropagatingResponse(false));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionIndexRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetTransactionBlockIndex(TransactionBlockIndexRequest transactionIndexRequest,
            IContext context)
        {
            Guard.Argument(transactionIndexRequest, nameof(transactionIndexRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var block = await unitOfWork.HashChainRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(transactionIndexRequest.TransactionId))));
                    if (block is { })
                    {
                        context.Respond(new TransactionBlockIndexResponse(block.Height));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new TransactionBlockIndexResponse(0));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        private async Task OnGetBlockCount(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var height = await unitOfWork.HashChainRepository.CountAsync();
                    context.Respond(new BlockCountResponse(height));
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new BlockCountResponse(0));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blocksRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetBlocks(BlocksRequest blocksRequest, IContext context)
        {
            Guard.Argument(blocksRequest, nameof(blocksRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var (skip, take) = blocksRequest;
                    var blocks = await unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, skip, take);
                    if (blocks.Any())
                    {
                        context.Respond(new BlocksResponse { Blocks = blocks });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new BlocksResponse());
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        private async Task OnGetBlockHeight(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var height = await unitOfWork.HashChainRepository.CountAsync();
                    if (height > 0)
                        height--;
                    else
                        height = 0;

                    context.Respond(new BlockHeightResponse(height));
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new BlockHeightResponse(0));
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memoryPoolGetTransactionRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnGetMemoryPoolTransaction(MemoryPoolTransactionRequest memoryPoolGetTransactionRequest,
            IContext context)
        {
            Guard.Argument(memoryPoolGetTransactionRequest, nameof(memoryPoolGetTransactionRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(() =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var memoryPool = scope.ServiceProvider.GetRequiredService<IMemoryPool>();
                    var transaction = memoryPool.Get(memoryPoolGetTransactionRequest.TransactionId);
                    if (transaction is { })
                    {
                        context.Respond(new MemoryPoolTransactionResponse { Transaction = transaction });
                        return Task.CompletedTask;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new MemoryPoolTransactionResponse());
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="posPoolTransactionRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnGetPoSPoolTransaction(PosPoolTransactionRequest posPoolTransactionRequest,
            IContext context)
        {
            Guard.Argument(posPoolTransactionRequest, nameof(posPoolTransactionRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(() =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var posMinting = scope.ServiceProvider.GetRequiredService<IPosMinting>();
                    var transaction = posMinting.Get(posPoolTransactionRequest.TransactionId);
                    if (transaction is { })
                    {
                        context.Respond(new PosPoolTransactionResponse { Transaction = transaction });
                        return Task.CompletedTask;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new PosPoolTransactionResponse());
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        private async Task OnGetPeer(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var localNodeDetailsResponse =
                        await _actorSystem.Root.RequestAsync<LocalNodeDetailsResponse>(_pidLocalNode,
                            new LocalNodeDetailsRequest());
                    var keyPairResponse = await _actorSystem.Root.RequestAsync<KeyPairResponse>(_pidCryptoKeySign,
                        new KeyPairRequest(CryptoKeySign.DefaultSigningKeyName));
                    var blockHeight = await unitOfWork.HashChainRepository.CountAsync();
                    var peer = new Peer
                    {
                        RestApi = localNodeDetailsResponse.RestApi,
                        BlockHeight = (ulong)blockHeight,
                        ClientId = Util.ToHashIdentifier(keyPairResponse.KeyPair.PublicKey.ByteToHex()),
                        Listening = localNodeDetailsResponse.Listening,
                        Name = localNodeDetailsResponse.Name,
                        PublicKey = keyPairResponse.KeyPair.PublicKey.ByteToHex(),
                        Version = localNodeDetailsResponse.Version,
                    };
                    context.Respond(new PeerResponse { Peer = peer });
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new PeerResponse());
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newBlockGraphRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnNewBlockGraph(NewBlockGraphRequest newBlockGraphRequest, IContext context)
        {
            Guard.Argument(newBlockGraphRequest, nameof(newBlockGraphRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var graph = scope.ServiceProvider.GetRequiredService<IGraph>();
                    var verifiedResult = await graph.NewBlockGraph(newBlockGraphRequest.BlockGraph);
                    if (verifiedResult is VerifyResult.Succeed or VerifyResult.AlreadyExists)
                    {
                        context.Respond(new NewBlockGraphResponse { OK = true });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new NewBlockGraphResponse { OK = false });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newTransactionRequest"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task OnNewTransaction(NewTransactionRequest newTransactionRequest, IContext context)
        {
            Guard.Argument(newTransactionRequest, nameof(newTransactionRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var memoryPool = scope.ServiceProvider.GetRequiredService<IMemoryPool>();
                    var verifiedResult = await memoryPool.NewTransaction(newTransactionRequest.Transaction);
                    if (verifiedResult == VerifyResult.Succeed)
                    {
                        context.Respond(new NewTransactionResponse { OK = true });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new NewTransactionResponse { OK = false });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="safeguardBlocksRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetSafeguardBlocks(SafeguardBlocksRequest safeguardBlocksRequest, IContext context)
        {
            Guard.Argument(safeguardBlocksRequest, nameof(safeguardBlocksRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var height = (int)await unitOfWork.HashChainRepository.GetBlockHeightAsync() -
                                 safeguardBlocksRequest.NumberOfBlocks;
                    height = height < 0x0 ? 0x0 : height;
                    var blocks = await unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, height,
                        safeguardBlocksRequest.NumberOfBlocks);
                    if (blocks.Any())
                    {
                        context.Respond(new SafeguardBlocksResponse { Blocks = blocks });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new SafeguardBlocksResponse());
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="saveBlockRequest"></param>
        /// <param name="context"></param>
        private async Task OnSaveBlock(SaveBlockRequest saveBlockRequest, IContext context)
        {
            Guard.Argument(saveBlockRequest, nameof(saveBlockRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var saved = await unitOfWork.HashChainRepository.PutAsync(saveBlockRequest.Block.ToIdentifier(),
                        saveBlockRequest.Block);
                    if (saved)
                    {
                        context.Respond(new SaveBlockResponse { OK = true });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new SaveBlockResponse { OK = false });
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionRequest"></param>
        /// <param name="context"></param>
        private async Task OnGetTransaction(TransactionRequest transactionRequest, IContext context)
        {
            Guard.Argument(transactionRequest, nameof(transactionRequest)).NotNull();
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var blocks = await unitOfWork.HashChainRepository.WhereAsync(x =>
                        new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(transactionRequest.TransactionId))));
                    var block = blocks.FirstOrDefault();
                    var transaction = block?.Txs.FirstOrDefault(x => x.TxnId.Xor(transactionRequest.TransactionId));
                    if (transaction is { })
                    {
                        context.Respond(new TransactionResponse { Transaction = transaction });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new TransactionResponse());
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        private async Task OnGetLastBlock(IContext context)
        {
            Guard.Argument(context, nameof(context)).NotNull();
            await Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var height = await unitOfWork.HashChainRepository.CountAsync();
                    var block = await unitOfWork.HashChainRepository.GetAsync(x =>
                        new ValueTask<bool>(x.Height == (ulong)height - 1));
                    if (block is { })
                    {
                        context.Respond(new LastBlockResponse { Block = block });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error(ex.Message);
                }

                context.Respond(new LastBlockResponse());
            });
        }
    }
}