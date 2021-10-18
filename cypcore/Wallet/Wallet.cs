// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Ledger;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Wallet.Models;
using Dawn;
using Libsecp256k1Zkp.Net;
using libsignal.util;
using MessagePack;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Stealth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Block = CYPCore.Models.Block;
using Transaction = CYPCore.Models.Transaction;
using Util = Libsecp256k1Zkp.Net.Util;

namespace CYPCore.Wallet
{
    /// <summary>
    /// 
    /// </summary>
    public interface INodeWallet
    {
        Task<Tuple<bool, string>> Login(string seed, string passphrase, string transactionId);
        Task<Tuple<Transaction, string>> CreateTransaction(ulong amount, ulong reward, string address);
        Task SaveQueued(List<Transaction> transactions);
        Task<List<Transaction>> LoadQueued();
        Task DeleteQueued();
    }

    /// <summary>
    /// 
    /// </summary>
    public class NodeWallet : INodeWallet
    {
        private const string FileName = @"RetainedWalletMessages.json";
        private const string HardwarePath = "m/44'/847177'/0'/0/";
        private readonly ILogger _logger;
        private readonly NBitcoin.Network _network;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidator _validator;
        private Session _session;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="unitOfWork"></param>
        /// <param name="validator"></param>
        /// <param name="networkSettings"></param>
        /// <param name="logger"></param>
        public NodeWallet(IUnitOfWork unitOfWork, IValidator validator, IOptions<NetworkSetting> networkSettings, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _validator = validator;
            _logger = logger;
            _network = networkSettings.Value.Environment == NetworkSetting.Mainnet
                ? NBitcoin.Network.Main
                : NBitcoin.Network.TestNet;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seed"></param>
        /// <param name="passphrase"></param>
        /// <param name="transactionId"></param>
        public async Task<Tuple<bool, string>> Login(string seed, string passphrase, string transactionId)
        {
            Guard.Argument(seed, nameof(seed)).NotNull().NotWhiteSpace().NotEmpty();
            Guard.Argument(passphrase, nameof(passphrase)).NotNull().NotWhiteSpace().NotEmpty();
            try
            {
                _session = new Session { Seed = seed.ToSecureString(), Passphrase = passphrase.ToSecureString() };
                seed.ZeroString();
                passphrase.ZeroString();
                CreateHdRootKey(_session.Seed, _session.Passphrase, out var rootKey);
                var keySet = CreateKeySet(new KeyPath($"{HardwarePath}0"), rootKey.PrivateKey.ToHex().HexToByte(),
                    rootKey.ChainCode);
                _session.SenderAddress = keySet.StealthAddress;
                _session.KeySet = MessagePackSerializer.Serialize(keySet).ByteToHex().ToSecureString();
                keySet.ChainCode.ZeroString();
                keySet.KeyPath.ZeroString();
                keySet.RootKey.ZeroString();
                if (!string.IsNullOrEmpty(transactionId))
                {
                    var (transaction, message) = await ReceivePayment(transactionId.HexToByte());
                    if (transaction is null)
                    {
                        _logger.Here().Error(message);
                        return new Tuple<bool, string>(false, message);
                    }

                    if (_session.MemStoreTransactions.Contains(transaction.TxnId))
                    {
                        return new Tuple<bool, string>(false,
                            $"Transaction already exists with TxId: {transaction.TxnId}");
                    }

                    _session.MemStoreTransactions.Put(transaction.TxnId, transaction);
                }
                else
                {
                    await ReloadQueued();
                }

                return new Tuple<bool, string>(true, null);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return new Tuple<bool, string>(false, "Unable to login.");
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task ReloadQueued()
        {
            try
            {
                _session.MemStoreTransactions.Clear();
                var transactions = await LoadQueued();
                foreach (var transaction in transactions)
                {
                    var verifyResult = await _validator.VerifyTransaction(transaction);
                    if (verifyResult is VerifyResult.Succeed or VerifyResult.KeyImageAlreadyExists)
                        _session.MemStoreTransactions.Put(transaction.TxnId, transaction);
                }

                if (_session.MemStoreTransactions.Count() == 0)
                {
                    await DeleteQueued();
                    _logger.Here().Warning("Please load more funds");
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transactionId"></param>
        private async Task<Tuple<Transaction, string>> ReceivePayment(byte[] transactionId)
        {
            try
            {
                var blocks = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(t => t.TxnId.Xor(transactionId))));
                var block = blocks.FirstOrDefault();
                var transaction = block?.Txs.FirstOrDefault(x => x.TxnId.Xor(transactionId));
                if (transaction is { })
                {
                    var (spendKey, scanKey) = Unlock(_session);
                    var outputs = (from v in transaction.Vout
                        let uncover = spendKey.Uncover(scanKey, new PubKey(v.E))
                        where uncover.PubKey.ToBytes().SequenceEqual(v.P)
                        select v.Cast()).ToList();
                    if (!outputs.Any())
                        return new Tuple<Transaction, string>(null, "Stealth address does not control this transaction");
                    transaction.Vout = outputs.ToArray();
                    return new Tuple<Transaction, string>(transaction, null);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex.Message);
            }

            return new Tuple<Transaction, string>(null, "Unable to find the transaction");
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyPath"></param>
        /// <param name="secretKey"></param>
        /// <param name="chainCode"></param>
        /// <returns></returns>
        private KeySet CreateKeySet(KeyPath keyPath, byte[] secretKey, byte[] chainCode)
        {
            Guard.Argument(keyPath, nameof(keyPath)).NotNull();
            Guard.Argument(secretKey, nameof(secretKey)).NotNull().MaxCount(32);
            Guard.Argument(chainCode, nameof(chainCode)).NotNull().MaxCount(32);
            var masterKey = new ExtKey(new Key(secretKey), chainCode);
            var spendKey = masterKey.Derive(keyPath).PrivateKey;
            var scanKey = masterKey.Derive(keyPath = keyPath.Increment()).PrivateKey;
            return new KeySet
            {
                ChainCode = masterKey.ChainCode.ByteToHex(),
                KeyPath = keyPath.ToString(),
                RootKey = masterKey.PrivateKey.ToHex(),
                StealthAddress = spendKey.PubKey.CreateStealthAddress(scanKey.PubKey, _network).ToString()
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seed"></param>
        /// <param name="passphrase"></param>
        /// <param name="hdRoot"></param>
        private static void CreateHdRootKey(SecureString seed, SecureString passphrase, out ExtKey hdRoot)
        {
            Guard.Argument(seed, nameof(seed)).NotNull();
            Guard.Argument(passphrase, nameof(passphrase)).NotNull();
            var concatenateMnemonic = string.Join(" ", seed.ToUnSecureString());
            hdRoot = new Mnemonic(concatenateMnemonic).DeriveExtKey(passphrase.ToUnSecureString());
            concatenateMnemonic.ZeroString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        private (Key, Key) Unlock(Session session)
        {
            Guard.Argument(session, nameof(session)).NotNull();
            Key spendKey = null;
            Key scanKey = null;
            try
            {
                var keySet = session.KeySet;
                var masterKey =
                    MasterKey(MessagePackSerializer.Deserialize<KeySet>(keySet.ToUnSecureString()
                        .HexToByte()));
                spendKey = masterKey.Derive(new KeyPath($"{HardwarePath}0")).PrivateKey;
                scanKey = masterKey.Derive(new KeyPath($"{HardwarePath}1")).PrivateKey;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Error unlocking");
            }

            return (spendKey, scanKey);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keySet"></param>
        /// <returns></returns>
        private static ExtKey MasterKey(KeySet keySet)
        {
            Guard.Argument(keySet, nameof(keySet)).NotNull();
            var extKey = new ExtKey(new Key(keySet.RootKey.HexToByte()), keySet.ChainCode.HexToByte());
            keySet.ChainCode.ZeroString();
            keySet.KeyPath.ZeroString();
            keySet.RootKey.ZeroString();
            return extKey;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="reward"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public async Task<Tuple<Transaction, string>> CreateTransaction(ulong amount, ulong reward, string address)
        {
            Guard.Argument(amount, nameof(amount)).NotNegative();
            Guard.Argument(reward, nameof(reward)).NotNegative();
            Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
            if (_session is null) throw new Exception("Login required.");
            if (_session.MemStoreTransactions.Count() == 0) throw new Exception("Payments required.");
            var (spendKey, scanKey) = Unlock(_session);
            _session.Amount = amount.MulWithNanoTan();
            _session.Reward = reward;
            _session.RecipientAddress = address;
            var (commitment, total) = await Spending(_session.Amount, spendKey, scanKey);
            if (commitment is null)
                return new Tuple<Transaction, string>(null,
                    $"No available commitment for this payment. Please load more funds.");
            _session.Spending = commitment;
            _session.Change = total - _session.Amount;
            if (amount > total)
                return new Tuple<Transaction, string>(null,
                    "The stake amount exceeds the available commitment amount.");
            var (transaction, message) = RingCT();
            ClearSessionAmounts();
            if (transaction is null) return new Tuple<Transaction, string>(null, message);
            var verifyTransaction = await _validator.VerifyTransaction(transaction);
            if (verifyTransaction is VerifyResult.UnableToVerify)
                return new Tuple<Transaction, string>(null, verifyTransaction.ToString());
            if (verifyTransaction is VerifyResult.CommitmentNotFound)
            {
                await ReloadQueued();
                return new Tuple<Transaction, string>(null, verifyTransaction.ToString());
            }

            _session.MemStoreTransactions.Put(transaction.TxnId, transaction);
            var transListAsync = await _session.MemStoreTransactions.GetMemSnapshot().SnapshotAsync().ToListAsync();
            await SaveQueued(transListAsync.Select(x => x.Value).ToList());
            return new Tuple<Transaction, string>(transaction, null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="spendKey"></param>
        /// <param name="scanKey"></param>
        /// <returns></returns>
        private async Task<Tuple<Vout, ulong>> Spending(ulong amount, Key spendKey, Key scanKey)
        {
            var snapshot = await _session.MemStoreTransactions.GetMemSnapshot().SnapshotAsync().ToArrayAsync();
            foreach (var tx in snapshot.Select(x => x.Value))
            {
                foreach (var (vOutput, index) in tx.Vout.WithIndex())
                {
                    if (vOutput.T == CoinType.Coinbase)
                    {
                        tx.Vout[index] = null;
                        continue;
                    }

                    var keyImage = GetKeyImage(vOutput, spendKey, scanKey);
                    var isSpent = await IsTransactionsSpent(keyImage);
                    if (isSpent)
                    {
                        tx.Vout[index] = null;
                    }
                }

                tx.Vout = tx.Vout.Where(v => v is not null).ToArray();
                if (!tx.Vout.Any()) continue;
                _session.MemStoreTransactions.Delete(tx.TxnId);
                _session.MemStoreTransactions.Put(tx.TxnId, tx);
            }

            var spendable = from tx in snapshot
                from output in tx.Value.Vout
                let spendingAmount = Amount(output, scanKey)
                where spendingAmount != 0
                select new { Commitment = output, Total = Amount(output, scanKey) };
            var spending = spendable.First(x => x.Total >= amount);
            return new Tuple<Vout, ulong>(spending.Commitment, spending.Total);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="output"></param>
        /// <param name="spendKey"></param>
        /// <param name="scanKey"></param>
        /// <returns></returns>
        private static byte[] GetKeyImage(Vout output, Key spendKey, Key scanKey)
        {
            Guard.Argument(output, nameof(output)).NotNull();
            Guard.Argument(spendKey, nameof(spendKey)).NotNull();
            Guard.Argument(scanKey, nameof(scanKey)).NotNull();
            var oneTimeSpendKey = spendKey.Uncover(scanKey, new PubKey(output.E));
            var mlsag = new MLSAG();
            return mlsag.ToKeyImage(oneTimeSpendKey.ToHex().HexToByte(), oneTimeSpendKey.PubKey.ToBytes());
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageKey"></param>
        /// <returns></returns>
        private async Task<bool> IsTransactionsSpent(byte[] imageKey)
        {
            Guard.Argument(imageKey, nameof(imageKey)).NotNull().MaxCount(33);
            try
            {
                var block = await _unitOfWork.HashChainRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(c => c.Vin[0].Key.Image.Xor(imageKey))));
                return block != null;
            }
            catch (Exception ex)
            {
                _logger.Here().Debug(ex.Message);
            }

            return false;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private Tuple<Transaction, string> RingCT()
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
            m = M(ref _session, blinds, sk, nRows, nCols, index, m, pcmIn, pkIn);
            blinds[1] = pedersen.BlindSwitch(_session.Amount, secp256K1.CreatePrivateKey());
            blinds[2] = pedersen.BlindSwitch(_session.Change, secp256K1.CreatePrivateKey());
            pcmOut[0] = pedersen.Commit(_session.Amount, blinds[1]);
            pcmOut[1] = pedersen.Commit(_session.Change, blinds[2]);
            var commitSumBalance = pedersen.CommitSum(new List<byte[]> { pcmOut[0], pcmOut[1] }, new List<byte[]>());
            if (!pedersen.VerifyCommitSum(new List<byte[]> { commitSumBalance }, new List<byte[]> { pcmOut[0], pcmOut[1] }))
            {
                return new Tuple<Transaction, string>(null, "Verify commit sum failed.");
            }

            var bulletChange = BulletProof(_session.Change, blinds[2], pcmOut[1]);
            if (!bulletChange.Success)
            {
                return new Tuple<Transaction, string>(null, bulletChange.Exception.Message);
            }

            var success = mlsag.Prepare(m, blindSum, pcmOut.Length, pcmOut.Length, nCols, nRows, pcmIn, pcmOut, blinds);
            if (!success)
            {
                return new Tuple<Transaction, string>(null, "MLSAG Prepare failed.");
            }

            sk[nRows - 1] = blindSum;
            success = mlsag.Generate(ki, pc, ss, randSeed, preimage, nCols, nRows, index, sk, m);
            if (!success)
            {
                return new Tuple<Transaction, string>(null, "MLSAG Generate failed.");
            }

            success = mlsag.Verify(preimage, nCols, nRows, m, ki, pc, ss);
            if (!success)
            {
                return new Tuple<Transaction, string>(null, "MLSAG Verify failed.");
            }

            var offsets = Offsets(pcmIn, nCols);
            var generateTransaction = GenerateTransaction(ref _session, m, nCols, pcmOut, blinds, preimage, pc, ki, ss, bulletChange.Value.proof, offsets);
            return !generateTransaction.Success
                ? new Tuple<Transaction, string>(null,
                    $"Unable to make the transaction. Inner error message {generateTransaction.NonSuccessMessage.message}")
                : new Tuple<Transaction, string>(generateTransaction.Value, null);
        }
        
        /// <summary>
        /// 
        /// </summary>
        private void ClearSessionAmounts()
        {
            _session.Amount = 0;
            _session.Reward = 0;
            _session.Change = 0;
        }

        /// <summary>
        /// 
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
        private unsafe byte[] M(ref Session session, Span<byte[]> blinds, Span<byte[]> sk, int nRows, int nCols, int index,
            byte[] m, Span<byte[]> pcmIn, Span<byte[]> pkIn)
        {
            Guard.Argument(session, nameof(session)).NotNull();
            Guard.Argument(nRows, nameof(nRows)).NotNegative();
            Guard.Argument(nCols, nameof(nCols)).NotNegative();
            Guard.Argument(index, nameof(index)).NotNegative();
            Guard.Argument(m, nameof(m)).NotNull().NotEmpty();
            using var pedersen = new Pedersen();
            var (spendKey, scanKey) = Unlock(session);
            var safeguardBlocks = SafeguardAsync();
            var transactions = safeguardBlocks.SelectMany(x => x.Txs).ToArray();
            transactions.Shuffle();
            for (var k = 0; k < nRows - 1; ++k)
            for (var i = 0; i < nCols; ++i)
            {
                if (i == index)
                {
                    try
                    {
                        var message = Message(session.Spending, scanKey);
                        var oneTimeSpendKey = spendKey.Uncover(scanKey, new PubKey(session.Spending.E));
                        sk[0] = oneTimeSpendKey.ToHex().HexToByte();
                        blinds[0] = message.Blind;
                        pcmIn[i + k * nCols] = pedersen.Commit(message.Amount, message.Blind);
                        pkIn[i + k * nCols] = oneTimeSpendKey.PubKey.ToBytes();
                        fixed (byte* mm = m, pk = pkIn[i + k * nCols])
                        {
                            Util.MemCpy(&mm[(i + k * nCols) * 33], pk, 33);
                        }

                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.Here().Error("Unable to create main ring member");
                        throw new Exception(ex.StackTrace);
                    }
                }

                try
                {
                    pcmIn[i + k * nCols] = transactions[i].Vout[0].C;
                    pkIn[i + k * nCols] = transactions[i].Vout[0].P;
                    fixed (byte* mm = m, pk = pkIn[i + k * nCols])
                    {
                        Util.MemCpy(&mm[(i + k * nCols) * 33], pk, 33);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Here().Error("Unable to create ring member");
                    throw new Exception(ex.StackTrace);
                }
            }

            return m;
        }

        /// <summary>
        /// 
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
        private TaskResult<Transaction> GenerateTransaction(ref Session session, byte[] m, int nCols,
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
                var (outPkChange, stealthChange) = StealthPayment(session.SenderAddress);
                var tx = new Transaction
                {
                    Bp = new[] { new Bp { Proof = bp } },
                    Mix = nCols,
                    Rct = new[] { new Rct { I = preimage, M = m, P = pc, S = ss } },
                    Ver = 0x2,
                    Vin = new[] { new Vin { Key = new KeyImage { Image = ki, Offsets = offsets } } },
                    Vout = new[]
                    {
                        new Vout
                        {
                            A = session.Amount,
                            C = pcmOut[0],
                            D = blinds[1],
                            E = stealthPayment.Metadata.EphemKey.ToBytes(),
                            N = ScanPublicKey(session.RecipientAddress).Encrypt(
                                Message(session.Amount, 0, blinds[1], string.Empty)),
                            P = outPkPayment.ToBytes(),
                            T = CoinType.Coinstake
                        },
                        new Vout
                        {
                            A = 0,
                            C = pcmOut[1],
                            E = stealthChange.Metadata.EphemKey.ToBytes(),
                            N = ScanPublicKey(session.SenderAddress).Encrypt(Message(session.Change,
                                session.Amount, blinds[2], string.Empty)),
                            P = outPkChange.ToBytes(),
                            T = CoinType.Change
                        }
                    }
                };
                using var secp256K1 = new Secp256k1();
                using var pedersen = new Pedersen();
                var (outPkReward, stealthReward) = StealthPayment(session.SenderAddress);
                var rewardLockTime =
                    new LockTime(CYPCore.Helper.Util.DateTimeToUnixTime(DateTimeOffset.UtcNow.AddHours(21)));
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
                        N = ScanPublicKey(session.SenderAddress)
                            .Encrypt(Message(session.Reward, 0, blind, string.Empty)),
                        P = outPkReward.ToBytes(),
                        S = new Script(Op.GetPushOp(rewardLockTime.Value), OpcodeType.OP_CHECKLOCKTIMEVERIFY)
                            .ToString(),
                        T = CoinType.Coinbase
                    });
                tx.Vout = vOutput.ToArray();
                tx.TxnId = tx.ToHash();
                return TaskResult<Transaction>.CreateSuccess(tx);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                return TaskResult<Transaction>.CreateFailure(JObject.FromObject(new
                {
                    success = false, message = ex.Message
                }));
            }
        }

        /// <summary>
        /// 
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
        /// 
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
                if (success)
                {
                    return TaskResult<ProofStruct>.CreateSuccess(proofStruct);
                }
            }
            catch (Exception ex)
            {
                return TaskResult<ProofStruct>.CreateFailure(JObject.FromObject(new
                {
                    success = false, message = ex.Message
                }));
            }

            return TaskResult<ProofStruct>.CreateFailure(JObject.FromObject(new
            {
                success = false, message = "Bulletproof Verify failed."
            }));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private (PubKey, StealthPayment) StealthPayment(string address)
        {
            Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
            var ephem = new Key();
            var stealth = new BitcoinStealthAddress(address, _network);
            var payment = stealth.CreatePayment(ephem);
            var outPk = stealth.SpendPubKeys[0].UncoverSender(ephem, stealth.ScanPubKey);
            return (outPk, payment);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="output"></param>
        /// <param name="scan"></param>
        /// <returns></returns>
        private static TransactionMessage Message(Vout output, Key scan)
        {
            Guard.Argument(output, nameof(output)).NotNull();
            Guard.Argument(scan, nameof(scan)).NotNull();
            try
            {
                var transactionMessage = MessagePackSerializer.Deserialize<TransactionMessage>(scan.Decrypt(output.N));
                transactionMessage.Output = output;
                return transactionMessage;
            }
            catch (Exception)
            {
                // Ignore
            }

            return null;
        }

        /// <summary>
        /// 
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
        /// 
        /// </summary>
        /// <param name="output"></param>
        /// <param name="scan"></param>
        /// <returns></returns>
        private static ulong Amount(Vout output, Key scan)
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
        /// 
        /// </summary>
        /// <returns></returns>
        private IEnumerable<Block> SafeguardAsync()
        {
            var tcs = new TaskCompletionSource<List<Block>>();
            Task.Run(async () =>
            {
                var height = (int)await _unitOfWork.HashChainRepository.GetBlockHeightAsync() - 147;
                height = height < 0x0 ? 0x0 : height;
                var blocks = await _unitOfWork.HashChainRepository.OrderByRangeAsync(x => x.Height, height, 147);
                if (blocks.Any())
                {
                    tcs.SetResult(blocks);
                    return;
                }

                tcs.SetResult(null);
            });
            return tcs.Task.Result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private PubKey ScanPublicKey(string address)
        {
            Guard.Argument(address, nameof(address)).NotNull().NotEmpty().NotWhiteSpace();
            var stealth = new BitcoinStealthAddress(address, _network);
            return stealth.ScanPubKey;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task SaveQueued(List<Transaction> transactions)
        {
            File.WriteAllTextAsync(FileName, JsonConvert.SerializeObject(transactions));
            return Task.FromResult(0);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<List<Transaction>> LoadQueued()
        {
            var path = Path.Combine(Helper.Util.EntryAssemblyPath(), FileName);
            if (!File.Exists(path)) return new List<Transaction>();
            var json = await File.ReadAllTextAsync(path);
            var transactions = JsonConvert.DeserializeObject<List<Transaction>>(json);
            return transactions;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Task DeleteQueued()
        {
            var path = Path.Combine(Helper.Util.EntryAssemblyPath(), FileName);
            File.Delete(path);
            return Task.FromResult(0);
        }
    }
}