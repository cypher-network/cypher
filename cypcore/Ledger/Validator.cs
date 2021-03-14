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
using cypcore.Extensions;
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
using Util = CYPCore.Helper.Util;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IValidator
    {
        uint StakeTimestampMask { get; }

        byte[] BlockZeroMerkel { get; }
        byte[] BlockZeroPreMerkel { get; }
        byte[] Seed { get; }
        byte[] Security256 { get; }

        Task<VerifyResult> VerifyBlockGraphSignatureNodeRound(BlockGraph blockGraph);
        VerifyResult VerifyBulletProof(TransactionProto transaction);
        Task<VerifyResult> VerifyCoinbaseTransaction(VoutProto coinbase, ulong solution);
        VerifyResult VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution);
        Task<VerifyResult> VerifyBlockHeader(BlockHeaderProto blockHeader);
        Task<VerifyResult> VerifyBlockHeaders(BlockHeaderProto[] blockHeaders);
        Task<VerifyResult> VerifyTransaction(TransactionProto transaction);
        Task<VerifyResult> VerifyTransactions(TransactionProto[] transactions);
        VerifyResult VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security);
        int Difficulty(ulong solution, double networkShare);
        ulong Reward(ulong solution, double runningDistribution);
        double NetworkShare(ulong solution, double runningDistribution);
        ulong Solution(byte[] vrfSig, byte[] kernel);
        long GetAdjustedTimeAsUnixTimestamp();
        Task<VerifyResult> VerifyForkRule(BlockHeaderProto[] xChain);
        VerifyResult VerifyLockTime(LockTime target, string script);
        VerifyResult VerifyCommitSum(TransactionProto transaction);
        VerifyResult VerifyTransactionFee(TransactionProto transaction);
        Task<VerifyResult> VerifyKeyImage(TransactionProto transaction);
        Task<VerifyResult> VerifyOutputCommits(TransactionProto transaction);
        Task<double> GetRunningDistribution();
        ulong Fee(int nByte);
        VerifyResult VerifyNetworkShare(ulong solution, double previousNetworkShare,
            double runningDistributionTotal);
    }

    /// <summary>
    /// 
    /// </summary>
    public class Validator : IValidator
    {
        private const double Distribution = 139_000_000;
        private const int FeeNByte = 6000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signingProvider;
        private readonly ILogger _logger;
        private readonly ReaderWriterLockSlim _sync = new();

        public Validator(IUnitOfWork unitOfWork, ISigning signingProvider, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
            _logger = logger.ForContext("SourceContext", nameof(Validator));
        }

        public uint StakeTimestampMask => 0x0000000A;
        public byte[] BlockZeroMerkel => "4157dd0a13221f2a1b90fef7e4864925389d3107e112c03a267580d3e84d7b49".HexToByte();
        public byte[] BlockZeroPreMerkel =>
            "3030303030303030437970686572204e6574776f726b2076742e322e32303231".HexToByte();

        public byte[] Seed =>
            "6b341e59ba355e73b1a8488e75b617fe1caa120aa3b56584a217862840c4f7b5d70cefc0d2b36038d67a35b3cd406d54f8065c1371a17a44c1abb38eea8883b2"
                .HexToByte();

        public byte[] Security256 =>
            "60464814417085833675395020742168312237934553084050601624605007846337253615407".ToBytes();

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
                if (!_signingProvider.VerifySignature(blockGraph.Signature,
                    blockGraph.PublicKey, blockGraph.ToHash()))
                {
                    _logger.Here().Error("Unable to verify signature for block {@Round} from node {@Node}",
                        blockGraph.Block.Round,
                        blockGraph.Block.Node);

                    return Task.FromResult(VerifyResult.UnableToVerify);
                }

                if (blockGraph.Prev != null && blockGraph.Prev?.Round != 0)
                {
                    if (blockGraph.Prev.Node != blockGraph.Block.Node)
                    {
                        _logger.Here().Error("Previous block node does not match on block {@Round} from node {@Node}",
                            blockGraph.Block.Round,
                            blockGraph.Block.Node);

                        return Task.FromResult(VerifyResult.UnableToVerify);
                    }

                    if (blockGraph.Prev.Round + 1 != blockGraph.Block.Round)
                    {
                        _logger.Here()
                            .Error("Previous block round is invalid on block {@Round} from node {@Node}",
                                blockGraph.Block.Round, blockGraph.Block.Node);

                        return Task.FromResult(VerifyResult.UnableToVerify);
                    }
                }

                if (blockGraph.Deps.Any(dep => dep.Block.Node == blockGraph.Block.Node))
                {
                    _logger.Here().Error(
                        "Block references includes a block from same node in block reference {@Round} from node {@Node}",
                        blockGraph.Block.Round,
                        blockGraph.Block.Node);

                    return Task.FromResult(VerifyResult.UnableToVerify);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify block graph signature");
                return Task.FromResult(VerifyResult.UnableToVerify);
            }

            return Task.FromResult(VerifyResult.Succeed);
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyBulletProof(TransactionProto transaction)
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
                _logger.Here().Error(ex, "Cannot verify bulletproof");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyCommitSum(TransactionProto transaction)
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
                        new List<byte[]> { fee, payment, change }))
                        return VerifyResult.UnableToVerify;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify commit sum");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="poSCommitBlindSwitch"></param>
        /// <returns></returns>
        public VerifyResult VerifyCommitBlindSwitch(PoSCommitBlindSwitchProto poSCommitBlindSwitch)
        {
            Guard.Argument(poSCommitBlindSwitch, nameof(poSCommitBlindSwitch)).NotNull();

            try
            {
                using var pedersen = new Pedersen();
                var verifyCommitSum = pedersen.VerifyCommitSum(
                    new List<byte[]> { poSCommitBlindSwitch.Balance.HexToByte() },
                    new List<byte[]>
                    {
                        poSCommitBlindSwitch.Difficulty.HexToByte(), poSCommitBlindSwitch.Difference.HexToByte()
                    });

                return verifyCommitSum ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify blind switch");
                return VerifyResult.UnableToVerify;
            }
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
                _logger.Here().Error(ex, "Cannot verify solution");
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

                _logger.Here().Fatal("Could not verify block header");

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

            var verifySloth = VerifySloth(blockHeader.Bits, blockHeader.VrfSig.HexToByte(), blockHeader.Nonce.ToBytes(),
                blockHeader.Sec.ToBytes());
            if (verifySloth == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Could not verify the block header sloth");
                return verifySloth;
            }

            var verifyCoinbase = await VerifyCoinbaseTransaction(blockHeader.Transactions.First().Vout.First(),
                blockHeader.Solution);
            if (verifyCoinbase == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Could not verify the block header coinbase transaction");
                return verifyCoinbase;
            }

            uint256 hash;
            using (var ts = new TangramStream())
            {
                blockHeader.Transactions.Skip(1).ForEach(x => ts.Append(x.Stream()));
                hash = Hashes.DoubleSHA256(ts.ToArray());
            }

            var verifySolution =
                VerifySolution(blockHeader.VrfSig.HexToByte(), hash.ToBytes(false), blockHeader.Solution);
            if (verifySolution == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Could not verify the block header solution");
                return verifySolution;
            }

            var bits = Difficulty(blockHeader.Solution, blockHeader.Transactions.First().Vout.First().A.DivWithNaT());
            if (blockHeader.Bits != bits)
            {
                _logger.Here().Fatal("Could not verify the block header bits");
                return VerifyResult.UnableToVerify;
            }

            var merkel = blockHeader.MerkelRoot.HexToByte();
            blockHeader.MerkelRoot = null;

            var tempBlockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
            if (tempBlockHeader == null)
            {
                _logger.Here().Fatal("Could not add the block header to merkel");
                return VerifyResult.UnableToVerify;
            }

            blockHeader.MerkelRoot = merkel.ByteToHex();
            if (!blockHeader.MerkelRoot.Equals(tempBlockHeader.MerkelRoot))
            {
                _logger.Here().Fatal("Could not verify the block header merkel");
                return VerifyResult.UnableToVerify;
            }

            var verifyLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(blockHeader.Locktime)),
                blockHeader.LocktimeScript);
            if (verifyLockTime == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Could not verify the block header lock time");
                return verifyLockTime;
            }

            if (blockHeader.MerkelRoot.HexToByte().Xor(BlockZeroMerkel) &&
                blockHeader.PrevMerkelRoot.HexToByte().Xor(BlockZeroPreMerkel))
                return VerifyResult.Succeed;

            var prevBlock = await _unitOfWork.DeliveredRepository.GetAsync(x =>
                new ValueTask<bool>(x.MerkelRoot.Equals(blockHeader.PrevMerkelRoot)));

            if (prevBlock == null)
            {
                _logger.Here().Fatal("Could not find previous block header");
                return VerifyResult.UnableToVerify;
            }

            var verifyTransactions = await VerifyTransactions(blockHeader.Transactions);
            if (verifyTransactions == VerifyResult.Succeed) return VerifyResult.Succeed;

            _logger.Here().Fatal("Could not verify block header transactions");
            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="transactions"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransactions(TransactionProto[] transactions)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();

            foreach (var transaction in transactions)
            {
                var verifyTransaction = await VerifyTransaction(transaction);
                if (verifyTransaction == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Fatal("Could not verify transaction");
                    return verifyTransaction;
                }

                if (transaction.Vout.First().T == CoinType.fee)
                {
                    var verifyTransactionFee = VerifyTransactionFee(transaction);
                    if (verifyTransactionFee == VerifyResult.Succeed) continue;
                }
                else if (transaction.Vout.First().T == CoinType.Coinbase)
                {
                    continue;
                }

                _logger.Here().Fatal("Could not verify transaction fee");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransaction(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;

            var verifySum = VerifyCommitSum(transaction);
            if (verifySum == VerifyResult.UnableToVerify) return verifySum;

            var verifyBulletProof = VerifyBulletProof(transaction);
            if (verifyBulletProof == VerifyResult.UnableToVerify) return verifyBulletProof;

            var transactionTypeArray = transaction.Vout.Select(x => x.T.ToString()).ToArray();
            if (transactionTypeArray.Contains(CoinType.fee.ToString()) &&
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

                _logger.Here().Fatal("Could not verify the MLSAG transaction");

                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyTransactionFee(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var vout = transaction.Vout.First();

            if (vout.T != CoinType.fee) return VerifyResult.UnableToVerify;

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
            return (0.000012 * nByte).ConvertToUInt64();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinbase"></param>
        /// <param name="solution"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyCoinbaseTransaction(VoutProto coinbase, ulong solution)
        {
            Guard.Argument(coinbase, nameof(coinbase)).NotNull();
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();

            if (coinbase.Validate().Any()) return VerifyResult.UnableToVerify;
            if (coinbase.T != CoinType.Coinbase) return VerifyResult.UnableToVerify;

            var runningDistribution = await GetRunningDistribution() - coinbase.A.DivWithNaT();

            var verifyNetworkShare = VerifyNetworkShare(solution, coinbase.A.DivWithNaT(), runningDistribution);
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
        public async Task<VerifyResult> VerifyKeyImage(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;

            foreach (var vin in transaction.Vin)
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(t => t.Vin.First().Key.Image.Xor(vin.Key.Image))));

                if (blockHeaders.Count > 1) return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyOutputCommits(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var offSets = transaction.Vin.Select(v => v.Key).SelectMany(k => k.Offsets.Split(33)).ToArray();
            var list = offSets.Where((value, index) => index % 2 == 0).ToArray();

            foreach (var commit in list)
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(v => v.Vout.Any(c => c.C.Xor(commit)))));

                if (!blockHeaders.Any()) return VerifyResult.UnableToVerify;


                var vouts = blockHeaders.SelectMany(blockHeader => blockHeader.Transactions).SelectMany(x => x.Vout);
                if (vouts.Where(vout => vout.T == CoinType.Coinbase && vout.T == CoinType.fee)
                    .Select(vout => VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(vout.L)), vout.S))
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
                var sloth = new Sloth();

                var x = System.Numerics.BigInteger.Parse(vrfSig.ByteToHex(),
                    NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.ToStr());
                var p256 = System.Numerics.BigInteger.Parse(security.ToStr());

                if (x.Sign <= 0) x = -x;

                verifySloth = sloth.Verify(bits, x, y, p256);
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Could not verify sloth");
            }

            return verifySloth ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public ulong Reward(ulong solution, double runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative().NotNaN().NotInfinity();

            var networkShare = NetworkShare(solution, runningDistribution);
            return networkShare.ConvertToUInt64();
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<double> GetRunningDistribution()
        {
            var runningDistributionTotal = Distribution;

            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.SelectAsync(x =>
                    new ValueTask<BlockHeaderProto>(x));

                for (var i = 0; i < blockHeaders.Count; i++)
                    runningDistributionTotal -= NetworkShare(blockHeaders.ElementAt(i).Solution, runningDistributionTotal);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get running distribution");
            }

            return runningDistributionTotal;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public double NetworkShare(ulong solution, double runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative().NotNaN().NotInfinity();

            var r = (Distribution - runningDistribution).FromExponential(11);
            var percentage = r / (runningDistribution * 100) == 0 ? 0.1 : r / (runningDistribution * 100);

            percentage = percentage.FromExponential(11);
            return (solution * percentage / Distribution).FromExponential(11); ;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="previousNetworkShare"></param>
        /// <param name="runningDistributionTotal"></param>
        /// <returns></returns>
        public VerifyResult VerifyNetworkShare(ulong solution, double previousNetworkShare, double runningDistributionTotal)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();

            var previousRunningDistribution = runningDistributionTotal + previousNetworkShare;
            if (previousRunningDistribution > Distribution) return VerifyResult.UnableToVerify;

            var r = (Distribution - previousRunningDistribution).FromExponential(11);
            var percentage = r / (previousRunningDistribution * 100) == 0
                ? 0.1
                : r / (previousRunningDistribution * 100);

            percentage = percentage.FromExponential(11);

            var networkShare = (solution * percentage / Distribution).FromExponential(11);
            return networkShare == previousNetworkShare ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="networkShare"></param>
        /// <returns></returns>
        public int Difficulty(ulong solution, double networkShare)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(networkShare, nameof(networkShare)).NotNaN().NotInfinity().NotNegative();

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

            var calculating = true;
            long itr = 0;

            var target = new BigInteger(1, vrfSig);
            var hashTarget = new BigInteger(1, kernel);

            var hashTargetValue = new BigInteger((target.IntValue / hashTarget.BitCount).ToString()).Abs();
            var hashWeightedTarget = new BigInteger(1, kernel).Multiply(hashTargetValue);

            while (calculating)
            {
                var weightedTarget = target.Multiply(BigInteger.ValueOf(itr));
                if (hashWeightedTarget.CompareTo(weightedTarget) <= 0) calculating = false;

                itr++;
            }

            return (ulong)itr;
        }

        /// <summary>
        /// </summary>
        /// <param name="xChain"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyForkRule(BlockHeaderProto[] xChain)
        {
            Guard.Argument(xChain, nameof(xChain)).NotNull().NotEmpty();

            try
            {
                var xBlockHeader = xChain.First();
                var blockHeader = await _unitOfWork.DeliveredRepository.LastAsync();

                if (blockHeader.MerkelRoot.Equals(xBlockHeader.PrevMerkelRoot)) return VerifyResult.Invalid;

                blockHeader = await _unitOfWork.DeliveredRepository.GetAsync(x =>
                    new ValueTask<bool>(x.MerkelRoot.Equals(xBlockHeader.PrevMerkelRoot)));

                if (blockHeader == null) return VerifyResult.UnableToVerify;

                var blockIndex = (await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.MerkelRoot != blockHeader.PrevMerkelRoot))).Count;

                var blockHeaders = await _unitOfWork.DeliveredRepository.SkipAsync(blockIndex);

                if (blockHeaders.Any())
                {
                    var blockTime = blockHeaders.First().Locktime.FromUnixTimeSeconds() -
                                    blockHeaders.Last().Locktime.FromUnixTimeSeconds();
                    var xBlockTime = xChain.First().Locktime.FromUnixTimeSeconds() -
                                     xChain.Last().Locktime.FromUnixTimeSeconds();
                    if (xBlockTime < blockTime) return VerifyResult.Succeed;
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

            _logger.Here().Fatal("Could not verify the MLSAG transaction");
            return null;
        }
    }
}