// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CypherNetwork.Consensus.Models;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Models.Messages;
using Dawn;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using Serilog;
using Block = CypherNetwork.Models.Block;

namespace CypherNetwork.Network;

/// <summary>
/// 
/// </summary>
public interface IP2PDeviceApi
{
    IDictionary<int, Func<Parameter[], Task<ReadOnlySequence<byte>>>> Commands { get; }
}

/// <summary>
/// 
/// </summary>
public class P2PDeviceApi : IP2PDeviceApi
{
    private static ReadOnlySequence<byte> _updatePeersResponse;

    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;

    public P2PDeviceApi(ICypherSystemCore cypherSystemCore)
    {
        _cypherSystemCore = cypherSystemCore;
        using var serviceScope = _cypherSystemCore.ServiceScopeFactory.CreateScope();
        _logger = serviceScope.ServiceProvider.GetService<ILogger>()
            ?.ForContext("SourceContext", nameof(P2PDeviceApi));
        Init();
    }

    public IDictionary<int, Func<Parameter[], Task<ReadOnlySequence<byte>>>> Commands { get; } =
        new Dictionary<int, Func<Parameter[], Task<ReadOnlySequence<byte>>>>();

    /// <summary>
    /// </summary>
    private void Init()
    {
        _updatePeersResponse = AsyncHelper.RunSync(async () => await SerializeAsync(new UpdatePeersResponse(true)));
        RegisterCommand();
    }

    /// <summary>
    /// </summary>
    private void RegisterCommand()
    {
        Commands.Add((int)ProtocolCommand.GetPeer, OnGetPeerAsync);
        Commands.Add((int)ProtocolCommand.GetPeers, OnGetPeersAsync);
        Commands.Add((int)ProtocolCommand.GetBlocks, OnGetBlocksAsync);
        Commands.Add((int)ProtocolCommand.SaveBlock, OnSaveBlockAsync);
        Commands.Add((int)ProtocolCommand.GetBlockHeight, OnGetBlockHeightAsync);
        Commands.Add((int)ProtocolCommand.GetBlockCount, OnGetBlockCountAsync);
        Commands.Add((int)ProtocolCommand.GetMemTransaction, OnGetMemoryPoolTransactionAsync);
        Commands.Add((int)ProtocolCommand.GetTransaction, OnGetTransactionAsync);
        Commands.Add((int)ProtocolCommand.Transaction, OnNewTransactionAsync);
        Commands.Add((int)ProtocolCommand.BlockGraph, OnNewBlockGraphAsync);
        Commands.Add((int)ProtocolCommand.GetPosTransaction, OnPosTransactionAsync);
        Commands.Add((int)ProtocolCommand.GetTransactionBlockIndex, OnGetTransactionBlockIndexAsync);
        Commands.Add((int)ProtocolCommand.Stake, OnStakeAsync);
        Commands.Add((int)ProtocolCommand.StakeEnabled, OnStakeEnabledAsync);
        Commands.Add((int)ProtocolCommand.GetSafeguardBlocks, OnGetSafeguardBlocksAsync);
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetPeerAsync(Parameter[] none = default)
    {
        var localPeerResponse = _cypherSystemCore.PeerDiscovery().GetLocalPeer();
        return await SerializeAsync(localPeerResponse);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="none"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetPeersAsync(Parameter[] none = default)
    {
        return await SerializeAsync(_cypherSystemCore.PeerDiscovery());
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetBlocksAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        var skip = Convert.ToInt32(parameters[0].Value.FromBytes());
        var take = Convert.ToInt32(parameters[1].Value.FromBytes());
        var blocksResponse = await _cypherSystemCore.Graph().GetBlocksAsync(new BlocksRequest(skip, take));
        return await SerializeAsync(blocksResponse);
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnSaveBlockAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        _logger.Here().Information("Saved in p2p device api");
        var saveBlockResponse = await _cypherSystemCore.Graph()
            .SaveBlockAsync(new SaveBlockRequest(MessagePackSerializer.Deserialize<Block>(parameters[0].Value)));
        return await SerializeAsync(saveBlockResponse);
    }

    /// <summary>
    /// </summary>
    private async Task<ReadOnlySequence<byte>> OnGetBlockHeightAsync(Parameter[] none = default)
    {
        return await SerializeAsync(new BlockHeightResponse(_cypherSystemCore.UnitOfWork().HashChainRepository.Height));
    }

    /// <summary>
    /// </summary>
    private async Task<ReadOnlySequence<byte>> OnGetBlockCountAsync(Parameter[] none = default)
    {
        return await SerializeAsync(new BlockCountResponse(_cypherSystemCore.UnitOfWork().HashChainRepository.Count));
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetMemoryPoolTransactionAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        return await SerializeAsync(new MemoryPoolTransactionResponse(
            _cypherSystemCore.MemPool().Get(parameters[0].Value)));
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetTransactionAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        var transactionResponse =
            await _cypherSystemCore.Graph().GetTransactionAsync(new TransactionRequest(parameters[0].Value));
        return await SerializeAsync(transactionResponse);
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnNewTransactionAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        var verifyResult = await _cypherSystemCore.MemPool()
            .NewTransactionAsync(MessagePackSerializer.Deserialize<Transaction>(parameters[0].Value));
        return await SerializeAsync(new NewTransactionResponse(verifyResult == VerifyResult.Succeed));
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnNewBlockGraphAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        await _cypherSystemCore.Graph()
            .PostAsync(MessagePackSerializer.Deserialize<BlockGraph>(parameters[0].Value));
        return await SerializeAsync(new NewBlockGraphResponse(true));
    }

    /// <summary>
    /// </summary>
    /// <param name="none"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetSafeguardBlocksAsync(Parameter[] none = default)
    {
        const int numberOfBlocks = 147; // +- block proposal time * number of blocks
        var safeguardBlocksResponse =
            await _cypherSystemCore.Graph().GetSafeguardBlocksAsync(new SafeguardBlocksRequest(numberOfBlocks));
        return await SerializeAsync(safeguardBlocksResponse.Blocks.Any()
            ? safeguardBlocksResponse
            : safeguardBlocksResponse with { Blocks = null });
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnPosTransactionAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        return await SerializeAsync(new PosPoolTransactionResponse(_cypherSystemCore.PPoS().Get(parameters[0].Value)));
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnGetTransactionBlockIndexAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        var transactionBlockIndexResponse = await _cypherSystemCore.Graph()
            .GetTransactionBlockIndexAsync(new TransactionBlockIndexRequest(parameters[0].Value));
        return await SerializeAsync(transactionBlockIndexResponse);
    }

    /// <summary>
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnStakeAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        try
        {
            await using var stream = Util.Manager.GetStream(parameters[0].Value);
            var stakeRequest = await MessagePackSerializer.DeserializeAsync<StakeRequest>(stream);
            var packet = _cypherSystemCore.Crypto().DecryptChaCha20Poly1305(stakeRequest.Data,
                _cypherSystemCore.KeyPair.PrivateKey.FromSecureString().HexToByte(), stakeRequest.Token,
                null, stakeRequest.Nonce);
            if (packet is null)
                return await SerializeAsync(new StakeCredentialsResponse("Unable to decrypt message", false));

            var walletSession = _cypherSystemCore.WalletSession();
            var stakeCredRequest = MessagePackSerializer.Deserialize<StakeCredentialsRequest>(packet);
            var (loginSuccess, loginMessage) = await walletSession.LoginAsync(stakeCredRequest.Seed);
            if (!loginSuccess)
                return await SerializeAsync(new StakeCredentialsResponse(loginMessage, false));

            var (setupSuccess, setupMessage) = await walletSession.InitializeWalletAsync(stakeCredRequest.Outputs);
            if (setupSuccess)
            {
                _cypherSystemCore.Node.Staking.RewardAddress = stakeCredRequest.RewardAddress.FromBytes();
                _cypherSystemCore.Node.Staking.Enabled = true;
                return await SerializeAsync(new StakeCredentialsResponse(setupMessage, true));
            }
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return await SerializeAsync(new StakeCredentialsResponse("Unable to setup staking", false));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="parameters"></param>
    /// <returns></returns>
    private async Task<ReadOnlySequence<byte>> OnStakeEnabledAsync(Parameter[] parameters)
    {
        Guard.Argument(parameters, nameof(parameters)).NotNull().NotEmpty();
        try
        {
            await using var stream = Util.Manager.GetStream(parameters[0].Value);
            var stakeRequest = await MessagePackSerializer.DeserializeAsync<StakeRequest>(stream);
            var packet = _cypherSystemCore.Crypto().DecryptChaCha20Poly1305(stakeRequest.Data,
                _cypherSystemCore.KeyPair.PrivateKey.FromSecureString().HexToByte(), stakeRequest.Token,
                null, stakeRequest.Nonce);
            if (packet is null)
                return await SerializeAsync(new StakeCredentialsResponse("Unable to decrypt message", false));

            return await SerializeAsync(new StakeCredentialsResponse(
                _cypherSystemCore.Node.Staking.Enabled ? "Staking enabled" : "Staking not enabled", true));
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return await SerializeAsync(new StakeCredentialsResponse("Unable to setup staking", false));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<ReadOnlySequence<byte>> SerializeAsync<T>(T value)
    {
        await using var stream =
            Util.Manager.GetStream(MessagePackSerializer.Serialize(value)) as
                RecyclableMemoryStream;
        return stream.GetReadOnlySequence();
    }
}