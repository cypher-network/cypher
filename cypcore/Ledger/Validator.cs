// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>
    /// 
    /// </summary>
    public class Validator : IValidator
    {
        private const double Distribution = 29858560.875;
        private const int FeeNByte = 6000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signingProvider;
        private readonly ILogger _logger;

        private double _distribution;
        private double _runningDistributionTotal;

        public Validator(IUnitOfWork unitOfWork, ISigning signingProvider, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
            _logger = logger.ForContext("SourceContext", nameof(Validator));

            SetInitialRunningDistribution(Distribution);
        }

        public uint StakeTimestampMask => 0x0000000A;
        public byte[] BlockZeroMerkel => "c10ab10e789edccd02fbb02d9c58e962416729a795ebee19aa85bac15a9e320c".HexToByte();

        public byte[] BlockZeroPreMerkel => "3030303030303030437970686572204e6574776f726b2076742e322e32303231".HexToByte();

        public byte[] Seed =>
            "6b341e59ba355e73b1a8488e75b617fe1caa120aa3b56584a217862840c4f7b5d70cefc0d2b36038d67a35b3cd406d54f8065c1371a17a44c1abb38eea8883b2"
                .HexToByte();

        public byte[] Security256 =>
            "60464814417085833675395020742168312237934553084050601624605007846337253615407".ToBytes();

        /// <summary>
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        public Task<bool> VerifyMemPoolSignatures(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                if (!_signingProvider.VerifySignature(memPool.Block.Signature.HexToByte(),
                    memPool.Block.PublicKey.HexToByte(), memPool.Block.Transaction.ToHash()))
                {
                    _logger.Here().Error("Unable to verify signature for block {@Round} from node {@Node}",
                        memPool.Block.Round,
                        memPool.Block.Node);

                    return Task.FromResult(false);
                }

                if (memPool.Prev != null && memPool.Prev?.Round != 0)
                {
                    if (!_signingProvider.VerifySignature(memPool.Prev.Signature.HexToByte(),
                        memPool.Prev.PublicKey.HexToByte(), memPool.Prev.Transaction.ToHash()))
                    {
                        _logger.Here().Error("Unable to verify signature for previous block on block {@Round} from node {@Node}",
                            memPool.Block.Round,
                            memPool.Block.Node);

                        return Task.FromResult(false);
                    }

                    if (memPool.Prev.Node != memPool.Block.Node)
                    {
                        _logger.Here().Error("Previous block node does not match on block {@Round} from node {@Node}",
                            memPool.Block.Round,
                            memPool.Block.Node);

                        return Task.FromResult(false);
                    }

                    if (memPool.Prev.Round + 1 != memPool.Block.Round)
                    {
                        _logger.Here().Error("Previous block round is invalid on block {@Round} from node {@Node}",
                            memPool.Block.Round,
                            memPool.Block.Node);

                        return Task.FromResult(false);
                    }
                }

                foreach (var dep in memPool.Deps)
                {
                    if (!_signingProvider.VerifySignature(dep.Block.Signature.HexToByte(),
                        dep.Block.PublicKey.HexToByte(), dep.Block.Transaction.ToHash()))
                    {
                        _logger.Here().Error("Unable to verify signature for block reference {@Round} from node {@Node}",
                            memPool.Block.Round,
                            memPool.Block.Node);

                        return Task.FromResult(false);
                    }

                    if (dep.Block.Node != memPool.Block.Node) continue;
                    
                    _logger.Here().Error("Block references includes a block from same node in block reference {@Round} from node {@Node}",
                        memPool.Block.Round,
                        memPool.Block.Node);

                    return Task.FromResult(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify memory pool signatures");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public bool VerifyBulletProof(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(transaction.Vout, nameof(transaction)).NotNull();

            try
            {
                var elements = transaction.Validate().Any();
                if (!elements)
                {
                    using var secp256K1 = new Secp256k1();
                    using var bulletProof = new BulletProof();

                    if (transaction.Bp.Select((t, i) => bulletProof.Verify(transaction.Vout[i + 2].C, t.Proof, null)).Any(verified => !verified))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify bulletproof");
                return false;
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public bool VerifyCommitSum(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            try
            {
                var elements = transaction.Validate().Any();
                if (!elements)
                {
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
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify commit sum");
                return false;
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="poSCommitBlindSwitch"></param>
        /// <returns></returns>
        public bool VerifyCommitBlindSwitch(PoSCommitBlindSwitchProto poSCommitBlindSwitch)
        {
            Guard.Argument(poSCommitBlindSwitch, nameof(poSCommitBlindSwitch)).NotNull();

            try
            {
                using var pedersen = new Pedersen();

                return pedersen.VerifyCommitSum(new List<byte[]> { poSCommitBlindSwitch.Balance.HexToByte() },
                    new List<byte[]>
                    {
                        poSCommitBlindSwitch.Difficulty.HexToByte(), poSCommitBlindSwitch.Difference.HexToByte()
                    });
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot verify blind switch");
                return false;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="vrfBytes"></param>
        /// <param name="kernel"></param>
        /// <param name="solution"></param>
        /// <returns></returns>
        public bool VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution)
        {
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(32);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();

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

            return isSolution;
        }

        /// <summary>
        /// </summary>
        /// <param name="blockHeaders"></param>
        /// <returns></returns>
        public async Task<bool> VerifyBlockHeaders(BlockHeaderProto[] blockHeaders)
        {
            Guard.Argument(blockHeaders, nameof(blockHeaders)).NotNull();

            foreach (var blockHeader in blockHeaders)
            {
                var verified = await VerifyBlockHeader(blockHeader);
                if (verified) continue;

                _logger.Here().Fatal("Could not verify block header");

                return false;
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public async Task<bool> VerifyBlockHeader(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();

            var verified = VerifySloth(blockHeader.Bits, blockHeader.VrfSig.HexToByte(), blockHeader.Nonce.ToBytes(),
                blockHeader.Sec.ToBytes());
            if (!verified)
            {
                _logger.Here().Fatal("Could not verify the block header sloth");
                return false;
            }

            if (blockHeader.MrklRoot.HexToByte().Xor(BlockZeroMerkel) &&
                blockHeader.PrevMrklRoot.HexToByte().Xor(BlockZeroPreMerkel))
                _runningDistributionTotal -= blockHeader.Transactions.First().Vout.First().A.DivWithNaT();

            verified = VerifyCoinbaseTransaction(blockHeader.Transactions.First(), blockHeader.Solution);
            if (!verified)
            {
                _logger.Here().Fatal("Could not verify the block header coinbase transaction");
                return false;
            }

            uint256 hash;
            using (var ts = new TangramStream())
            {
                blockHeader.Transactions.Skip(1).ForEach(x => ts.Append(x.Stream()));
                hash = Hashes.DoubleSHA256(ts.ToArray());
            }

            var solution = VerifySolution(blockHeader.VrfSig.HexToByte(), hash.ToBytes(false), blockHeader.Solution);
            if (!solution)
            {
                _logger.Here().Fatal("Could not verify the block header solution");
                return false;
            }

            var bits = Difficulty(blockHeader.Solution, blockHeader.Transactions.First().Vout.First().A.DivWithNaT());
            if (blockHeader.Bits != bits)
            {
                _logger.Here().Fatal("Could not verify the block header bits");
                return false;
            }

            var merkel = blockHeader.MrklRoot.HexToByte();
            blockHeader.MrklRoot = null;

            var tempBlockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
            if (tempBlockHeader == null)
            {
                _logger.Here().Fatal("Could not add the block header to merkel");
                return false;
            }

            blockHeader.MrklRoot = merkel.ByteToHex();

            if (blockHeader.MrklRoot != _unitOfWork.DeliveredRepository.MerkleRoot.ByteToHex())
            {
                _logger.Here().Fatal("Could not verify the block header merkel");
                return false;
            }

            _unitOfWork.DeliveredRepository.ResetTrie();

            verified = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(blockHeader.Locktime)),
                blockHeader.LocktimeScript);
            if (!verified)
            {
                _logger.Here().Fatal("Could not verify the block header lock time");
                return false;
            }

            if (blockHeader.MrklRoot.HexToByte().Xor(BlockZeroMerkel) &&
                blockHeader.PrevMrklRoot.HexToByte().Xor(BlockZeroPreMerkel))
                return true;

            var prevBlock = await _unitOfWork.DeliveredRepository.FirstAsync(x =>
                new ValueTask<bool>(x.MrklRoot.Equals(blockHeader.PrevMrklRoot)));

            if (prevBlock == null)
            {
                _logger.Here().Fatal("Could not find previous block header");
                return false;
            }

            verified = await VerifyTransactions(blockHeader.Transactions);
            if (verified) return true;

            _logger.Here().Fatal("Could not verify block header transactions");
            return false;

        }

        /// <summary>
        /// </summary>
        /// <param name="transactions"></param>
        /// <returns></returns>
        public async Task<bool> VerifyTransactions(HashSet<TransactionProto> transactions)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();

            foreach (var tx in transactions)
            {
                var verified = await VerifyTransaction(tx);
                if (!verified)
                {
                    _logger.Here().Fatal("Could not verify transaction");
                    return false;
                }

                verified = VerifyTransactionFee(tx);
                if (verified) continue;

                _logger.Here().Fatal("Could not verify transaction fee");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<bool> VerifyTransaction(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var notValid = transaction.Validate().Any();
            if (notValid) return false;

            var verifySum = VerifyCommitSum(transaction);
            if (!verifySum) return false;

            var verifyBulletProof = VerifyBulletProof(transaction);
            if (!verifyBulletProof) return false;

            var verifyVOutCommits = await VerifyVOutCommits(transaction);
            if (!verifyVOutCommits) return false;

            var verifyKImage = await VerifyKimage(transaction);
            if (!verifyKImage) return false;

            using var mlsag = new MLSAG();
            for (var i = 0; i < transaction.Vin.Length; i++)
            {
                var m = PrepareMlsag(transaction.Rct[i].M, transaction.Vout, transaction.Vin[i].Key.K_Offsets,
                    transaction.Mix, 2);

                var verifyMlsag = mlsag.Verify(transaction.Rct[i].I, transaction.Mix, 2, m,
                    transaction.Vin[i].Key.K_Image, transaction.Rct[i].P, transaction.Rct[i].S);
                if (verifyMlsag) continue;

                _logger.Here().Fatal("Could not verify the MLSAG transaction");

                return false;
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public bool VerifyTransactionFee(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var vout = transaction.Vout.First();

            if (vout.T != CoinType.fee) return false;

            var feeRate = Fee(FeeNByte);
            if (vout.A != feeRate) return false;

            using var pedersen = new Pedersen();

            var commitSum = pedersen.CommitSum(new List<byte[]> { vout.C }, new List<byte[]> { vout.C });
            return commitSum == null;
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
        /// <param name="transaction"></param>
        /// <param name="solution"></param>
        /// <returns></returns>
        public bool VerifyCoinbaseTransaction(TransactionProto transaction, ulong solution)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var vout = transaction.Vout.First();
            if (vout.T != CoinType.Coinbase) return false;

            var verified = VerifyNetworkShare(solution, vout.A.DivWithNaT(), ref _runningDistributionTotal);
            if (!verified) return false;

            using var pedersen = new Pedersen();
            var commitSum = pedersen.CommitSum(new List<byte[]> { vout.C }, new List<byte[]> { vout.C });

            return commitSum == null;
        }

        /// <summary>
        /// </summary>
        /// <param name="target"></param>
        /// <param name="script"></param>
        /// <returns></returns>
        public bool VerifyLockTime(LockTime target, string script)
        {
            Guard.Argument(target, nameof(target)).NotDefault();
            Guard.Argument(script, nameof(script)).NotNull().NotEmpty();

            var sc1 = new Script(Op.GetPushOp(target.Value), OpcodeType.OP_CHECKLOCKTIMEVERIFY);
            var sc2 = new Script(script);

            if (!sc1.ToString().Equals(sc2.ToString())) return false;

            var tx = NBitcoin.Network.Main.CreateTransaction();
            tx.Outputs.Add(new TxOut { ScriptPubKey = new Script(script) });

            var spending = NBitcoin.Network.Main.CreateTransaction();
            spending.LockTime = new LockTime(DateTimeOffset.Now);
            spending.Inputs.Add(new TxIn(tx.Outputs.AsCoins().First().Outpoint, new Script()));
            spending.Inputs[0].Sequence = 1;

            return spending.Inputs.AsIndexedInputs().First().VerifyScript(tx.Outputs[0]);
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<bool> VerifyKimage(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var elements = transaction.Validate().Any();
            if (!elements)
                foreach (var vin in transaction.Vin)
                {
                    var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                        new ValueTask<bool>(x.Transactions.Any(t => t.Vin.First().Key.K_Image.Xor(vin.Key.K_Image))));

                    if (blockHeaders.Count > 1) return false;
                }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<bool> VerifyVOutCommits(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            foreach (var commit in transaction.Vin.Select(v => v.Key).SelectMany(k => k.K_Offsets.Split(33)))
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Transactions.Any(t => t.Vout.FirstOrDefault().C.Xor(commit))));

                if (!blockHeaders.Any()) return false;

                var vouts = blockHeaders.SelectMany(blockHeader => blockHeader.Transactions).SelectMany(x => x.Vout);
                if (vouts.Where(vout => vout.T == CoinType.Coinbase && vout.T == CoinType.fee).Select(vout => VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(vout.L)), vout.S)).Any(verified => !verified))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="vrfSig"></param>
        /// <param name="nonce"></param>
        /// <param name="security"></param>
        /// <returns></returns>
        public bool VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            Guard.Argument(nonce, nameof(nonce)).NotNull().MaxCount(77);
            Guard.Argument(security, nameof(security)).NotNull().MaxCount(77);

            var verified = false;

            try
            {
                var sloth = new Sloth();

                var x = System.Numerics.BigInteger.Parse(vrfSig.ByteToHex(),
                    NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.ToStr());
                var p256 = System.Numerics.BigInteger.Parse(security.ToStr());

                if (x.Sign <= 0) x = -x;

                verified = sloth.Verify(bits, x, y, p256);
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Could not verify sloth");
            }

            return verified;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public ulong Reward(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var networkShare = NetworkShare(solution);

            _runningDistributionTotal -= networkShare;
            return networkShare.ConvertToUInt64();
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<double> GetRunningDistribution()
        {
            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.SelectAsync(x =>
                    new ValueTask<BlockHeaderProto>(x));

                for (var i = 0; i < blockHeaders.Count; i++)
                    _runningDistributionTotal -= NetworkShare(blockHeaders.ElementAt(i).Solution);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Cannot get running distribution");
            }

            return _runningDistributionTotal;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public double NetworkShare(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var r = (_distribution - _runningDistributionTotal).FromExponential(11);
            var percentage = r / (_runningDistributionTotal * 100) == 0 ? 0.1 : r / (_runningDistributionTotal * 100);

            percentage = percentage.FromExponential(11);

            var networkShare = (solution * percentage / _distribution).FromExponential(11);

            return networkShare;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="previousNetworkShare"></param>
        /// <param name="runningDistributionTotal"></param>
        /// <returns></returns>
        public bool VerifyNetworkShare(ulong solution, double previousNetworkShare, ref double runningDistributionTotal)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var previousRunningDistribution = runningDistributionTotal + previousNetworkShare;
            var r = (_distribution - previousRunningDistribution).FromExponential(11);
            var percentage = r / (previousRunningDistribution * 100) == 0
                ? 0.1
                : r / (previousRunningDistribution * 100);

            percentage = percentage.FromExponential(11);

            var networkShare = (solution * percentage / _distribution).FromExponential(11);

            return networkShare == previousNetworkShare;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="networkShare"></param>
        /// <returns></returns>
        public int Difficulty(ulong solution, double networkShare)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();
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

            var calculating = true;
            long itrr = 0;

            var target = new BigInteger(1, vrfSig);
            var hashTarget = new BigInteger(1, kernel);

            var hashTargetValue = new BigInteger((target.IntValue / hashTarget.BitCount).ToString()).Abs();
            var hashWeightedTarget = new BigInteger(1, kernel).Multiply(hashTargetValue);

            while (calculating)
            {
                var weightedTarget = target.Multiply(BigInteger.ValueOf(itrr));
                if (hashWeightedTarget.CompareTo(weightedTarget) <= 0) calculating = false;

                itrr++;
            }

            return (ulong)itrr;
        }

        /// <summary>
        /// </summary>
        /// <param name="xChain"></param>
        /// <returns></returns>
        public async Task<bool> ForkRule(BlockHeaderProto[] xChain)
        {
            Guard.Argument(xChain, nameof(xChain)).NotNull();

            try
            {
                var xBlockHeader = xChain.First();
                var blockHeader = await _unitOfWork.DeliveredRepository.LastAsync();

                if (blockHeader.MrklRoot.Equals(xBlockHeader.PrevMrklRoot)) return false;

                blockHeader = await _unitOfWork.DeliveredRepository.FirstAsync(x =>
                    new ValueTask<bool>(x.MrklRoot.Equals(xBlockHeader.PrevMrklRoot)));

                if (blockHeader == null) return false;

                var blockIndex = (await _unitOfWork.DeliveredRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.MrklRoot != blockHeader.PrevMrklRoot))).Count;

                var blockHeaders = await _unitOfWork.DeliveredRepository.SkipAsync(blockIndex);

                if (blockHeaders.Any())
                {
                    var blockTime = blockHeaders.First().Locktime.FromUnixTimeSeconds() -
                                    blockHeaders.Last().Locktime.FromUnixTimeSeconds();
                    var xBlockTime = xChain.First().Locktime.FromUnixTimeSeconds() -
                                     xChain.Last().Locktime.FromUnixTimeSeconds();
                    if (xBlockTime < blockTime) return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Error while processing fork rule");
            }

            return false;
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
        /// <param name="distribution"></param>
        private void SetInitialRunningDistribution(double distribution)
        {
            _distribution = distribution;
            _runningDistributionTotal = distribution;
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

            var pcmIn = new Span<byte[]>(new byte[cols * 1][]);
            var pcmOut = new Span<byte[]>(new byte[3][] { vout[0].C, vout[1].C, vout[2].C });
            var kOffsets = keyOffset.Split(33).Select(x => x).ToList();

            pcmIn[0] = kOffsets.ElementAt(0);
            pcmIn[1] = kOffsets.ElementAt(2);

            using var mlsag = new MLSAG();

            var prepareMlsag = mlsag.Prepare(m, null, pcmOut.Length, pcmOut.Length, cols, rows, pcmIn, pcmOut, null);
            if (prepareMlsag) return m;

            _logger.Here().Fatal("Could not verify the MLSAG transaction");
            return null;
        }
    }
}