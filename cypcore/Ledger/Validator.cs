// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Extentions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Persistence;
using Dawn;
using Libsecp256k1Zkp.Net;
using Serilog;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;
using Stratis.Patricia;
using Util = CYPCore.Helper.Util;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IValidator
    {
        Task<VerifyResult> VerifyBlockGraphSignatureNodeRound(BlockGraph blockGraph);
        VerifyResult VerifyBulletProof(TransactionModel transaction);
        VerifyResult VerifyCoinbaseTransaction(VoutProto coinbase, ulong solution, decimal runningDistribution);
        VerifyResult VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution);
        Task<VerifyResult> VerifyBlockHeader(BlockHeaderProto blockHeader);
        Task<VerifyResult> VerifyBlockHeaders(BlockHeaderProto[] blockHeaders);
        Task<VerifyResult> VerifyTransaction(TransactionModel transaction);
        Task<VerifyResult> VerifyTransactions(TransactionModel[] transactions);
        VerifyResult VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security);
        int Difficulty(ulong solution, decimal networkShare);
        ulong Reward(ulong solution, decimal runningDistribution);
        decimal NetworkShare(ulong solution, decimal runningDistribution);
        ulong Solution(byte[] vrfSig, byte[] kernel);
        long GetAdjustedTimeAsUnixTimestamp();
        Task<VerifyResult> VerifyForkRule(BlockHeaderProto[] xChain);
        VerifyResult VerifyLockTime(LockTime target, string script);
        VerifyResult VerifyCommitSum(TransactionModel transaction);
        VerifyResult VerifyTransactionFee(TransactionModel transaction);
        Task<VerifyResult> VerifyKeyImage(TransactionModel transaction);
        Task<VerifyResult> VerifyOutputCommits(TransactionModel transaction);
        Task<decimal> CurrentRunningDistribution(ulong solution);
        Task<decimal> GetRunningDistribution();
        ulong Fee(int nByte);
        VerifyResult VerifyNetworkShare(ulong solution, decimal previousNetworkShare, decimal runningDistributionTotal);
        Task<VerifyResult> BlockExists(BlockHeaderProto blockHeader);
        PatriciaTrie Trie { get; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class Validator : IValidator
    {
        private const decimal Distribution = 139_000_000;
        private const int FeeNByte = 6000;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly ILogger _logger;
        public PatriciaTrie Trie { get; private set; }

        public Validator(IUnitOfWork unitOfWork, ISigning signing, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _signing = signing;
            _logger = logger.ForContext("SourceContext", nameof(Validator));

            SetupTrie().SafeFireAndForget(exception =>
            {
                _logger.Here().Fatal(exception, "Unable to setup patricia trie");
            });
        }

        public const uint StakeTimestampMask = 0x0000000A;
        public const string BlockZeroMerkel = "95ffdcc129740ae6b535c4274e4ea251501cebc00a90898a4e7265ffe494a895";
        public const string BlockZeroPreMerkel = "3030303030303030437970686572204e6574776f726b2076742e322e32303231";

        public const string Seed =
            "6b341e59ba355e73b1a8488e75b617fe1caa120aa3b56584a217862840c4f7b5d70cefc0d2b36038d67a35b3cd406d54f8065c1371a17a44c1abb38eea8883b2";

        public const string Security256 =
            "60464814417085833675395020742168312237934553084050601624605007846337253615407";

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task SetupTrie()
        {
            Trie = new PatriciaTrie(_unitOfWork.TrieRepository);
            var height = await _unitOfWork.HashChainRepository.CountAsync() - 1;
            var blockHeader =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == height));
            if (blockHeader == null) return;
            Trie.Put(blockHeader.ToHash(), blockHeader.ToHash());
            Trie.Flush();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public async Task<VerifyResult> BlockExists(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();
            var hasSeen = await _unitOfWork.HashChainRepository.GetAsync(blockHeader.ToIdentifier());
            return hasSeen != null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public Task<VerifyResult> VerifyBlockGraphSignatureNodeRound(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                if (!_signing.VerifySignature(blockGraph.Signature, blockGraph.PublicKey, blockGraph.ToHash()))
                {
                    _logger.Here().Error("Unable to verify the signature for block {@Round} from node {@Node}",
                        blockGraph.Block.Round, blockGraph.Block.Node);
                    return Task.FromResult(VerifyResult.UnableToVerify);
                }

                if (blockGraph.Prev != null && blockGraph.Prev?.Round != 0)
                {
                    if (blockGraph.Prev.Node != blockGraph.Block.Node)
                    {
                        _logger.Here().Error("Previous block node does not match block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                        return Task.FromResult(VerifyResult.UnableToVerify);
                    }

                    if (blockGraph.Prev.Round + 1 != blockGraph.Block.Round)
                    {
                        _logger.Here().Error("Previous block round is invalid on block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                        return Task.FromResult(VerifyResult.UnableToVerify);
                    }
                }

                if (blockGraph.Deps.Any(dep => dep.Block.Node == blockGraph.Block.Node))
                {
                    _logger.Here()
                        .Error(
                            "Block references includes a block from the same node in block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                    return Task.FromResult(VerifyResult.UnableToVerify);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify block graph signature");
                return Task.FromResult(VerifyResult.UnableToVerify);
            }

            return Task.FromResult(VerifyResult.Succeed);
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyBulletProof(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(transaction.Vout, nameof(transaction)).NotNull();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                using var secp256K1 = new Secp256k1();
                using var bulletProof = new BulletProof();
                if (transaction.Bp.Select((t, i) => bulletProof.Verify(transaction.Vout[i + 2].C, t.Proof, null))
                    .Any(verified => !verified))
                {
                    return VerifyResult.UnableToVerify;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify the bullet proof");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyCommitSum(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                using var pedersen = new Pedersen();
                for (var i = 0; i < transaction.Vout.Length / 3; i++)
                {
                    var fee = transaction.Vout[i].C;
                    var payment = transaction.Vout[i + 1].C;
                    var change = transaction.Vout[i + 2].C;
                    var commitSumBalance = pedersen.CommitSum(new List<byte[]> { fee, payment, change },
                        new List<byte[]>());
                    if (!pedersen.VerifyCommitSum(new List<byte[]> { commitSumBalance },
                        new List<byte[]> { fee, payment, change })) return VerifyResult.UnableToVerify;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify the committed sum");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="vrfBytes"></param>
        /// <param name="kernel"></param>
        /// <param name="solution"></param>
        /// <returns></returns>
        public VerifyResult VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution)
        {
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(32);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            var isSolution = false;
            try
            {
                var target = new BigInteger(1, vrfBytes);
                var weight = BigInteger.ValueOf(Convert.ToInt64(solution));
                var hashTarget = new BigInteger(1, kernel);
                var weightedTarget = target.Multiply(weight);
                isSolution = hashTarget.CompareTo(weightedTarget) <= 0;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify the solution");
            }

            return isSolution ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="blockHeaders"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlockHeaders(BlockHeaderProto[] blockHeaders)
        {
            Guard.Argument(blockHeaders, nameof(blockHeaders)).NotNull();
            foreach (var blockHeader in blockHeaders)
            {
                var verifyBlockHeader = await VerifyBlockHeader(blockHeader);
                if (verifyBlockHeader == VerifyResult.Succeed) continue;
                _logger.Here().Fatal("Unable to verify the block");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlockHeader(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();
            var verifySignature = _signing.VerifySignature(blockHeader.Signature.HexToByte(),
                blockHeader.PublicKey.HexToByte(), blockHeader.ToFinalStream());
            if (verifySignature == false)
            {
                _logger.Here().Fatal("Unable to verify the block signature");
                return VerifyResult.UnableToVerify;
            }

            var verifySloth = VerifySloth(blockHeader.Bits, blockHeader.VrfSignature.HexToByte(),
                blockHeader.Nonce.ToBytes(), blockHeader.Sec.ToBytes());
            if (verifySloth == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the Verified Delay Function");
                return verifySloth;
            }

            var runningDistribution = await CurrentRunningDistribution(blockHeader.Solution);
            var verifyCoinbase = VerifyCoinbaseTransaction(blockHeader.Transactions.First().Vout.First(),
                blockHeader.Solution, runningDistribution);
            if (verifyCoinbase == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the coinbase transaction");
                return verifyCoinbase;
            }

            uint256 hash;
            using (var ts = new TangramStream())
            {
                blockHeader.Transactions.Skip(1).ForEach(x => ts.Append(x.Stream()));
                hash = Hashes.DoubleSHA256(ts.ToArray());
            }

            var verifySolution = VerifySolution(blockHeader.VrfSignature.HexToByte(), hash.ToBytes(false),
                blockHeader.Solution);
            if (verifySolution == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the solution");
                return verifySolution;
            }

            var bits = Difficulty(blockHeader.Solution,
                blockHeader.Transactions.First().Vout.First().A.DivWithNanoTan());
            if (blockHeader.Bits != bits)
            {
                _logger.Here().Fatal("Unable to verify the bits");
                return VerifyResult.UnableToVerify;
            }

            Trie.Put(blockHeader.ToHash(), blockHeader.ToHash());
            if (!blockHeader.MerkelRoot.Equals(Trie.GetRootHash().ByteToHex()))
            {
                _logger.Here().Fatal("Unable to verify the merkel");
                var key = Trie.Get(blockHeader.ToHash());
                if (key != null) Trie.Delete(blockHeader.ToHash());

                return VerifyResult.UnableToVerify;
            }

            var verifyLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(blockHeader.Locktime)),
                blockHeader.LocktimeScript);
            if (verifyLockTime == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the block lock time");
                return verifyLockTime;
            }

            if (blockHeader.MerkelRoot.HexToByte().Xor(BlockZeroMerkel.HexToByte()) &&
                blockHeader.PrevMerkelRoot.HexToByte().Xor(BlockZeroPreMerkel.HexToByte())) return VerifyResult.Succeed;
            var prevBlock = await _unitOfWork.HashChainRepository.GetAsync(x =>
                new ValueTask<bool>(x.MerkelRoot.Equals(blockHeader.PrevMerkelRoot)));
            if (prevBlock == null)
            {
                _logger.Here().Fatal("Unable to find the previous block");
                return VerifyResult.UnableToVerify;
            }

            var verifyTransactions = await VerifyTransactions(blockHeader.Transactions);
            if (verifyTransactions == VerifyResult.Succeed) return VerifyResult.Succeed;
            _logger.Here().Fatal("Unable to verify the block transactions");
            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="transactions"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransactions(TransactionModel[] transactions)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();

            foreach (var transaction in transactions)
            {
                var verifyTransaction = await VerifyTransaction(transaction);
                if (verifyTransaction == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Fatal("Unable to verify the transaction");
                    return verifyTransaction;
                }

                if (transaction.Vout.First().T == CoinType.Fee)
                {
                    var verifyTransactionFee = VerifyTransactionFee(transaction);
                    if (verifyTransactionFee == VerifyResult.Succeed) continue;
                }
                else if (transaction.Vout.First().T == CoinType.Coinbase)
                {
                    continue;
                }

                _logger.Here().Fatal("Unable to verify the transaction fee");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransaction(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
            var verifySum = VerifyCommitSum(transaction);
            if (verifySum == VerifyResult.UnableToVerify) return verifySum;
            var verifyBulletProof = VerifyBulletProof(transaction);
            if (verifyBulletProof == VerifyResult.UnableToVerify) return verifyBulletProof;
            var transactionTypeArray = transaction.Vout.Select(x => x.T.ToString()).ToArray();
            if (transactionTypeArray.Contains(CoinType.Fee.ToString()) &&
                transactionTypeArray.Contains(CoinType.Coin.ToString()))
            {
                var verifyVOutCommits = await VerifyOutputCommits(transaction);
                if (verifyVOutCommits == VerifyResult.UnableToVerify) return verifyVOutCommits;
            }

            var verifyKImage = await VerifyKeyImage(transaction);
            if (verifyKImage == VerifyResult.UnableToVerify) return verifyKImage;
            using var mlsag = new MLSAG();
            for (var i = 0; i < transaction.Vin.Length; i++)
            {
                var m = PrepareMlsag(transaction.Rct[i].M, transaction.Vout, transaction.Vin[i].Key.Offsets,
                    transaction.Mix, 2);
                var verifyMlsag = mlsag.Verify(transaction.Rct[i].I, transaction.Mix, 2, m,
                    transaction.Vin[i].Key.Image, transaction.Rct[i].P, transaction.Rct[i].S);
                if (verifyMlsag) continue;
                _logger.Here()
                    .Fatal("Unable to verify the Multilayered Linkable Spontaneous Anonymous Group transaction");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyTransactionFee(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            var vout = transaction.Vout.First();
            if (vout.T != CoinType.Fee) return VerifyResult.UnableToVerify;
            var feeRate = Fee(FeeNByte);
            if (vout.A != feeRate) return VerifyResult.UnableToVerify;
            using var pedersen = new Pedersen();
            var commitSum = pedersen.CommitSum(new List<byte[]> { vout.C }, new List<byte[]> { vout.C });
            return commitSum == null ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="nByte"></param>
        /// <returns></returns>
        public ulong Fee(int nByte)
        {
            return (0.000012M * Convert.ToDecimal(nByte)).ConvertToUInt64();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinbase"></param>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public VerifyResult VerifyCoinbaseTransaction(VoutProto coinbase, ulong solution, decimal runningDistribution)
        {
            Guard.Argument(coinbase, nameof(coinbase)).NotNull();
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            if (coinbase.Validate().Any()) return VerifyResult.UnableToVerify;
            if (coinbase.T != CoinType.Coinbase) return VerifyResult.UnableToVerify;
            var verifyNetworkShare = VerifyNetworkShare(solution, coinbase.A.DivWithNanoTan(), runningDistribution);
            if (verifyNetworkShare == VerifyResult.UnableToVerify) return verifyNetworkShare;
            using var pedersen = new Pedersen();
            var commitSum = pedersen.CommitSum(new List<byte[]> { coinbase.C }, new List<byte[]> { coinbase.C });
            return commitSum == null ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="target"></param>
        /// <param name="script"></param>
        /// <returns></returns>
        public VerifyResult VerifyLockTime(LockTime target, string script)
        {
            Guard.Argument(target, nameof(target)).NotDefault();
            Guard.Argument(script, nameof(script)).NotNull().NotEmpty().NotWhiteSpace();
            var sc1 = new Script(Op.GetPushOp(target.Value), OpcodeType.OP_CHECKLOCKTIMEVERIFY);
            var sc2 = new Script(script);
            if (!sc1.ToString().Equals(sc2.ToString())) return VerifyResult.UnableToVerify;
            var tx = NBitcoin.Network.Main.CreateTransaction();
            tx.Outputs.Add(new TxOut { ScriptPubKey = new Script(script) });
            var spending = NBitcoin.Network.Main.CreateTransaction();
            spending.LockTime = new LockTime(DateTimeOffset.Now);
            spending.Inputs.Add(new TxIn(tx.Outputs.AsCoins().First().Outpoint, new Script()));
            spending.Inputs[0].Sequence = 1;
            return spending.Inputs.AsIndexedInputs().First().VerifyScript(tx.Outputs[0])
                ? VerifyResult.Succeed
                : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyKeyImage(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
            foreach (var vin in transaction.Vin)
            {
                var blockHeaders = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(t => t.Vin.First().Key.Image.Xor(vin.Key.Image))));
                if (blockHeaders.Count > 1) return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyOutputCommits(TransactionModel transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            var offSets = transaction.Vin.Select(v => v.Key).SelectMany(k => k.Offsets.Split(33)).ToArray();
            var list = offSets.Where((value, index) => index % 2 == 0).ToArray();
            foreach (var commit in list)
            {
                var blockHeaders = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(v => v.Vout.Any(c => c.C.Xor(commit)))));
                if (!blockHeaders.Any()) return VerifyResult.UnableToVerify;
                var outputs = blockHeaders.SelectMany(blockHeader => blockHeader.Transactions).SelectMany(x => x.Vout);
                if (outputs.Where(output => output.T == CoinType.Coinbase && output.T == CoinType.Fee)
                    .Select(output => VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(output.L)), output.S))
                    .Any(verified => verified != VerifyResult.UnableToVerify))
                {
                    return VerifyResult.UnableToVerify;
                }
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="vrfSig"></param>
        /// <param name="nonce"></param>
        /// <param name="security"></param>
        /// <returns></returns>
        public VerifyResult VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            Guard.Argument(nonce, nameof(nonce)).NotNull().MaxCount(77);
            Guard.Argument(security, nameof(security)).NotNull().MaxCount(77);
            var verifySloth = false;
            try
            {
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
                var sloth = new Sloth(ct);
                var x = System.Numerics.BigInteger.Parse(vrfSig.ByteToHex(), NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.ToStr());
                var p256 = System.Numerics.BigInteger.Parse(security.ToStr());
                if (x.Sign <= 0) x = -x;
                verifySloth = sloth.Verify(bits, x, y, p256);
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Unable to verify the Verified Delay Function");
            }

            return verifySloth ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public ulong Reward(ulong solution, decimal runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative();
            var networkShare = NetworkShare(solution, runningDistribution);
            return networkShare.ConvertToUInt64();
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<decimal> GetRunningDistribution()
        {
            var runningDistributionTotal = Distribution;
            try
            {
                var height = await _unitOfWork.HashChainRepository.CountAsync() + 1;
                var blockHeaders = await _unitOfWork.HashChainRepository.TakeLongAsync(height);
                var orderedBlockHeaders = blockHeaders.OrderBy(x => x.Height).ToArray();
                var length = height > orderedBlockHeaders.Length
                    ? orderedBlockHeaders.LongLength
                    : orderedBlockHeaders.Length - 1;
                for (var i = 0; i < length; i++)
                {
                    runningDistributionTotal -= NetworkShare(orderedBlockHeaders.ElementAt(i).Solution,
                        runningDistributionTotal);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable tp get the running distribution");
            }

            return runningDistributionTotal;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public decimal NetworkShare(ulong solution, decimal runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative();
            var r = Distribution - runningDistribution;
            var percentage = r / runningDistribution == 0 ? 0.1M : r / runningDistribution;
            if (percentage != 0.1M)
            {
                percentage += percentage * Convert.ToDecimal("1".PadRight(percentage.LeadingZeros(), '0'));
            }

            return solution * percentage / Distribution;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="previousNetworkShare"></param>
        /// <param name="runningDistributionTotal"></param>
        /// <returns></returns>
        public VerifyResult VerifyNetworkShare(ulong solution, decimal previousNetworkShare,
            decimal runningDistributionTotal)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            var previousRunningDistribution = runningDistributionTotal + previousNetworkShare;
            if (previousRunningDistribution > Distribution) return VerifyResult.UnableToVerify;
            var networkShare = NetworkShare(solution, previousRunningDistribution).ConvertToUInt64().DivWithNanoTan();
            previousNetworkShare = previousNetworkShare.ConvertToUInt64().DivWithNanoTan();
            return networkShare == previousNetworkShare ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="networkShare"></param>
        /// <returns></returns>
        public int Difficulty(ulong solution, decimal networkShare)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(networkShare, nameof(networkShare)).NotNegative();
            var diff = Math.Truncate(solution * networkShare / 144);
            diff = diff == 0 ? 1 : diff;
            return (int)diff;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="vrfSig"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        public ulong Solution(byte[] vrfSig, byte[] kernel)
        {
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);

            long itr = 0;

            try
            {
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
                var calculating = true;

                var target = new BigInteger(1, vrfSig);
                var hashTarget = new BigInteger(1, kernel);
                var hashTargetValue = new BigInteger((target.IntValue / hashTarget.BitCount).ToString()).Abs();
                var hashWeightedTarget = new BigInteger(1, kernel).Multiply(hashTargetValue);
                while (calculating)
                {
                    ct.ThrowIfCancellationRequested();
                    var weightedTarget = target.Multiply(BigInteger.ValueOf(itr));
                    if (hashWeightedTarget.CompareTo(weightedTarget) <= 0) calculating = false;
                    itr++;
                }
            }
            catch (Exception ex)
            {
                itr = 0;
                _logger.Here().Fatal(ex, "Unable to calculate solution");
            }

            return (ulong)itr;
        }

        ///TODO: Change LastAsync as this brings back incorrect data
        /// <summary>
        /// </summary>
        /// <param name="xChain"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyForkRule(BlockHeaderProto[] xChain)
        {
            Guard.Argument(xChain, nameof(xChain)).NotNull().NotEmpty();
            try
            {
                xChain = xChain.OrderBy(x => x.Height).ToArray();

                var xBlockHeader = xChain.First();
                var lastBlock = await _unitOfWork.HashChainRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Height == xBlockHeader.Height));
                if (lastBlock == null) return VerifyResult.Succeed;
                if (lastBlock.MerkelRoot.Equals(xBlockHeader.PrevMerkelRoot)) return VerifyResult.UnableToVerify;
                lastBlock = await _unitOfWork.HashChainRepository.GetAsync(x =>
                    new ValueTask<bool>(x.MerkelRoot.Equals(xBlockHeader.MerkelRoot)));
                if (lastBlock == null) return VerifyResult.UnableToVerify;
                var blockHeaders = await _unitOfWork.HashChainRepository.SkipAsync((int)xBlockHeader.Height + 1);

                blockHeaders = blockHeaders.OrderBy(x => x.Height).ToList();

                if (blockHeaders.Any())
                {
                    var blockTime = blockHeaders.First().Locktime.FromUnixTimeSeconds() -
                                    blockHeaders.Last().Locktime.FromUnixTimeSeconds();
                    var xBlockTime = xChain.First().Locktime.FromUnixTimeSeconds() -
                                     xChain.Last().Locktime.FromUnixTimeSeconds();
                    if (xBlockTime <= blockTime) return VerifyResult.Succeed;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Error while processing fork rule");
            }

            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public long GetAdjustedTimeAsUnixTimestamp()
        {
            return Util.GetAdjustedTimeAsUnixTimestamp() & ~StakeTimestampMask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public async Task<decimal> CurrentRunningDistribution(ulong solution)
        {
            var runningDistribution = await GetRunningDistribution();
            if (runningDistribution == Distribution)
            {
                runningDistribution -= NetworkShare(solution, runningDistribution);
                var share = NetworkShare(solution, runningDistribution);
                runningDistribution -= share.ConvertToUInt64().DivWithNanoTan();
                return runningDistribution;
            }

            var networkShare = NetworkShare(solution, runningDistribution);
            runningDistribution -= networkShare.ConvertToUInt64().DivWithNanoTan();
            return runningDistribution;
        }

        /// <summary>
        /// </summary>
        /// <param name="m"></param>
        /// <param name="vout"></param>
        /// <param name="keyOffset"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        private byte[] PrepareMlsag(byte[] m, VoutProto[] vout, byte[] keyOffset, int cols, int rows)
        {
            Guard.Argument(m, nameof(m)).NotNull();
            Guard.Argument(vout, nameof(vout)).NotNull();
            Guard.Argument(keyOffset, nameof(keyOffset)).NotNull();
            Guard.Argument(cols, nameof(cols)).NotZero().NotNegative();
            Guard.Argument(rows, nameof(rows)).NotZero().NotNegative();
            var pcmOut = new Span<byte[]>(new[] { vout[0].C, vout[1].C, vout[2].C });
            var kOffsets = keyOffset.Split(33).Select(x => x).ToList();
            var pcmIn = kOffsets.Where((value, index) => index % 2 == 0).ToArray().AsSpan();
            using var mlsag = new MLSAG();
            var prepareMlsag = mlsag.Prepare(m, null, pcmOut.Length, pcmOut.Length, cols, rows, pcmIn, pcmOut, null);
            if (prepareMlsag) return m;
            _logger.Here().Fatal("Unable to verify the Multilayered Linkable Spontaneous Anonymous Group transaction");
            return null;
        }
    }
}