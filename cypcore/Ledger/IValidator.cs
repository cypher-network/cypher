// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using System.Threading.Tasks;

using NBitcoin;

using CYPCore.Models;

namespace CYPCore.Ledger
{
    public interface IValidator
    {
        uint StakeTimestampMask { get; }

        byte[] BlockZeroMerkel { get; }
        byte[] BlockZeroPreMerkel { get; }
        byte[] Seed { get; }
        byte[] Security256 { get; }

        Task<bool> VerifyMemPoolSignatures(MemPoolProto memPool);
        bool VerifyBulletProof(TransactionProto transaction);
        bool VerifyCoinbaseTransaction(TransactionProto transaction, ulong solution);
        bool VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution);
        Task<bool> VerifyBlockHeader(BlockHeaderProto blockHeader);
        Task<bool> VerifyBlockHeaders(BlockHeaderProto[] blockHeaders);
        Task<bool> VerifyTransaction(TransactionProto transaction);
        Task<bool> VerifyTransactions(HashSet<TransactionProto> transactions);
        bool VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security);
        int Difficulty(ulong solution, double networkShare);
        ulong Reward(ulong solution);
        double NetworkShare(ulong solution);
        ulong Solution(byte[] vrfSig, byte[] kernel);
        long GetAdjustedTimeAsUnixTimestamp();
        Task<bool> ForkRule(BlockHeaderProto[] xChain);
        bool VerifyLockTime(LockTime target, string script);
        bool VerifyCommitSum(TransactionProto transaction);
        bool VerifyTransactionFee(TransactionProto transaction);
        Task<bool> VerifyKimage(TransactionProto transaction);
        Task<bool> VerifyVOutCommits(TransactionProto transaction);
        Task<double> GetRunningDistribution();
        ulong Fee(int nByte);
        bool VerifyNetworkShare(ulong solution, double previousNetworkShare, ref double runningDistributionTotal);
    }
}