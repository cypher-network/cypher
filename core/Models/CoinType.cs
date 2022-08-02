// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

namespace CypherNetwork.Models;

[Flags]
public enum CoinType : sbyte
{
    Empty = 0x00,
    Coin = 0x01,
    Coinbase = 0x02,
    Coinstake = 0x03,
    Payment = 0x06,
    Change = 0x07,
    Stash = 0x09
}