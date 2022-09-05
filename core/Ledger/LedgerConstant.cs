// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CypherNetwork.Extensions;

namespace CypherNetwork.Ledger;

public static class LedgerConstant
{
    // Graph
    public const double
        OnRoundThrottleFromSeconds =
            5; // Block size will have an effect. Should increase/decrease.

    // Validator
    public const int SolutionCancellationTimeoutFromMilliseconds = 10_000;
    public const decimal Distribution = 21_000_000M;
    public const int RewardPercentage = 50;
    public const ulong SolutionThrottle = 700_000_00;
    public const int Coin = 1000_000_000;
    public const int BlockHalving = 25246081; // 25246081*0.08333333/60/24/365.25 = 3.9999999984
    public const int Bits = 8192;
    public static readonly byte[] BlockZeroMerkel =
        "E76BC3FEE881F72FA257DA08305F06DFB7CCC1BB926F7943FE9E7E45A394EF54".HexToByte();
    public static readonly byte[] BlockZeroPrevHash =
        "303030303063797068657270756E6B7330777269746530636F64653030303030".HexToByte();

    // PPoS
    public const uint BlockProposalTimeFromSeconds = 5;
    public const uint WaitSyncTimeFromSeconds = 5;
    public const uint WaitPPoSEnabledTimeFromSeconds = 5;
    public const int SlothCancellationTimeoutFromMilliseconds = 60_000;

    // MemPool
    public const uint TransactionDefaultTimeDelayFromSeconds = 5;
}