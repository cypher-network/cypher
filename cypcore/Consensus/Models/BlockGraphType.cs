using FlatSharp.Attributes;

namespace CYPCore.Consensus.Models
{
    [FlatBufferEnum(typeof(sbyte))]
    public enum BlockGraphType : sbyte
    {
        Cryptocurrency,
        SmartContract
    }
}