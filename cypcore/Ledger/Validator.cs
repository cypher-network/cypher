// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Dawn;

using BigInteger = NBitcoin.BouncyCastle.Math.BigInteger;

using Libsecp256k1Zkp.Net;

using NBitcoin;

using CYPCore.Extentions;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Extensions;
using CYPCore.Cryptography;
using NBitcoin.Crypto;

namespace CYPCore.Ledger
{
    public class Validator : IValidator
    {
        protected readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signingProvider;
        private readonly ILogger _logger;

        // TODO:
        // Distribution hard coded..
        private double _distribution = 119434243.5;
        private double _runningDistributionTotal;

        public Validator(IUnitOfWork unitOfWork, ISigning signingProvider, ILogger<Validator> logger)
        {
            _unitOfWork = unitOfWork;
            _signingProvider = signingProvider;
            _logger = logger;
        }

        public int DefualtMiningDifficulty => 20555;
        public uint StakeTimestampMask => 0x0000000A;
        public byte[] Seed => "6b341e59ba355e73b1a8488e75b617fe1caa120aa3b56584a217862840c4f7b5d70cefc0d2b36038d67a35b3cd406d54f8065c1371a17a44c1abb38eea8883b2".HexToByte();
        public byte[] Security256 => "60464814417085833675395020742168312237934553084050601624605007846337253615407".ToBytes();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memPool"></param>
        /// <returns></returns>
        public Task<bool> VerifyMemPoolSignatures(MemPoolProto memPool)
        {
            Guard.Argument(memPool, nameof(memPool)).NotNull();

            try
            {
                if (!_signingProvider.VerifySignature(memPool.Block.Signature.HexToByte(), memPool.Block.PublicKey.HexToByte(), memPool.Block.Transaction.ToHash()))
                {
                    _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Unable to verify signature for block {memPool.Block.Round} from node {memPool.Block.Node}");
                    return Task.FromResult(false);
                }

                if (memPool.Prev != null && memPool.Prev?.Round != 0)
                {
                    if (!_signingProvider.VerifySignature(memPool.Prev.Signature.HexToByte(), memPool.Prev.PublicKey.HexToByte(), memPool.Prev.Transaction.ToHash()))
                    {
                        _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Unable to verify signature for previous block on block {memPool.Block.Round} from node {memPool.Block.Node}");
                        return Task.FromResult(false);
                    }

                    if (memPool.Prev.Node != memPool.Block.Node)
                    {
                        _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Previous block node does not match on block {memPool.Block.Round} from node {memPool.Block.Node}");
                        return Task.FromResult(false);
                    }

                    if (memPool.Prev.Round + 1 != memPool.Block.Round)
                    {
                        _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Previous block round is invalid on block {memPool.Block.Round} from node {memPool.Block.Node}");
                        return Task.FromResult(false);
                    }
                }

                for (int i = 0; i < memPool.Deps.Count; i++)
                {
                    var dep = memPool.Deps[i];

                    if (!_signingProvider.VerifySignature(dep.Block.Signature.HexToByte(), dep.Block.PublicKey.HexToByte(), dep.Block.Transaction.ToHash()))
                    {
                        _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Unable to verify signature for block reference {memPool.Block.Round} from node {memPool.Block.Node}");
                        return Task.FromResult(false);
                    }

                    if (dep.Block.Node == memPool.Block.Node)
                    {
                        _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: Block references includes a block from same node in block reference  {memPool.Block.Round} from node {memPool.Block.Node}");
                        return Task.FromResult(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Validator.VerifiyMemPoolSignatures >>>: {ex}");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 
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
                    using var secp256k1 = new Secp256k1();
                    using var bulletProof = new BulletProof();

                    for (int i = 0; i < transaction.Bp.Length; i++)
                    {
                        var verified = bulletProof.Verify(transaction.Vout[i + 2].C, transaction.Bp[i].Proof, null);
                        if (!verified)
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Validator.VerifyBulletProof >>>: {ex}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
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

                    for (int i = 0; i < transaction.Vout.Length / 3; i++)
                    {
                        var fee = transaction.Vout[i].C;
                        var payment = transaction.Vout[i + 1].C;
                        var change = transaction.Vout[i + 2].C;

                        var commitSumBalance = pedersen.CommitSum(new List<byte[]> { fee, payment, change }, new List<byte[]> { });
                        if (!pedersen.VerifyCommitSum(new List<byte[]> { commitSumBalance }, new List<byte[]> { fee, payment, change }))
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Validator.VerifyCommitSum >>>: {ex}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="poSCommitBlindSwitch"></param>
        /// <returns></returns>
        public bool VerifyCommitBlindSwitch(PoSCommitBlindSwitchProto poSCommitBlindSwitch)
        {
            Guard.Argument(poSCommitBlindSwitch, nameof(poSCommitBlindSwitch)).NotNull();

            try
            {
                using var pedersen = new Pedersen();

                return pedersen.VerifyCommitSum(
                    new List<byte[]> { poSCommitBlindSwitch.Balance.HexToByte() },
                    new List<byte[]> { poSCommitBlindSwitch.Difficulty.HexToByte(), poSCommitBlindSwitch.Difference.HexToByte() });
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Validator.VerifyCommitBlindSwitch >>>: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 
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

            bool isSolution = false;

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
                _logger.LogError($"<<< Validator.VerifySolution >>>: {ex}");
            }

            return isSolution;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeaders"></param>
        /// <returns></returns>
        public async Task<bool> VerifyBlockHeaders(IEnumerable<BlockHeaderProto> blockHeaders)
        {
            Guard.Argument(blockHeaders, nameof(blockHeaders)).NotNull();

            foreach (var blockHeader in blockHeaders)
            {
                var verified = await VerifyBlockHeader(blockHeader);
                if (!verified)
                {
                    _logger.LogCritical($"<<< Validator.VerifyBlockHeaders >>>: Could not verify block header");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockHeader"></param>
        /// <returns></returns>
        public async Task<bool> VerifyBlockHeader(BlockHeaderProto blockHeader)
        {
            Guard.Argument(blockHeader, nameof(blockHeader)).NotNull();

            var verified = VerifySloth(blockHeader.Bits, blockHeader.VrfSig.HexToByte(), blockHeader.Nonce.ToBytes(), blockHeader.SecKey256.ToBytes());
            if (!verified)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header solth");
                return false;
            }

            uint256 hash;
            using (var ts = new Helper.TangramStream())
            {
                blockHeader.Transactions.ForEach(x => ts.Append(x.Stream()));
                hash = Hashes.DoubleSHA256(ts.ToArray());
            }

            var solution = Solution(blockHeader.VrfSig.HexToByte(), hash.ToBytes(false));
            var networkShare = NetworkShare(solution);
            var bits = Difficulty(solution, networkShare);

            if (blockHeader.Bits != bits)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header bits");
                return false;
            }

            blockHeader = _unitOfWork.DeliveredRepository.ToTrie(blockHeader);
            if (blockHeader == null)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not add the block header to merkel");
                return false;
            }

            if (blockHeader.MrklRoot != _unitOfWork.DeliveredRepository.MrklRoot.ByteToHex())
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header merkel");
                return false;
            }

            _unitOfWork.DeliveredRepository.ResetTrie();

            var prevBlock = await _unitOfWork.DeliveredRepository.FirstOrDefaultAsync(x => x.MrklRoot == blockHeader.PrevMrklRoot);
            if (prevBlock == null)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not find previous block header");
                return false;
            }

            if (blockHeader.Solution != solution)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header solution");
                return false;
            }

            verified = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(blockHeader.Locktime)), blockHeader.LocktimeScript);
            if (!verified)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header locktime");
                return false;
            }

            verified = VerifyCoinbaseTransaction(blockHeader.Transactions.First(), solution);
            if (!verified)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header transactions");
                return false;
            }

            verified = await VerifyTransactions(blockHeader.Transactions);
            if (!verified)
            {
                _logger.LogCritical($"<<< Validator.VerifyBlockHeader >>>: Could not verify the block header transactions");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Transactions"></param>
        /// <returns></returns>
        public async Task<bool> VerifyTransactions(HashSet<TransactionProto> transactions)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();

            foreach (var tx in transactions)
            {
                var verified = await VerifyTransaction(tx);
                if (!verified)
                {
                    _logger.LogCritical($"<<< Validator.VerifyTransactions >>>: Could not verify the transaction");
                    return false;
                }

                verified = VerifyTransactionFee(tx);
                if (!verified)
                {
                    _logger.LogCritical($"<<< Validator.VerifyTransactions >>>: Could not verify the transaction fee");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public async Task<bool> VerifyTransaction(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var notValid = transaction.Validate().Any();
            if (notValid)
            {
                return false;
            }

            var verifySum = VerifyCommitSum(transaction);
            if (!verifySum)
            {
                return false;
            }

            var verifyBulletProof = VerifyBulletProof(transaction);
            if (!verifyBulletProof)
            {
                return false;
            }

            var verifyVoutCommits = await VerifyVoutCommits(transaction);
            if (!verifyVoutCommits)
            {
                return false;
            }

            var verifyKImage = await VerifyKimage(transaction);
            if (!verifyKImage)
            {
                return false;
            }

            using var mlsag = new MLSAG();
            for (int i = 0; i < transaction.Vin.Length; i++)
            {
                byte[] Prepare(byte[] m, byte[] keyOffset, int mix, int rows)
                {
                    var pcm_in = new Span<byte[]>(new byte[mix * 1][]);
                    var pcm_out = new Span<byte[]>(new byte[3][]);
                    var success = mlsag.Prepare(m, null, 3, 3, mix, rows, pcm_in, pcm_out, null);

                    var offsets = keyOffset.Split(33);

                    foreach (var cin in offsets.Take(mix).Select((value, i) => (i, value)))
                    {
                        pcm_in[cin.i] = cin.value;
                    }

                    foreach (var cout in offsets.Skip(mix).Select((value, i) => (i, value)))
                    {
                        pcm_out[cout.i] = cout.value;
                    }

                    var prepareMLSAG = mlsag.Prepare(m, null, pcm_out.Length, pcm_out.Length, mix, rows, pcm_in, pcm_out, null);
                    if (!prepareMLSAG)
                    {
                        _logger.LogCritical($"<<< Validator.VerifyTransaction >>>: Could not verify the MLSAG transaction");
                        return null;
                    }

                    return m;
                }

                var m = Prepare(transaction.Rct[i].M, transaction.Vin[i].Key.K_Offsets, transaction.Mix, 2);
                var verifyMLSAG = mlsag.Verify(transaction.Rct[i].I, transaction.Mix, 2, m, transaction.Vin[i].Key.K_Image, transaction.Rct[i].P, transaction.Rct[i].S);
                if (!verifyMLSAG)
                {
                    _logger.LogCritical($"<<< Validator.VerifyTransaction >>>: Could not verify the MLSAG transaction");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public bool VerifyTransactionFee(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var vout = transaction.Vout.First();
            if (vout.A < 0)
            {
                return false;
            }

            if (vout.T != CoinType.fee)
            {
                return false;
            }

            var feeRate = Fee(transaction.Stream().Length);
            if (vout.A != feeRate)
            {
                return false;
            }

            using var pedersen = new Pedersen();

            var commitSum = pedersen.CommitSum(new List<byte[]> { vout.C }, new List<byte[]> { vout.C });
            if (commitSum != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nByte"></param>
        /// <returns></returns>
        public ulong Fee(int nByte)
        {
            return ((double)0.000012 * nByte).ConvertToUInt64();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public bool VerifyCoinbaseTransaction(TransactionProto transaction, ulong solution)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var vout = transaction.Vout.First();
            if (vout.T != CoinType.Coinbase)
            {
                return false;
            }

            var reward = Reward(solution);
            if (vout.A != reward)
            {
                return false;
            }

            using var pedersen = new Pedersen();
            var commitSum = pedersen.CommitSum(new List<byte[]> { vout.C }, new List<byte[]> { vout.C });
            if (commitSum != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
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

            if (!sc1.ToString().Equals(sc2.ToString()))
            {
                return false;
            }

            var tx = NBitcoin.Network.Main.CreateTransaction();
            tx.Outputs.Add(new TxOut()
            {
                ScriptPubKey = new Script(script)
            });

            var spending = NBitcoin.Network.Main.CreateTransaction();
            spending.LockTime = new LockTime(DateTimeOffset.Now);
            spending.Inputs.Add(new TxIn(tx.Outputs.AsCoins().First().Outpoint, new Script()));
            spending.Inputs[0].Sequence = 1;

            return spending.Inputs.AsIndexedInputs().First().VerifyScript(tx.Outputs[0]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<bool> VerifyKimage(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            var elements = transaction.Validate().Any();
            if (!elements)
            {
                foreach (var vin in transaction.Vin)
                {
                    var blockHeaders = await _unitOfWork.DeliveredRepository
                        .WhereAsync(x => new ValueTask<bool>(x.Transactions.Any(t => t.Vin.First().Key.K_Image.SequenceEqual(vin.Key.K_Image))));

                    if (blockHeaders.Count() > 1)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<bool> VerifyVoutCommits(TransactionProto transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();

            foreach (var commit in transaction.Vin.Select(v => v.Key).SelectMany(k => k.K_Offsets.Split(33)))
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository
                    .WhereAsync(x => new ValueTask<bool>(x.Transactions.Any(t => t.Vout.FirstOrDefault().C.SequenceEqual(commit))));

                if (!blockHeaders.Any())
                {
                    return false;
                }

                var vouts = blockHeaders.SelectMany(blockHeader => blockHeader.Transactions).SelectMany(x => x.Vout);
                foreach (var vout in vouts.Where(vout => vout.T == CoinType.Coinbase && vout.T == CoinType.fee))
                {
                    var verified = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(vout.L)), vout.S);
                    if (!verified)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="proof"></param>
        /// <param name="nonce"></param>
        /// <param name="security"></param>
        /// <returns></returns>
        public bool VerifySloth(int bits, byte[] vrfSig, byte[] nonce, byte[] security)
        {
            Guard.Argument(bits, nameof(bits)).NotNegative().NotZero();
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            Guard.Argument(nonce, nameof(nonce)).NotNull().MaxCount(77);
            Guard.Argument(security, nameof(security)).NotNull().MaxCount(77);

            bool verified = false;

            try
            {
                var sloth = new Sloth();

                var x = System.Numerics.BigInteger.Parse(vrfSig.ByteToHex(), System.Globalization.NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.ToStr());
                var p256 = System.Numerics.BigInteger.Parse(security.ToStr());

                if (x.Sign <= 0)
                {
                    x = -x;
                }

                verified = sloth.Verify(bits, x, y, p256);
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"<<< Validator.VerifySloth >>>: {ex.Message}");
            }

            return verified;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public ulong Reward(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();

            _runningDistributionTotal -= Math.Truncate(NetworkShare(solution));
            return Math.Truncate(NetworkShare(solution)).ConvertToUInt64();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="distribution"></param>
        public void SetRunningDistribution(double distribution)
        {
            _runningDistributionTotal = distribution;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<double> GetRunningDistribution()
        {
            double runningDistributionTotal = 0d;

            try
            {
                var blockHeaders = await _unitOfWork.DeliveredRepository.SelectAsync(x => new ValueTask<BlockHeaderProto>(x));
                for (int i = 0; i < blockHeaders.Count(); i++)
                {
                    runningDistributionTotal -= Math.Truncate(NetworkShare(blockHeaders.ElementAt(i).Solution));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Validator.GetRunningDistribution >>>: {ex}");
            }

            return runningDistributionTotal;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public double NetworkShare(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();

            var totalPrecentage = _runningDistributionTotal * (100 / _distribution);
            var networkShare = solution * (totalPrecentage / _distribution);

            return networkShare;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="networkShare"></param>
        /// <returns></returns>
        public int Difficulty(ulong solution, double networkShare)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative();
            Guard.Argument(networkShare, nameof(networkShare)).NotNegative();

            var diff = solution * (Convert.ToDouble($"0.0{Math.Truncate(networkShare)}") / 100);

            diff = diff == 0 ? 1 : diff;

            return (int)diff;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="vrfBytes"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        public ulong Solution(byte[] vrfSig, byte[] kernel)
        {
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);

            bool calculating = true;
            long itrr = 0;

            var target = new BigInteger(1, vrfSig);
            var hashTarget = new BigInteger(1, kernel);

            var hashTargetValue = new BigInteger((target.IntValue / hashTarget.BitCount).ToString()).Abs();
            var hashWeightedTarget = new BigInteger(1, kernel).Multiply(hashTargetValue);

            while (calculating)
            {
                var weightedTarget = target.Multiply(BigInteger.ValueOf(itrr));
                if (hashWeightedTarget.CompareTo(weightedTarget) <= 0)
                {
                    calculating = false;
                }

                itrr++;
            }

            return (ulong)itrr;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="xChain"></param>
        /// <returns></returns>
        public async Task<bool> ForkRule(IEnumerable<BlockHeaderProto> xChain)
        {
            Guard.Argument(xChain, nameof(xChain)).NotNull();

            try
            {
                var xBlockHeader = xChain.First();
                var blockHeader = await _unitOfWork.DeliveredRepository.LastOrDefaultAsync();

                if (blockHeader.MrklRoot.Equals(xBlockHeader.PrevMrklRoot))
                {
                    return false;
                }

                blockHeader = await _unitOfWork.DeliveredRepository.FirstOrDefaultAsync(x => x.MrklRoot.Equals(xBlockHeader.PrevMrklRoot));
                if (blockHeader == null)
                {
                    return false;
                }

                var blockIndex = (await _unitOfWork.DeliveredRepository.TakeWhileAsync(x => new ValueTask<bool>(x.MrklRoot != blockHeader.PrevMrklRoot))).Count();
                var blockHeaders = await _unitOfWork.DeliveredRepository.TakeLastAsync(blockIndex);

                if (blockHeaders.Any())
                {
                    var blockTime = blockHeaders.First().Locktime.FromUnixTimeSeconds() - blockHeaders.Last().Locktime.FromUnixTimeSeconds();
                    var xBlockTime = xChain.First().Locktime.FromUnixTimeSeconds() - xChain.Last().Locktime.FromUnixTimeSeconds();
                    if (xBlockTime < blockTime)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"<<< Validator.ForkRule >>>: {ex}");
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public long GetAdjustedTimeAsUnixTimestamp()
        {
            return Helper.Util.GetAdjustedTimeAsUnixTimestamp() & ~StakeTimestampMask;
        }
    }
}
