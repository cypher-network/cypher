// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using CypherNetwork.Consensus.Models;
using libsignal.ecc;
using MessagePack;

namespace CypherNetwork.Models.Messages;

/// <summary>
/// 
/// </summary>
/// <param name="Count"></param>
[MessagePackObject]
public record BlockCountResponse([property: Key(0)] long Count);
public record BlockCountRequest;

/// <summary>
/// 
/// </summary>
/// <param name="Count"></param>
[MessagePackObject]
public record BlockHeightResponse([property: Key(0)] long Count);
public record BlockHeightRequest(ulong Height);
public record BlockByHeightRequest(ulong Height);

public record BlockHeightExistsRequest(ulong Height);
public record BlockExistsRequest(byte[] Hash);

/// <summary>
/// </summary>
[MessagePackObject]
public record BlocksResponse([property: Key(0)] IReadOnlyList<Block> Blocks);
public record BlocksRequest(int Skip, int Take);

/// <summary>
/// 
/// </summary>
/// <param name="Transaction"></param>
[MessagePackObject]
public record MemoryPoolTransactionResponse([property: Key(0)] Transaction Transaction);

/// <summary>
/// 
/// </summary>
/// <param name="Transaction"></param>
[MessagePackObject]
public record TransactionResponse([property: Key(0)] Transaction Transaction);

/// <summary>
/// 
/// </summary>
/// <param name="TransactionId"></param>
[MessagePackObject]
public record TransactionRequest([property: Key(0)] byte[] TransactionId)
{
    public Parameter GetPayload()
    {
        return new Parameter { Value = TransactionId, ProtocolCommand = ProtocolCommand.Transaction };
    }
}

[MessagePackObject]
public record TransactionIdRequest([property: Key(0)] byte[] TransactionId);

/// <summary>
/// </summary>
public record PeerResponse
{
    public Peer Peer { get; init; }
}

public record PeerRequest;

/// <summary>
/// </summary>
public record JoinPeerResponse
{
    public Peer Peer { get; set; }
}

public record JoinPeerRequest;

/// <summary>
/// 
/// </summary>
/// <param name="Ok"></param>
[MessagePackObject]
public record NewBlockGraphResponse([property: Key(0)] bool Ok);
public record NewBlockGraphRequest(BlockGraph BlockGraph);

/// <summary>
/// 
/// </summary>
/// <param name="Ok"></param>
[MessagePackObject]
public record NewTransactionResponse([property: Key(0)] bool Ok);
public record NewTransactionRequest(Transaction Transaction);

/// <summary>
/// 
/// </summary>
/// <param name="Blocks"></param>
/// <param name="Error"></param>
[MessagePackObject]
public record SafeguardBlocksResponse([property: Key(0)] IReadOnlyList<Block> Blocks, [property: Key(1)] string Error);
/// <summary>
/// 
/// </summary>
/// <param name="NumberOfBlocks"></param>
public record SafeguardBlocksRequest(int NumberOfBlocks);

/// <summary>
/// 
/// </summary>
/// <param name="Ok"></param>
public record SaveBlockResponse(bool Ok);
/// <summary>
/// 
/// </summary>
/// <param name="Block"></param>
public record SaveBlockRequest(Block Block);

/// <summary>
/// 
/// </summary>
/// <param name="Block"></param>
[MessagePackObject]
public record BlockResponse([property: Key(0)] Block Block);
/// <summary>
/// 
/// </summary>
/// <param name="Hash"></param>
public record BlockRequest(byte[] Hash);

/// <summary>
/// </summary>
public record StakeResponse(int Code);

/// <summary>
/// 
/// </summary>
[MessagePackObject]
public record StakeRequest
{
    [Key(0)] public byte[] Tag { get; set; }
    [Key(1)] public byte[] Nonce { get; set; }
    [Key(2)] public byte[] Token { get; set; }
    [Key(3)] public byte[] Data { get; set; }
}

/// <summary>
/// 
/// </summary>
/// <param name="Message"></param>
/// <param name="Success"></param>
[MessagePackObject]
public record StakeCredentialsResponse([property: Key(0)] string Message, [property: Key(1)] bool Success);

/// <summary>
/// 
/// </summary>
[MessagePackObject]
public record StakeCredentialsRequest
{
    [Key(0)] public byte[] Seed { get; set; }
    [Key(1)] public byte[] Passphrase { get; set; }
    [Key(2)] public byte[] RewardAddress { get; set; }
    [Key(3)] public Output[] Outputs { get; set; }
}

/// <summary>
/// 
/// </summary>
/// <param name="Transaction"></param>
[MessagePackObject]
public record PosPoolTransactionResponse([property: Key(0)] Transaction Transaction);
/// <summary>
/// 
/// </summary>
/// <param name="TransactionId"></param>
public record PosPoolTransactionRequest(byte[] TransactionId);

/// <summary>
/// </summary>
[MessagePackObject, Serializable]
public record PeerDiscoveryResponse([property: Key(0)] Peer[] Peers);

/// <summary>
/// </summary>
/// <param name="Signature"></param>
/// <param name="PublicKey"></param>
public record SignatureResponse(byte[] Signature, byte[] PublicKey);
/// <summary>
/// 
/// </summary>
/// <param name="Signature"></param>
/// <param name="PublicKey"></param>
/// <param name="Message"></param>
public record VerifySignatureManualRequest(byte[] Signature, byte[] PublicKey, byte[] Message);

/// <summary>
/// </summary>
/// <param name="Signature"></param>
public record CalculateVrfResponse(byte[] Signature);
/// <summary>
/// 
/// </summary>
/// <param name="EcPrivateKey"></param>
/// <param name="Message"></param>
public record CalculateVrfRequest(ECPrivateKey EcPrivateKey, byte[] Message);

/// <summary>
/// </summary>
/// <param name="Signature"></param>
public record VerifyVrfSignatureResponse(byte[] Signature);
/// <summary>
/// 
/// </summary>
/// <param name="EcPublicKey"></param>
/// <param name="Signature"></param>
/// <param name="Message"></param>
public record VerifyVrfSignatureRequest(ECPublicKey EcPublicKey, byte[] Signature, byte[] Message);

/// <summary>
/// 
/// </summary>
/// <param name="TransactionId"></param>
[MessagePackObject]
public record TransactionBlockIndexRequest([property: Key(0)] byte[] TransactionId);
/// <summary>
/// </summary>
/// <param name="Index"></param>
[MessagePackObject]
public record TransactionBlockIndexResponse([property: Key(0)] ulong Index);

/// <summary>
/// 
/// </summary>
/// <param name="Transactions"></param>
public record HashTransactionsRequest(Transaction[] Transactions);

/// <summary>
/// 
/// </summary>
/// <param name="Ok"></param>
[MessagePackObject]
public record UpdatePeersResponse([property: Key(0)] bool Ok);