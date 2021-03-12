// TGMNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Models
{
    //TODO: Change fee to Fee when we create a new block zero
    [FlatBufferEnum(typeof(sbyte))]
    public enum CoinType : sbyte
    {
        Coin,
        Coinbase,
        Coinstake,
        fee,
        Genesis
    }
}
