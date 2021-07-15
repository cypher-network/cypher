// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blake3;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Helper;
using CYPCore.Models;
using CYPCore.Persistence;
using Dawn;
using Libsecp256k1Zkp.Net;
using libsignal.ecc;
using Serilog;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using Transaction = CYPCore.Models.Transaction;
using Block = CYPCore.Models.Block;
using BlockHeader = CYPCore.Models.BlockHeader;
using Util = CYPCore.Helper.Util;

namespace CYPCore.Ledger
{
    /// <summary>
    /// 
    /// </summary>
    public interface IValidator
    {
        Task<VerifyResult> VerifyBlockGraphSignatureNodeRound(BlockGraph blockGraph);
        VerifyResult VerifyBulletProof(Transaction transaction);
        VerifyResult VerifyCoinbaseTransaction(Vout coinbase, ulong solution, decimal runningDistribution);
        VerifyResult VerifySolution(byte[] vrfBytes, byte[] kernel, ulong solution);
        Task<VerifyResult> VerifyBlock(Block block);
        Task<VerifyResult> VerifyBlocks(Block[] blocks);
        Task<VerifyResult> VerifyTransaction(Transaction transaction);
        Task<VerifyResult> VerifyTransactions(IList<Transaction> transactions);
        VerifyResult VerifySloth(uint t, byte[] message, byte[] nonce);
        uint Difficulty(ulong solution, decimal networkShare);
        ulong Reward(ulong solution, decimal runningDistribution);
        decimal NetworkShare(ulong solution, decimal runningDistribution);
        ulong Solution(byte[] vrfSig, byte[] kernel);
        long GetAdjustedTimeAsUnixTimestamp(uint timeStampMask);
        VerifyResult VerifyLockTime(LockTime target, string script);
        VerifyResult VerifyCommitSum(Transaction transaction);
        Task<VerifyResult> VerifyKeyImage(Transaction transaction);
        Task<VerifyResult> VerifyOutputCommitments(Transaction transaction);
        Task<decimal> CurrentRunningDistribution(ulong solution);
        Task<decimal> GetRunningDistribution();
        VerifyResult VerifyNetworkShare(ulong solution, decimal previousNetworkShare, decimal runningDistributionTotal);
        Task<VerifyResult> BlockExists(Block block);
        byte[] IncrementHasher(byte[] previous, byte[] next);
        Task<VerifyResult> VerifyBlockHash(Block block);
        VerifyResult VerifyVrfProof(byte[] publicKey, byte[] vrfProof, byte[] message, byte[] vrfSig);
        Task<VerifyResult> VerifyMerkel(Block block);
        VerifyResult VerifyTransactionTime(Transaction transaction);
    }

    /// <summary>
    /// 
    /// </summary>
    public class Validator : IValidator
    {
        public static readonly byte[] BlockZeroMerkel =
            "c4cf99ce84c74cdaa353202ab19bc159f2396eb7a128f7758fb432f6092fb126".HexToByte();

        public static readonly byte[] BlockZeroPreHash =
            "3030303030303030437970686572204e6574776f726b2076742e322e32303231".HexToByte();

        private const decimal Distribution = 139_000_000;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISigning _signing;
        private readonly ILogger _logger;

        public Validator(IUnitOfWork unitOfWork, ISigning signing, ILogger logger)
        {
            _unitOfWork = unitOfWork;
            _signing = signing;
            _logger = logger.ForContext("SourceContext", nameof(Validator));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="previous"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public byte[] IncrementHasher(byte[] previous, byte[] next)
        {
            Guard.Argument(previous, nameof(previous)).NotNull().MaxCount(32);
            Guard.Argument(next, nameof(next)).NotNull().MaxCount(32);

            var hasher = Hasher.New();

            hasher.Update(previous);
            hasher.Update(next);

            var hash = hasher.Finalize();
            return hash.AsSpan().ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlockHash(Block block)
        {
            Guard.Argument(block, nameof(block)).NotNull();
            using var hasher = Hasher.New();
            var height = await _unitOfWork.HashChainRepository.GetBlockHeightAsync();
            var prevBlock =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == (ulong)height));
            if (prevBlock == null)
            {
                _logger.Here().Error("No previous block available");
                return VerifyResult.UnableToVerify;
            }

            hasher.Update(prevBlock.Hash);
            hasher.Update(block.ToHash());
            var hash = hasher.Finalize();
            var verifyHasher = hash.HexToByte().Xor(block.Hash);
            return verifyHasher ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyMerkel(Block block)
        {
            Guard.Argument(block, nameof(block)).NotNull();
            var height = await _unitOfWork.HashChainRepository.GetBlockHeightAsync();
            var prevBlock =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == (ulong)height));
            if (prevBlock == null)
            {
                _logger.Here().Error("No previous block available");
                return VerifyResult.UnableToVerify;
            }

            var merkelRoot = BlockHeader.ToMerkelRoot(prevBlock.BlockHeader.MerkleRoot, block.Txs);
            var verifyMerkel = merkelRoot.Xor(block.BlockHeader.MerkleRoot);
            return verifyMerkel ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public async Task<VerifyResult> BlockExists(Block block)
        {
            Guard.Argument(block, nameof(block)).NotNull();
            var seen =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == block.Height));
            return seen != null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
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
        public VerifyResult VerifyBulletProof(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(transaction.Vout, nameof(transaction)).NotNull();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                using var secp256K1 = new Secp256k1();
                using var bulletProof = new BulletProof();
                var index = 1;
                var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
                if (outputs.Contains(CoinType.Coinbase.ToString()) && outputs.Contains(CoinType.Coinstake.ToString()))
                {
                    index++;
                }

                if (transaction.Bp.Select((t, i) => bulletProof.Verify(transaction.Vout[i + index].C, t.Proof, null))
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
        public VerifyResult VerifyCommitSum(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                using var pedersen = new Pedersen();
                var index = 0;
                var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
                if (outputs.Contains(CoinType.Coinbase.ToString()) && outputs.Contains(CoinType.Coinstake.ToString()))
                {
                    index++;
                }

                var payment = transaction.Vout[index].C;
                var change = transaction.Vout[index + 1].C;
                var commitSumBalance = pedersen.CommitSum(new List<byte[]> {payment, change}, new List<byte[]>());
                if (!pedersen.VerifyCommitSum(new List<byte[]> {commitSumBalance}, new List<byte[]> {payment, change}))
                    return VerifyResult.UnableToVerify;
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
        /// <param name="blocks"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlocks(Block[] blocks)
        {
            Guard.Argument(blocks, nameof(blocks)).NotNull();
            foreach (var block in blocks)
            {
                var verifyBlockHeader = await VerifyBlock(block);
                if (verifyBlockHeader == VerifyResult.Succeed) continue;
                _logger.Here().Fatal("Unable to verify the block");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlock(Block block)
        {
            Guard.Argument(block, nameof(block)).NotNull();

            var verifySloth = VerifySloth(block.BlockPos.Bits, block.BlockPos.VrfSig,
                block.BlockPos.Nonce.ToStr().ToBytes());
            if (verifySloth == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the delay function");
                return verifySloth;
            }

            var runningDistribution = await CurrentRunningDistribution(block.BlockPos.Solution);
            var verifyCoinbase = VerifyCoinbaseTransaction(block.Txs.First().Vout.First(),
                block.BlockPos.Solution, runningDistribution);
            if (verifyCoinbase == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the coinbase transaction");
                return verifyCoinbase;
            }

            byte[] hash;
            using (var ts = new TangramStream())
            {
                block.Txs.Skip(1).ForEach(x =>
                {
                    if (block.Height == 0ul)
                    {
                        ts.Append(x.ToStream());
                    }
                    else
                    {
                        var hasAny = x.Validate();
                        if (hasAny.Any())
                        {
                            throw new ArithmeticException("Unable to verify the transaction");
                        }
                        ts.Append(x.ToStream());
                    }
                });
                hash = Hasher.Hash(ts.ToArray()).HexToByte();
            }

            var verifyVrfProof = VerifyVrfProof(block.BlockPos.PublicKey, block.BlockPos.VrfProof, hash, block.BlockPos.VrfSig);
            if (verifyVrfProof == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the Vrf Proof");
                return verifyVrfProof;
            }

            var verifySolution = VerifySolution(block.BlockPos.VrfSig, hash,
                block.BlockPos.Solution);
            if (verifySolution == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the solution");
                return verifySolution;
            }

            var bits = Difficulty(block.BlockPos.Solution,
                block.Txs.First().Vout.First().A.DivWithNanoTan());
            if (block.BlockPos.Bits != bits)
            {
                _logger.Here().Fatal("Unable to verify the bits");
                return VerifyResult.UnableToVerify;
            }

            var verifyLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(block.BlockHeader.Locktime)),
                block.BlockHeader.LocktimeScript);
            if (verifyLockTime == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the block lock time");
                return verifyLockTime;
            }

            if (block.BlockHeader.MerkleRoot.Xor(BlockZeroMerkel) &&
                block.BlockHeader.PrevBlockHash.Xor(BlockZeroPreHash)) return VerifyResult.Succeed;

            var prevBlock = await _unitOfWork.HashChainRepository.GetAsync(x =>
                new ValueTask<bool>(x.Hash.Xor(block.BlockHeader.PrevBlockHash)));
            if (prevBlock == null)
            {
                _logger.Here().Fatal("Unable to find the previous block");
                return VerifyResult.UnableToVerify;
            }

            var verifyPreviousHasher = await VerifyBlockHash(block);
            if (verifyPreviousHasher == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the block hash");
                return verifyPreviousHasher;
            }

            var verifyMerkel = await VerifyMerkel(block);
            if (verifyMerkel == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the merkel tree");
                return verifyMerkel;
            }

            var verifyTransactions = await VerifyTransactions(block.Txs);
            if (verifyTransactions == VerifyResult.Succeed) return VerifyResult.Succeed;
            _logger.Here().Fatal("Unable to verify the block transactions");
            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="transactions"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransactions(IList<Transaction> transactions)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            foreach (var transaction in transactions)
            {
                var verifyTransaction = await VerifyTransaction(transaction);
                if (verifyTransaction != VerifyResult.UnableToVerify) continue;
                _logger.Here().Fatal("Unable to verify the transaction");
                return verifyTransaction;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyTransaction(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
            var verifyTransactionTime = VerifyTransactionTime(transaction);
            if (verifyTransactionTime == VerifyResult.UnableToVerify) return verifyTransactionTime;
            var verifySum = VerifyCommitSum(transaction);
            if (verifySum == VerifyResult.UnableToVerify) return verifySum;
            var verifyBulletProof = VerifyBulletProof(transaction);
            if (verifyBulletProof == VerifyResult.UnableToVerify) return verifyBulletProof;
            var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
            if (outputs.Contains(CoinType.Payment.ToString()) && outputs.Contains(CoinType.Change.ToString()))
            {
                var verifyVOutCommits = await VerifyOutputCommitments(transaction);
                if (verifyVOutCommits == VerifyResult.UnableToVerify) return verifyVOutCommits;
            }
            var verifyKImage = await VerifyKeyImage(transaction);
            if (verifyKImage == VerifyResult.UnableToVerify) return verifyKImage;
            using var mlsag = new MLSAG();
            for (var i = 0; i < transaction.Vin.Length; i++)
            {
                var m = GenerateMLSAG(transaction.Rct[i].M, transaction.Vout, transaction.Vin[i].Key.Offsets, transaction.Mix, 2);
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
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyTransactionTime(Transaction transaction)
        {
            try
            {
                var t = transaction.Vtime.I / 2.7 / 1000;
                if (t < 5) return VerifyResult.UnableToVerify;
                var verifyLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(transaction.Vtime.L)),
                    transaction.Vtime.S);
                if (verifyLockTime == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Fatal("Unable to verify the transaction locktime");
                    return verifyLockTime;
                }

                if (!transaction.ToHash().Xor(transaction.TxnId))
                {
                    _logger.Here().Fatal("Unable to verify the transaction hash for the transaction time");
                    return VerifyResult.UnableToVerify;
                }

                var verifySloth = VerifySloth((uint) transaction.Vtime.I, transaction.Vtime.M,
                    transaction.Vtime.N.ToStr().ToBytes());
                if (verifySloth == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Fatal("Unable to verify the delay function for the transaction time");
                    return verifySloth;
                }
            }
            catch (Exception)
            {
                _logger.Here().Fatal("Unable to verify the transaction time");
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinbase"></param>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public VerifyResult VerifyCoinbaseTransaction(Vout coinbase, ulong solution, decimal runningDistribution)
        {
            Guard.Argument(coinbase, nameof(coinbase)).NotNull();
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotZero().NotNegative();
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
            spending.LockTime = new LockTime(DateTimeOffset.UtcNow);
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
        public async Task<VerifyResult> VerifyKeyImage(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
            foreach (var vin in transaction.Vin)
            {
                var blocks = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(t => t.Vin.First().Key.Image.Xor(vin.Key.Image))));
                if (blocks.Any()) return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyOutputCommitments(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            var offSets = transaction.Vin.Select(v => v.Key).SelectMany(k => k.Offsets.Split(33)).ToArray();
            var list = offSets.Where((value, index) => index % 2 == 0).ToArray();
            foreach (var commit in list)
            {
                var blocks = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(v => v.Vout.Any(c => c.C.Xor(commit)))));
                if (!blocks.Any()) return VerifyResult.UnableToVerify;
                var coinbase = blocks.SelectMany(block => block.Txs).SelectMany(x => x.Vout)
                    .FirstOrDefault(output => output.T == CoinType.Coinbase);
                if (coinbase == null) return VerifyResult.UnableToVerify;
                var verifyCoinbaseLockTime =
                    VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(coinbase.L)), coinbase.S);
                if (verifyCoinbaseLockTime == VerifyResult.UnableToVerify) return verifyCoinbaseLockTime;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="publicKey"></param>
        /// <param name="vrfProof"></param>
        /// <param name="message"></param>
        /// <param name="vrfSig"></param>
        /// <returns></returns>
        public VerifyResult VerifyVrfProof(byte[] publicKey, byte[] vrfProof, byte[] message, byte[] vrfSig)
        {
            Guard.Argument(publicKey, nameof(publicKey)).NotNull().MaxCount(33);
            Guard.Argument(vrfProof, nameof(vrfProof)).NotNull().MaxCount(96);
            Guard.Argument(message, nameof(message)).NotNull().MaxCount(32);
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            var verifyVrfSignature = _signing.VerifyVrfSignature(Curve.decodePoint(publicKey, 0), message, vrfProof);
            return verifyVrfSignature.Xor(vrfSig) ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="message"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public VerifyResult VerifySloth(uint t, byte[] message, byte[] nonce)
        {
            Guard.Argument(t, nameof(t)).NotNegative().NotZero();
            Guard.Argument(message, nameof(message)).NotNull().MaxCount(32);
            Guard.Argument(nonce, nameof(nonce)).NotNull().MaxCount(77);
            var verifySloth = false;
            try
            {
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
                var sloth = new Sloth(ct);
                var x = System.Numerics.BigInteger.Parse(message.ByteToHex(), NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.ToStr());
                if (x.Sign <= 0) x = -x;
                verifySloth = sloth.Verify(t, x, y);
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Unable to verify the delay function");
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
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotZero().NotNegative();
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
                    runningDistributionTotal -= NetworkShare(orderedBlockHeaders.ElementAt(i).BlockPos.Solution,
                        runningDistributionTotal);
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the running distribution");
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
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotZero().NotNegative();
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
            Guard.Argument(previousNetworkShare, nameof(previousNetworkShare)).NotZero().NotNegative();
            Guard.Argument(runningDistributionTotal, nameof(runningDistributionTotal)).NotZero().NotNegative();
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
        public uint Difficulty(ulong solution, decimal networkShare)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            Guard.Argument(networkShare, nameof(networkShare)).NotNegative();
            var diff = Math.Truncate(solution * networkShare / 144);
            diff = diff == 0 ? 1 : diff;
            return (uint)diff;
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

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public long GetAdjustedTimeAsUnixTimestamp(uint timeStampMask)
        {
            return Util.GetAdjustedTimeAsUnixTimestamp() & ~timeStampMask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public async Task<decimal> CurrentRunningDistribution(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotZero().NotNegative();
            var runningDistribution = await GetRunningDistribution();
            var networkShare = NetworkShare(solution, runningDistribution);

            runningDistribution -= networkShare.ConvertToUInt64().DivWithNanoTan();
            return runningDistribution;
        }

        /// <summary>
        /// </summary>
        /// <param name="m"></param>
        /// <param name="outputs"></param>
        /// <param name="keyOffset"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        private byte[] GenerateMLSAG(byte[] m, Vout[] outputs, byte[] keyOffset, int cols, int rows)
        {
            Guard.Argument(m, nameof(m)).NotNull();
            Guard.Argument(outputs, nameof(outputs)).NotNull();
            Guard.Argument(keyOffset, nameof(keyOffset)).NotNull();
            Guard.Argument(cols, nameof(cols)).NotZero().NotNegative();
            Guard.Argument(rows, nameof(rows)).NotZero().NotNegative();
            
            var index = 0;
            var vOutputs = outputs.Select(x => x.T.ToString()).ToArray();
            if (vOutputs.Contains(CoinType.Coinbase.ToString()) && vOutputs.Contains(CoinType.Coinstake.ToString()))
            {
                index++;
            }

            var pcmOut = new Span<byte[]>(new[] {outputs[index].C, outputs[index + 1].C});
            var pcmIn = keyOffset.Split(33).Select(x => x).ToArray().AsSpan();
            using var mlsag = new MLSAG();
            var preparemlsag = mlsag.Prepare(m, null, pcmOut.Length, pcmOut.Length, cols, rows, pcmIn, pcmOut, null);
            if (preparemlsag) return m;
            
            _logger.Here().Fatal("Unable to verify the Multilayered Linkable Spontaneous Anonymous Group membership");
            return null;
        }
    }
}