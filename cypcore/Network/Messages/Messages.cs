//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using CYPCore.Consensus.Models;
using CYPCore.Models;
using CYPCore.Persistence;
using libsignal.ecc;
using MessagePack;
using Block = CYPCore.Models.Block;

namespace CYPCore.Network.Messages
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Count"></param>
    [MessagePackObject(true)]
    public record BlockCountResponse(long Count);
    public record BlockCountRequest;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Count"></param>
    [MessagePackObject(true)]
    public record BlockHeightResponse(long Count);
    public record BlockHeightRequest;

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record BlocksResponse
    {
        [Key(0)] public List<Block> Blocks { get; set; }
    }
    public record BlocksRequest(int Skip, int Take);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record MemoryPoolTransactionResponse
    {
        [Key(0)] public Transaction Transaction { get; set; }
    }
    public record MemoryPoolTransactionRequest(byte[] TransactionId);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record TransactionResponse
    {
        [Key(0)] public Transaction Transaction { get; set; }
    }
    public record TransactionRequest(byte[] TransactionId);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record PeerResponse
    {
        [Key(0)] public Peer Peer { get; set; }
    }
    public record PeerRequest;

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record NewBlockGraphResponse
    {
        [Key(0)] public bool OK { get; set; }
    }
    public record NewBlockGraphRequest(BlockGraph BlockGraph);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record NewTransactionResponse
    {
        [Key(0)] public bool OK { get; set; }
    }
    public record NewTransactionRequest(Transaction Transaction);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record SafeguardBlocksResponse
    {
        [Key(0)] public List<Block> Blocks { get; set; }
    }
    public record SafeguardBlocksRequest(int NumberOfBlocks);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record SaveBlockResponse
    {
        [Key(0)] public bool OK { get; set; }
    }
    public record SaveBlockRequest(Block Block);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record LastBlockResponse
    {
        [Key(0)] public Block Block { get; set; }
    }
    public record LastBlockRequest;

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record StakeResponse
    {
        [Key(0)] public Transaction Transaction { get; set; }
        [Key(1)] public string Message { get; set; }
    }
    [MessagePackObject]
    public record StakeRequest
    {
        [Key(0)] public Payment Payment { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Propagating"></param>
    [MessagePackObject(true)]
    public record CoinstakePropagatingResponse(bool Propagating);
    [MessagePackObject]
    public record CoinstakePropagatingRequest
    {
        public CoinstakePropagatingRequest(byte[] transactionId)
        {
            TransactionId = transactionId;
        }

        [Key(0)] public byte[] TransactionId { get; }
    }

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject]
    public record PosPoolTransactionResponse
    {
        [Key(0)] public Transaction Transaction { get; set; }
    }
    public record PosPoolTransactionRequest(byte[] TransactionId);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject(true)]
    public record BroadcastAutoResponse;
    public record BroadcastAutoRequest(TopicType TopicType, byte[] Data);

    /// <summary>
    /// 
    /// </summary>
    [MessagePackObject(true)]
    public record BroadcastManualResponse;
    public record BroadcastManualRequest(Peer[] Peers, TopicType TopicType, byte[] Data);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="MemStore"></param>
    [MessagePackObject(true)]
    public record PeersMemStoreResponse(MemStore<Peer> MemStore);
    public record PeersMemStoreRequest(bool ShouldUpdateHeight = false);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="GossipGraph"></param>
    [MessagePackObject(true)]
    public record GossipGraphResponse(GossipGraph GossipGraph);
    public record GossipGraphRequest;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Identifier"></param>
    /// <param name="Name"></param>
    /// <param name="RestApi"></param>
    /// <param name="Listening"></param>
    /// <param name="Version"></param>
    [MessagePackObject(true)]
    public record LocalNodeDetailsResponse(ulong Identifier, string Name, string RestApi, string Listening, string Version);

    public record LocalNodeDetailsRequest;


    /// <summary>
    /// 
    /// </summary>
    /// <param name="KeyPair"></param>
    [MessagePackObject(true)]
    public record KeyPairResponse(KeyPair KeyPair);
    public record KeyPairRequest(string KeyName);


    /// <summary>
    /// 
    /// </summary>
    /// <param name="Signature"></param>
    /// <param name="PublicKey"></param>
    [MessagePackObject(true)]
    public record SignatureResponse(byte[] Signature, byte[] PublicKey);
    public record SignatureRequest(string KeyName, byte[] Message);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Ok"></param>
    [MessagePackObject(true)]
    public record VerifySignatureAutoResponse(bool Ok);
    public record VerifySignatureAutoRequest(byte[] Signature, byte[] Message, string KeyName);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Ok"></param>
    [MessagePackObject(true)]
    public record VerifySignatureManualResponse(bool Ok);
    public record VerifySignatureManualRequest(byte[] Signature, byte[] PublicKey, byte[] Message);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Signature"></param>
    [MessagePackObject(true)]
    public record CalculateVrfResponse(byte[] Signature);
    public record CalculateVrfRequest(ECPrivateKey EcPrivateKey, byte[] Message);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Signature"></param>
    [MessagePackObject(true)]
    public record VerifyVrfSignatureResponse(byte[] Signature);
    public record VerifyVrfSignatureRequest(ECPublicKey EcPublicKey, byte[] Signature, byte[] Message);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Index"></param>
    [MessagePackObject(true)]
    public record TransactionBlockIndexResponse(ulong Index);
    public record TransactionBlockIndexRequest(byte[] TransactionId);
}

