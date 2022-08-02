// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Models;

public record StakingOptions
{
    public bool Enabled { get; set; }
    public int TransactionsPerBlock { get; set; }
    public string RewardAddress { get; set; }
    public bool CreateGenesisBlock { get; set; }
}