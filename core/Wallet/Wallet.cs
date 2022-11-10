// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using CypherNetwork.Helper;
using CypherNetwork.Models;
using CypherNetwork.Wallet.Models;
using Dawn;
using Libsecp256k1Zkp.Net;
using libsignal.util;
using MessagePack;
using NBitcoin;
using NBitcoin.Stealth;
using Newtonsoft.Json.Linq;
using Serilog;
using Transaction = CypherNetwork.Models.Transaction;
using Util = Libsecp256k1Zkp.Net.Util;

namespace CypherNetwork.Wallet;

/// <summary>
/// </summary>
public interface INodeWallet
{
    Task<WalletTransaction> CreateTransactionAsync(ulong amount, ulong reward, string address);
}

/// <summary>
/// </summary>
public struct Balance
{
    public ulong Total { get; init; }
    public Output Commitment { get; init; }
}

/// <summary>
/// </summary>
public struct WalletTransaction
{
    public readonly Transaction Transaction;
    public readonly string Message;

    public WalletTransaction(Transaction transaction, string message)
    {
        Transaction = transaction;
        Message = message;
    }
}

/// <summary>
/// </summary>
public class NodeWallet : INodeWallet
{
    private const string HardwarePath = "m/44'/847177'/0'/0/";
    private readonly ICypherSystemCore _cypherSystemCore;
    private readonly ILogger _logger;
    private readonly NBitcoin.Network _network;

    /// <summary>
    /// </summary>
    /// <param name="cypherSystemCore"></param>
    /// <param name="logger"></param>
    public NodeWallet(ICypherSystemCore cypherSystemCore, ILogger logger)
    {
        _cypherSystemCore = cypherSystemCore;
        _logger = logger;
        _network = cypherSystemCore.Node.Network.Environment == Node.Mainnet
            ? NBitcoin.Network.Main
            : NBitcoin.Network.TestNet;
    }

    /// <summary>
    /// </summary>
    /// <param name="amount"></param>
    /// <param name="reward"></param>
    /// <param name="address"></param>
    /// <returns></returns>
    public async Task<WalletTransaction> CreateTransactionAsync(ulong amount, ulong reward, string address)
    {
        Guard.Argument(amount, nameof(amount)).NotNegative();
        Guard.Argument(reward, nameof(reward)).NotNegative();
        Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
        try
        {
            var session = _cypherSystemCore.WalletSession();
            if (session.KeySet is null) return new WalletTransaction(default, "Node wallet login required");
            if (session.CacheTransactions.Count == 0)
                return new WalletTransaction(default, "Node wallet payments required");
            var (spendKey, scanKey) = Unlock();
            if (spendKey == null || scanKey == null)
                return new WalletTransaction(default, "Unable to unlock node wallet");
            _logger.Information("Coinstake Amount: [{@Amount}]", amount);
            session.Amount = amount.MulCoin();
            session.Reward = reward;
            session.RecipientAddress = address;
            var (commitment, total) = GetSpending(session.Amount);
            if (commitment is null)
                return new WalletTransaction(default,
                    "No available commitment for this payment. Please load more funds");
            session.Spending = commitment;
            session.Change = total - session.Amount;
            if (session.Amount > total)
                return new WalletTransaction(default, "The stake amount exceeds the available commitment amount");
            var (transaction, message) = RingConfidentialTransaction(session);
            if (transaction.IsDefault()) return new WalletTransaction(default, message);
            var validator = _cypherSystemCore.Validator();
            var verifyOutputCommitments = await validator.VerifyCommitmentOutputsAsync(transaction);
            var verifyKeyImage = await validator.VerifyKeyImageNotExistsAsync(transaction);
            if (verifyOutputCommitments == VerifyResult.CommitmentNotFound ||
                verifyKeyImage == VerifyResult.KeyImageAlreadyExists)
            {
                session.CacheTransactions.Remove(session.Spending.C);
                return new WalletTransaction(default,
                    $"Unable to create coinstake Commitment: [{verifyOutputCommitments}] Key image: [{verifyKeyImage}]");
            }

            foreach (var vout in transaction.Vout)
            {
                if (vout.T == CoinType.Coinbase) continue;
                var output = new Output
                {
                    C = vout.C,
                    E = vout.E,
                    N = vout.N,
                    T = vout.T
                };

                session.CacheTransactions.Add(vout.C, output);
            }
            return new WalletTransaction(transaction, null);
        }
        catch (Exception ex)
        {
            _logger.Here().Error("{@Message}", ex.Message);
        }

        return new WalletTransaction(default, "Coinstake transaction failed");
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private (Key, Key) Unlock()
    {
        try
        {
            var session = _cypherSystemCore.WalletSession();
            var keySet = session.KeySet;
            var masterKey = MasterKey(MessagePackSerializer.Deserialize<KeySet>(keySet.FromSecureString().HexToByte()));
            var spendKey = masterKey.Derive(new KeyPath($"{HardwarePath}0")).PrivateKey;
            var scanKey = masterKey.Derive(new KeyPath($"{HardwarePath}1")).PrivateKey;
            return (spendKey, scanKey);
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to unlock master key");
        }

        return (null, null);
    }

    /// <summary>
    /// </summary>
    /// <param name="keySet"></param>
    /// <returns></returns>
    private static ExtKey MasterKey(KeySet keySet)
    {
        Guard.Argument(keySet, nameof(keySet)).IsDefault();
        var extKey = new ExtKey(new Key(keySet.RootKey.HexToByte()), keySet.ChainCode.HexToByte());
        keySet.ChainCode.ZeroString();
        keySet.KeyPath.ZeroString();
        keySet.RootKey.ZeroString();
        return extKey;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private Tuple<Output, ulong> GetSpending(ulong amount)
    {
        try
        {
            var freeBalances = new List<Balance>();
            var (_, scan) = Unlock();
            var balances = GetBalances();
            freeBalances.AddRange(balances.Where(balance => amount <= balance.Total).OrderByDescending(x => x.Total));
            if (!freeBalances.Any()) return new Tuple<Output, ulong>(default, 0);
            var spendAmount = freeBalances.Where(a => a.Total >= amount && a.Total <= freeBalances.Max(m => m.Total))
                .Select(x => x.Total).Aggregate((x, y) => x - amount < y - amount ? x : y);
            var spendingBalance = freeBalances.First(a => a.Total == spendAmount);
            var commitmentTotal = Amount(spendingBalance.Commitment, scan);
            return amount > commitmentTotal
                ? new Tuple<Output, ulong>(default, 0)
                : new Tuple<Output, ulong>(spendingBalance.Commitment, commitmentTotal);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error calculating the node wallet change");
            return new Tuple<Output, ulong>(default, 0);
        }
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private Balance[] GetBalances()
    {
        var balances = new List<Balance>();
        try
        {
            var session = _cypherSystemCore.WalletSession();
            var (_, scan) = Unlock();
            var outputs = session.CacheTransactions.GetItems()
                .Where(x => !session.CacheConsumed.GetItems().Any(c => x.C.Xor(c.Commit))).ToArray();
            if (!outputs.Any()) return Enumerable.Empty<Balance>().ToArray();
            balances.AddRange(from vout in outputs.ToArray()
                              let coinType = vout.T
                              where coinType is CoinType.Change or CoinType.Payment or CoinType.Coinstake
                              let isSpent = IsSpent(vout, session)
                              where isSpent != true
                              let amount = Amount(vout, scan)
                              where amount != 0
                              select new Balance { Commitment = vout, Total = amount });
        }
        catch (Exception ex)
        {
            _logger.Here().Error(ex, "Unable to retrieve node wallet balances");
        }

        return balances.ToArray();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="output"></param>
    /// <param name="session"></param>
    /// <returns></returns>
    private bool IsSpent(Output output, IWalletSession session)
    {
        Guard.Argument(output, nameof(output)).NotNull();
        Guard.Argument(session, nameof(session)).NotNull();
        var (spend, scan) = Unlock();
        var oneTimeSpendKey = spend.Uncover(scan, new PubKey(output.E));
        using var mlsag = new MLSAG();
        var imageKey = mlsag.ToKeyImage(oneTimeSpendKey.ToHex().HexToByte(), oneTimeSpendKey.PubKey.ToBytes());
        var result = AsyncHelper.RunSync(async () =>
            await _cypherSystemCore.Validator().VerifyKeyImageNotExistsAsync(imageKey));
        if (result != VerifyResult.Succeed) session.CacheTransactions.Remove(output.C);
        return result != VerifyResult.Succeed;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    private Tuple<Transaction, string> RingConfidentialTransaction(IWalletSession session)
    {
        using var secp256K1 = new Secp256k1();
        using var pedersen = new Pedersen();
        using var mlsag = new MLSAG();
        var blinds = new Span<byte[]>(new byte[3][]);
        var sk = new Span<byte[]>(new byte[2][]);
        const int nRows = 2; // last row sums commitments
        const int nCols = 22; // ring size
        var index = Util.Rand(0, nCols) % nCols;
        var m = new byte[nRows * nCols * 33];
        var pcmIn = new Span<byte[]>(new byte[nCols * 1][]);
        var pcmOut = new Span<byte[]>(new byte[2][]);
        var randSeed = secp256K1.Randomize32();
        var preimage = secp256K1.Randomize32();
        var pc = new byte[32];
        var ki = new byte[33 * 1];
        var ss = new byte[nCols * nRows * 32];
        var blindSum = new byte[32];
        var pkIn = new Span<byte[]>(new byte[nCols * 1][]);
        m = RingMembers(ref session, blinds, sk, nRows, nCols, index, m, pcmIn, pkIn);
        if (m == null) return new Tuple<Transaction, string>(default, "Unable to create ring members");
        blinds[1] = pedersen.BlindSwitch(session.Amount, secp256K1.CreatePrivateKey());
        blinds[2] = pedersen.BlindSwitch(session.Change, secp256K1.CreatePrivateKey());
        pcmOut[0] = pedersen.Commit(session.Amount, blinds[1]);
        pcmOut[1] = pedersen.Commit(session.Change, blinds[2]);
        var commitSumBalance = pedersen.CommitSum(new List<byte[]> { pcmOut[0], pcmOut[1] }, new List<byte[]>());
        if (!pedersen.VerifyCommitSum(new List<byte[]> { commitSumBalance }, new List<byte[]> { pcmOut[0], pcmOut[1] }))
            return new Tuple<Transaction, string>(default, "Verify commit sum failed");

        var bulletChange = BulletProof(session.Change, blinds[2], pcmOut[1]);
        if (!bulletChange.Success) return new Tuple<Transaction, string>(default, bulletChange.Exception.Message);

        var success = mlsag.Prepare(m, blindSum, pcmOut.Length, pcmOut.Length, nCols, nRows, pcmIn, pcmOut, blinds);
        if (!success) return new Tuple<Transaction, string>(default, "MLSAG Prepare failed");

        sk[nRows - 1] = blindSum;
        success = mlsag.Generate(ki, pc, ss, randSeed, preimage, nCols, nRows, index, sk, m);
        if (!success) return new Tuple<Transaction, string>(default, "MLSAG Generate failed");

        success = mlsag.Verify(preimage, nCols, nRows, m, ki, pc, ss);
        if (!success) return new Tuple<Transaction, string>(default, "MLSAG Verify failed");

        var offsets = Offsets(pcmIn, nCols);
        var generateTransaction = GenerateTransaction(ref session, m, nCols, pcmOut, blinds, preimage, pc, ki, ss,
            bulletChange.Value.proof, offsets);
        session.Amount = 0;
        session.Reward = 0;
        session.Change = 0;
        return !generateTransaction.Success
            ? new Tuple<Transaction, string>(default,
                $"Unable to create the transaction. Inner error message {generateTransaction.NonSuccessMessage.message}")
            : new Tuple<Transaction, string>(generateTransaction.Value, null);
    }

    /// <summary>
    /// </summary>
    /// <param name="session"></param>
    /// <param name="blinds"></param>
    /// <param name="sk"></param>
    /// <param name="nRows"></param>
    /// <param name="nCols"></param>
    /// <param name="index"></param>
    /// <param name="m"></param>
    /// <param name="pcmIn"></param>
    /// <param name="pkIn"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private unsafe byte[] RingMembers(ref IWalletSession session, Span<byte[]> blinds, Span<byte[]> sk, int nRows,
        int nCols, int index, byte[] m, Span<byte[]> pcmIn, Span<byte[]> pkIn)
    {
        Guard.Argument(session, nameof(session)).NotNull();
        Guard.Argument(nRows, nameof(nRows)).NotNegative();
        Guard.Argument(nCols, nameof(nCols)).NotNegative();
        Guard.Argument(index, nameof(index)).NotNegative();
        Guard.Argument(m, nameof(m)).NotNull().NotEmpty();
        using var pedersen = new Pedersen();
        using var secp256K1 = new Secp256k1();
        var transactions = session.GetSafeGuardBlocks()
            .SelectMany(x => x.Txs).ToArray();
        if (transactions.Any() != true) return null;
        transactions.Shuffle();

        var (spendKey, scanKey) = Unlock();

        for (var k = 0; k < nRows - 1; ++k)
            for (var i = 0; i < nCols; ++i)
            {
                if (index == i)
                    try
                    {
                        var message = Message(session.Spending, scanKey);
                        var oneTimeSpendKey = spendKey.Uncover(scanKey, new PubKey(session.Spending.E));
                        sk[0] = oneTimeSpendKey.ToHex().HexToByte();
                        blinds[0] = message.Blind;
                        pcmIn[i + k * nCols] = pedersen.Commit(message.Amount, message.Blind);
                        session.CacheConsumed.Add(pcmIn[i + k * nCols],
                            new Consumed(pcmIn[i + k * nCols], DateTime.UtcNow));
                        pkIn[i + k * nCols] = oneTimeSpendKey.PubKey.ToBytes();
                        fixed (byte* mm = m, pk = pkIn[i + k * nCols])
                        {
                            Util.MemCpy(&mm[(i + k * nCols) * 33], pk, 33);
                        }

                        continue;
                    }
                    catch (Exception)
                    {
                        _logger.Here().Error("Unable to create inner ring member");
                        return null;
                    }

                try
                {
                    var ringMembers = (from tx in transactions
                                       let vtime = tx.Vtime
                                       where !vtime.IsDefault()
                                       let verifyLockTime =
                                           _cypherSystemCore.Validator()
                                               .VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(tx.Vtime.L)), tx.Vtime.S)
                                       where verifyLockTime != VerifyResult.UnableToVerify
                                       select tx).ToArray();
                    ringMembers.Shuffle();

                    ringMembers.ElementAt(0).Vout.Shuffle();
                    Vout vout;
                    if (!ContainsCommitment(pcmIn, ringMembers.ElementAt(0).Vout[0].C))
                    {
                        vout = ringMembers.ElementAt(0).Vout[0];
                    }
                    else
                    {
                        ringMembers.ElementAt(1).Vout.Shuffle();
                        vout = ringMembers.ElementAt(1).Vout[0];
                    }

                    pcmIn[i + k * nCols] = vout.C;
                    pkIn[i + k * nCols] = vout.P;

                    fixed (byte* mm = m, pk = pkIn[i + k * nCols])
                    {
                        Util.MemCpy(&mm[(i + k * nCols) * 33], pk, 33);
                    }
                }
                catch (Exception)
                {
                    _logger.Here().Error("Unable to create outer ring members");
                    return null;
                }
            }

        return m;
    }

    /// <summary>
    /// </summary>
    /// <param name="session"></param>
    /// <param name="m"></param>
    /// <param name="nCols"></param>
    /// <param name="pcmOut"></param>
    /// <param name="blinds"></param>
    /// <param name="preimage"></param>
    /// <param name="pc"></param>
    /// <param name="ki"></param>
    /// <param name="ss"></param>
    /// <param name="bp"></param>
    /// <param name="offsets"></param>
    /// <returns></returns>
    private TaskResult<Transaction> GenerateTransaction(ref IWalletSession session, byte[] m, int nCols,
        Span<byte[]> pcmOut, Span<byte[]> blinds, byte[] preimage, byte[] pc, byte[] ki, byte[] ss, byte[] bp,
        byte[] offsets)
    {
        Guard.Argument(session, nameof(session)).NotNull();
        Guard.Argument(m, nameof(m)).NotNull().NotEmpty();
        Guard.Argument(nCols, nameof(nCols)).NotNegative();
        Guard.Argument(preimage, nameof(preimage)).NotNull().NotEmpty();
        Guard.Argument(pc, nameof(pc)).NotNull().NotEmpty();
        Guard.Argument(ki, nameof(ki)).NotNull().NotEmpty();
        Guard.Argument(ss, nameof(ss)).NotNull().NotEmpty();
        Guard.Argument(bp, nameof(bp)).NotNull().NotEmpty();
        Guard.Argument(offsets, nameof(offsets)).NotNull().NotEmpty();
        try
        {
            var (outPkPayment, stealthPayment) = StealthPayment(session.RecipientAddress);
            var (outPkChange, stealthChange) = StealthPayment(session.RecipientAddress);
            var tx = new Transaction
            {
                Bp = new[] { new Bp { Proof = bp } },
                Mix = nCols,
                Rct = new[] { new Rct { I = preimage, M = m, P = pc, S = ss } },
                Ver = 2,
                Vin = new[] { new Vin { Image = ki, Offsets = offsets } },
                Vout = new[]
                {
                    new Vout
                    {
                        A = session.Amount,
                        C = pcmOut[0],
                        D = blinds[1],
                        E = stealthPayment.Metadata.EphemKey.ToBytes(),
                        N = ScanPublicKey(session.RecipientAddress).Encrypt(Message(session.Amount, 0, blinds[1],
                            $"coinstake: {ShortPublicKey().ByteToHex()}")),
                        P = outPkPayment.ToBytes(),
                        S = Array.Empty<byte>(),
                        T = CoinType.Coinstake
                    },
                    new Vout
                    {
                        A = 0,
                        C = pcmOut[1],
                        D = Array.Empty<byte>(),
                        E = stealthChange.Metadata.EphemKey.ToBytes(),
                        N = ScanPublicKey(session.RecipientAddress).Encrypt(Message(session.Change,
                            session.Amount, blinds[2], $"staking: {ShortPublicKey().ByteToHex()}")),
                        P = outPkChange.ToBytes(),
                        S = Array.Empty<byte>(),
                        T = CoinType.Change
                    }
                }
            };
            using var secp256K1 = new Secp256k1();
            using var pedersen = new Pedersen();
            var (outPkReward, stealthReward) = StealthPayment(session.SenderAddress);
            var rewardLockTime =
                new LockTime(Helper.Util.DateTimeToUnixTime(DateTimeOffset.UtcNow.AddHours(24)));
            var blind = pedersen.BlindSwitch(session.Reward, secp256K1.CreatePrivateKey());
            var commit = pedersen.Commit(session.Reward, blind);
            var vOutput = tx.Vout.ToList();
            vOutput.Insert(0,
                new Vout
                {
                    A = session.Reward,
                    C = commit,
                    D = blind,
                    E = stealthReward.Metadata.EphemKey.ToBytes(),
                    L = rewardLockTime.Value,
                    N = ScanPublicKey(session.SenderAddress).Encrypt(Message(session.Reward, 0, blind,
                        $"coinbase: {ShortPublicKey().ByteToHex()}")),
                    P = outPkReward.ToBytes(),
                    S = new Script(Op.GetPushOp(rewardLockTime.Value), OpcodeType.OP_CHECKLOCKTIMEVERIFY).ToString().ToBytes(),
                    T = CoinType.Coinbase
                });
            tx.Vout = vOutput.ToArray();
            tx.TxnId = tx.ToHash();
            return TaskResult<Transaction>.CreateSuccess(tx);
        }
        catch (Exception ex)
        {
            _logger.Error("{@Message}", ex.Message);
            return TaskResult<Transaction>.CreateFailure(JObject.FromObject(new
            {
                success = false,
                message = ex.Message
            }));
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private byte[] ShortPublicKey()
    {
        return _cypherSystemCore.PeerDiscovery().GetLocalNode().PublicKey[..6];
    }

    /// <summary>
    /// </summary>
    /// <param name="commitIn"></param>
    /// <param name="nCols"></param>
    /// <returns></returns>
    private static byte[] Offsets(Span<byte[]> commitIn, int nCols)
    {
        Guard.Argument(nCols, nameof(nCols)).NotNegative();
        var i = 0;
        const int k = 0;
        var offsets = new byte[nCols * 33];
        var commits = commitIn.GetEnumerator();
        while (commits.MoveNext())
        {
            Buffer.BlockCopy(commits.Current, 0, offsets, (i + k * nCols) * 33, 33);
            i++;
        }

        return offsets;
    }

    /// <summary>
    /// </summary>
    /// <param name="commitIn"></param>
    /// <param name="commit"></param>
    /// <returns></returns>
    private static bool ContainsCommitment(Span<byte[]> commitIn, byte[] commit)
    {
        Guard.Argument(commit, nameof(commit)).NotEmpty().NotEmpty().MaxCount(33);
        var commits = commitIn.GetEnumerator();
        while (commits.MoveNext())
        {
            if (commits.Current == null) break;
            if (commits.Current.Xor(commit)) return true;
        }

        return false;
    }

    /// <summary>
    /// </summary>
    /// <param name="balance"></param>
    /// <param name="blindSum"></param>
    /// <param name="commitSum"></param>
    /// <returns></returns>
    private static TaskResult<ProofStruct> BulletProof(ulong balance, byte[] blindSum, byte[] commitSum)
    {
        Guard.Argument(balance, nameof(balance)).NotNegative();
        Guard.Argument(blindSum, nameof(blindSum)).NotNull().NotEmpty();
        Guard.Argument(commitSum, nameof(commitSum)).NotNull().NotEmpty();
        try
        {
            using var bulletProof = new BulletProof();
            using var sec256K1 = new Secp256k1();
            var proofStruct = bulletProof.GenProof(balance, blindSum, sec256K1.RandomSeed(32), null!, null!, null!);
            var success = bulletProof.Verify(commitSum, proofStruct.proof, null!);
            if (success) return TaskResult<ProofStruct>.CreateSuccess(proofStruct);
        }
        catch (Exception ex)
        {
            return TaskResult<ProofStruct>.CreateFailure(JObject.FromObject(new
            {
                success = false,
                message = ex.Message
            }));
        }

        return TaskResult<ProofStruct>.CreateFailure(JObject.FromObject(new
        {
            success = false,
            message = "Bulletproof Verify failed"
        }));
    }

    /// <summary>
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    private (PubKey, StealthPayment) StealthPayment(string address)
    {
        Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
        var ephemeralKey = new Key();
        var stealth = new BitcoinStealthAddress(address, _network);
        var payment = stealth.CreatePayment(ephemeralKey);
        var outPk = stealth.SpendPubKeys[0].UncoverSender(ephemeralKey, stealth.ScanPubKey);
        return (outPk, payment);
    }

    /// <summary>
    /// </summary>
    /// <param name="output"></param>
    /// <param name="scan"></param>
    /// <returns></returns>
    private static TransactionMessage Message(Output output, Key scan)
    {
        Guard.Argument(output, nameof(output)).NotNull();
        Guard.Argument(scan, nameof(scan)).NotNull();
        try
        {
            var transactionMessage = MessagePackSerializer.Deserialize<TransactionMessage>(scan.Decrypt(output.N));
            return transactionMessage;
        }
        catch (Exception)
        {
            // Ignore
        }

        return default;
    }

    /// <summary>
    /// </summary>
    /// <param name="amount"></param>
    /// <param name="paid"></param>
    /// <param name="blind"></param>
    /// <param name="memo"></param>
    /// <returns></returns>
    private static byte[] Message(ulong amount, ulong paid, byte[] blind, string memo)
    {
        Guard.Argument(amount, nameof(amount)).NotNegative();
        Guard.Argument(paid, nameof(paid)).NotNegative();
        Guard.Argument(blind, nameof(blind)).NotNull().NotEmpty();
        Guard.Argument(memo, nameof(memo)).NotNull();
        return MessagePackSerializer.Serialize(new TransactionMessage
        {
            Amount = amount,
            Blind = blind,
            Memo = memo,
            Date = DateTime.UtcNow,
            Paid = paid
        });
    }

    /// <summary>
    /// </summary>
    /// <param name="output"></param>
    /// <param name="scan"></param>
    /// <returns></returns>
    private static ulong Amount(Output output, Key scan)
    {
        Guard.Argument(output, nameof(output)).NotNull();
        Guard.Argument(scan, nameof(scan)).NotNull();
        try
        {
            var amount = MessagePackSerializer.Deserialize<TransactionMessage>(scan.Decrypt(output.N)).Amount;
            return amount;
        }
        catch (Exception)
        {
            // Ignore
        }

        return 0;
    }

    /// <summary>
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    private PubKey ScanPublicKey(string address)
    {
        Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
        var stealth = new BitcoinStealthAddress(address, _network);
        return stealth.ScanPubKey;
    }
}