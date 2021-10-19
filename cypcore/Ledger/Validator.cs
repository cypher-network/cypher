// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blake3;
using CYPCore.Consensus.Models;
using CYPCore.Cryptography;
using CYPCore.Extensions;
using CYPCore.Models;
using CYPCore.Network.Messages;
using CYPCore.Persistence;
using Dawn;
using Libsecp256k1Zkp.Net;
using libsignal.ecc;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using Proto;
using Proto.DependencyInjection;
using Serilog;
using Block = CYPCore.Models.Block;
using BlockHeader = CYPCore.Models.BlockHeader;
using BufferStream = CYPCore.Helper.BufferStream;
using Transaction = CYPCore.Models.Transaction;
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
        Task<ulong> Solution(byte[] vrfBytes, byte[] kernel);
        VerifyResult VerifyKernel(byte[] calculateVrfSig, byte[] kernel);
        long GetAdjustedTimeAsUnixTimestamp(uint timeStampMask);
        VerifyResult VerifyLockTime(LockTime target, string script);
        VerifyResult VerifyCommitSum(Transaction transaction);
        Task<VerifyResult> VerifyKeyImage(Transaction transaction);
        Task<VerifyResult> VerifyOutputCommitments(Transaction transaction);
        Task<decimal> CurrentRunningDistribution(ulong solution);
        Task<decimal> GetRunningDistribution();
        VerifyResult VerifyNetworkShare(ulong solution, decimal previousNetworkShare, decimal runningDistributionTotal);
        Task<VerifyResult> BlockExists(byte[] hash);
        Task<VerifyResult> BlockHeightExists(ulong height);
        byte[] IncrementHasher(byte[] previous, byte[] next);
        Task<VerifyResult> VerifyBlockHash(Block block);
        Task<VerifyResult> VerifyVrfProof(byte[] publicKey, byte[] vrfProof, byte[] message, byte[] vrfSig);
        Task<VerifyResult> VerifyMerkel(Block block);
        VerifyResult VerifyTransactionTime(in Transaction transaction);
        byte[] Kernel(byte[] prevHash, byte[] hash);
        Task<VerifyResult> VerifyForkRule(Block[] xChain);
    }

    /// <summary>
    /// 
    /// </summary>
    public class Validator : IValidator
    {
        public static readonly byte[] BlockZeroMerkel =
            "3d2a28d30ce069ca792c4946030e378857748d9e980ac137d3838f152e36be41".HexToByte();

        public static readonly byte[] BlockZeroPreHash =
            "3030303030303030437970686572204e6574776f726b2076742e322e32303231".HexToByte();

        // Number in seconds
        private const uint SolutionCancellationTimeout = 32;
        private const decimal Distribution = 21_000_000M;
        private const decimal RewardPercentage = 10M;

        private readonly ActorSystem _actorSystem;
        private readonly PID _pidCryptoKeySign;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger _logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="unitOfWork"></param>
        /// <param name="logger"></param>
        public Validator(ActorSystem actorSystem, IUnitOfWork unitOfWork, ILogger logger)
        {
            _actorSystem = actorSystem;
            _pidCryptoKeySign = actorSystem.Root.Spawn(actorSystem.DI().PropsFor<CryptoKeySign>());
            _unitOfWork = unitOfWork;
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
            if (prevBlock is null)
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
            if (prevBlock is null)
            {
                _logger.Here().Error("No previous block available");
                return VerifyResult.UnableToVerify;
            }

            var merkelRoot = BlockHeader.ToMerkelRoot(prevBlock.BlockHeader.MerkleRoot, block.Txs.ToImmutableArray());
            var verifyMerkel = merkelRoot.Xor(block.BlockHeader.MerkleRoot);
            return verifyMerkel ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="height"></param>
        /// <returns></returns>
        public async Task<VerifyResult> BlockHeightExists(ulong height)
        {
            Guard.Argument(height, nameof(height)).NotNegative();
            var seen =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Height == height));
            return seen is not null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task<VerifyResult> BlockExists(byte[] hash)
        {
            Guard.Argument(hash, nameof(hash)).NotEmpty().NotEmpty().MaxCount(64);
            var seen =
                await _unitOfWork.HashChainRepository.GetAsync(x => new ValueTask<bool>(x.Hash.Xor(hash)));
            return seen is not null ? VerifyResult.AlreadyExists : VerifyResult.Succeed;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraph"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyBlockGraphSignatureNodeRound(BlockGraph blockGraph)
        {
            Guard.Argument(blockGraph, nameof(blockGraph)).NotNull();
            try
            {
                var verifySignatureManualResponse = await _actorSystem.Root.RequestAsync<VerifySignatureManualResponse>(
                    _pidCryptoKeySign,
                    new VerifySignatureManualRequest(blockGraph.Signature, blockGraph.PublicKey,
                        blockGraph.ToHash()));
                if (!verifySignatureManualResponse.Ok)
                {
                    _logger.Here().Error("Unable to verify the signature for block {@Round} from node {@Node}",
                        blockGraph.Block.Round, blockGraph.Block.Node);
                    return VerifyResult.UnableToVerify;
                }

                if (blockGraph.Prev is { } && blockGraph.Prev?.Round != 0)
                {
                    if (blockGraph.Prev.Node != blockGraph.Block.Node)
                    {
                        _logger.Here().Error("Previous block node does not match block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                        return VerifyResult.UnableToVerify;
                    }

                    if (blockGraph.Prev.Round + 1 != blockGraph.Block.Round)
                    {
                        _logger.Here().Error("Previous block round is invalid on block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                        return VerifyResult.UnableToVerify;
                    }
                }

                var node = blockGraph.Block.Node;
                if (blockGraph.Deps.Any(dep => dep.Block.Node == node))
                {
                    _logger.Here()
                        .Error(
                            "Block references includes a block from the same node in block {@Round} from node {@Node}",
                            blockGraph.Block.Round, blockGraph.Block.Node);
                    return VerifyResult.UnableToVerify;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to verify block graph signature");
                return VerifyResult.UnableToVerify;
            }

            return VerifyResult.Succeed;
        }

        /// <summary>
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyBulletProof(Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(transaction.Vout, nameof(transaction.Vout)).NotNull().NotEmpty();
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

                if (transaction.Bp.Select((t, i) => bulletProof.Verify(transaction.Vout[i + index].C, t.Proof, null!))
                    .Any(verified => !verified))
                {
                    _logger.Here().Fatal("Unable to verify the bullet proof");
                    return VerifyResult.UnableToVerify;
                }
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, ex.Message);
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
            Guard.Argument(transaction.Vout, nameof(transaction.Vout)).NotNull().NotEmpty();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                using var pedersen = new Pedersen();
                var index = 0;
                var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
                if (outputs.Contains(CoinType.Coinbase.ToString()) && outputs.Contains(CoinType.Coinstake.ToString()))
                {
                    if (transaction.Vout[index].D is { })
                    {
                        var reward = transaction.Vout[index].A;
                        var coinbase = transaction.Vout[index].C;
                        var blind = transaction.Vout[index].D;
                        var commit = pedersen.Commit(reward, blind);
                        if (!commit.Xor(coinbase))
                        {
                            _logger.Here().Fatal("Unable to verify coinbase commitment");
                            return VerifyResult.UnableToVerify;
                        }

                        index++;
                        var payout = transaction.Vout[index].A;
                        var coinstake = transaction.Vout[index].C;
                        blind = transaction.Vout[index].D;
                        commit = pedersen.Commit(payout, blind);
                        if (!commit.Xor(coinstake))
                        {
                            _logger.Here().Fatal("Unable to verify coinstake commitment");
                            return VerifyResult.UnableToVerify;
                        }
                    }
                    else
                    {
                        index++;
                    }
                }

                var payment = transaction.Vout[index].C;
                var change = transaction.Vout[index + 1].C;
                var commitSumBalance = pedersen.CommitSum(new List<byte[]> { payment, change }, new List<byte[]>());
                if (!pedersen.VerifyCommitSum(new List<byte[]> { commitSumBalance },
                    new List<byte[]> { payment, change }))
                {
                    _logger.Here().Fatal("Unable to verify committed sum");
                    return VerifyResult.UnableToVerify;
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
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(96);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            var isSolution = false;
            try
            {
                var target = new BigInteger(1, Hasher.Hash(vrfBytes).HexToByte());
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
            Guard.Argument(blocks, nameof(blocks)).NotNull().NotEmpty();
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
                block.BlockPos.Nonce.FromBytes().ToBytes());
            if (verifySloth == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the delay function");
                return verifySloth;
            }

            var runningDistribution = await CurrentRunningDistribution(block.BlockPos.Solution);
            var verifyCoinbase = VerifyCoinbaseTransaction(block.Txs.First().Vout.First(), block.BlockPos.Solution,
                runningDistribution);
            if (verifyCoinbase == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the coinbase transaction");
                return verifyCoinbase;
            }

            byte[] hash;
            using (var ts = new BufferStream())
            {
                block.Txs.Skip(1).ForEach(x =>
                {
                    var hasAny = x.Validate();
                    if (hasAny.Any())
                    {
                        throw new ArithmeticException("Unable to verify the transaction");
                    }

                    // ReSharper disable once AccessToDisposedClosure
                    ts.Append(x.ToStream());
                });
                hash = Hasher.Hash(ts.ToArray()).HexToByte();
            }

            var kernel = Kernel(block.BlockHeader.PrevBlockHash, hash);
            var verifyKernel = VerifyKernel(block.BlockPos.VrfProof, kernel);
            if (verifyKernel == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify kernel");
                return verifyKernel;
            }

            var verifyVrfProof = await VerifyVrfProof(block.BlockPos.PublicKey, block.BlockPos.VrfProof, kernel,
                block.BlockPos.VrfSig);
            if (verifyVrfProof == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the Vrf Proof");
                return verifyVrfProof;
            }

            var verifySolution = VerifySolution(block.BlockPos.VrfProof, kernel, block.BlockPos.Solution);
            if (verifySolution == VerifyResult.UnableToVerify)
            {
                _logger.Here().Fatal("Unable to verify the solution");
                return verifySolution;
            }

            var bits = Difficulty(block.BlockPos.Solution, block.Txs.First().Vout.First().A.DivWithNanoTan());
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
            if (prevBlock is null)
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
            Guard.Argument(transactions, nameof(transactions)).NotNull().NotEmpty();
            foreach (var transaction in transactions)
            {
                var verifyTransaction = await VerifyTransaction(transaction);
                if (verifyTransaction == VerifyResult.Succeed) continue;
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
            if (transaction.Validate().Any())
            {
                _logger.Here().Fatal("Unable to validate transaction");
                return VerifyResult.UnableToVerify;
            }

            var outputs = transaction.Vout.Select(x => x.T.ToString()).ToArray();
            if (outputs.Contains(CoinType.Payment.ToString()) && outputs.Contains(CoinType.Change.ToString()))
            {
                var verifyTransactionTime = VerifyTransactionTime(in transaction);
                if (verifyTransactionTime != VerifyResult.Succeed) return verifyTransactionTime;
            }

            var verifyVOutCommits = await VerifyOutputCommitments(transaction);
            if (verifyVOutCommits != VerifyResult.Succeed) return verifyVOutCommits;
            var verifyKImage = await VerifyKeyImage(transaction);
            if (verifyKImage != VerifyResult.Succeed) return verifyKImage;
            var verifySum = VerifyCommitSum(transaction);
            if (verifySum == VerifyResult.UnableToVerify) return verifySum;
            var verifyBulletProof = VerifyBulletProof(transaction);
            if (verifyBulletProof == VerifyResult.UnableToVerify) return verifyBulletProof;
            using var mlsag = new MLSAG();
            for (var i = 0; i < transaction.Vin.Length; i++)
            {
                var m = GenerateMlsag(transaction.Rct[i].M, transaction.Vout, transaction.Vin[i].Key.Offsets,
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
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public VerifyResult VerifyTransactionTime(in Transaction transaction)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            try
            {
                if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
                var t = transaction.Vtime.I / 2.7 / 1000;
                if (t < MemoryPool.TransactionDefaultTimeDelayFromSeconds) return VerifyResult.UnableToVerify;
                var verifyLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(transaction.Vtime.L)),
                    transaction.Vtime.S);
                if (verifyLockTime == VerifyResult.UnableToVerify)
                {
                    _logger.Here().Fatal("Unable to verify the transaction lock time");
                    return verifyLockTime;
                }

                var verifySloth = VerifySloth((uint)transaction.Vtime.I, transaction.Vtime.M,
                    transaction.Vtime.N.FromBytes().ToBytes());
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
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative().NotZero();
            if (coinbase.Validate().Any()) return VerifyResult.UnableToVerify;
            if (coinbase.T != CoinType.Coinbase) return VerifyResult.UnableToVerify;
            var verifyNetworkShare = VerifyNetworkShare(solution, coinbase.A.DivWithNanoTan(), runningDistribution);
            if (verifyNetworkShare == VerifyResult.UnableToVerify) return verifyNetworkShare;
            using var pedersen = new Pedersen();
            var commitSum = pedersen.CommitSum(new List<byte[]> { coinbase.C }, new List<byte[]> { coinbase.C });
            return commitSum is null ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
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
                var block = await _unitOfWork.HashChainRepository.GetAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(c => c.Vin[0].Key.Image.Xor(vin.Key.Image))));
                if (block is null) continue;
                _logger.Here().Fatal("Unable to verify. Key Image Already Exists");
                return VerifyResult.KeyImageAlreadyExists;
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
            if (transaction.Validate().Any()) return VerifyResult.UnableToVerify;
            var offSets = transaction.Vin.Select(v => v.Key).SelectMany(k => k.Offsets.Split(33)).ToArray();
            foreach (var commit in offSets)
            {
                var blocks = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Txs.Any(v => v.Vout.Any(c => c.C.Xor(commit)))));
                if (!blocks.Any())
                {
                    _logger.Here().Fatal("Unable to find commitment {@Commit}", commit.ByteToHex());
                    return VerifyResult.CommitmentNotFound;
                }

                var coinbase = blocks.SelectMany(block => block.Txs).SelectMany(x => x.Vout)
                    .FirstOrDefault(output => output.C.Xor(commit) && output.T == CoinType.Coinbase);
                if (coinbase is null) continue;
                var verifyCoinbaseLockTime = VerifyLockTime(new LockTime(Utils.UnixTimeToDateTime(coinbase.L)),
                    coinbase.S);
                if (verifyCoinbaseLockTime != VerifyResult.UnableToVerify) continue;
                _logger.Here().Fatal("Unable to verify coinbase commitment lock time {@Commit}", commit.ByteToHex());
                return verifyCoinbaseLockTime;
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
        public async Task<VerifyResult> VerifyVrfProof(byte[] publicKey, byte[] vrfProof, byte[] message, byte[] vrfSig)
        {
            Guard.Argument(publicKey, nameof(publicKey)).NotNull().MaxCount(33);
            Guard.Argument(vrfProof, nameof(vrfProof)).NotNull().MaxCount(96);
            Guard.Argument(message, nameof(message)).NotNull().MaxCount(32);
            Guard.Argument(vrfSig, nameof(vrfSig)).NotNull().MaxCount(32);
            try
            {
                var verifyVrfSignatureResponse = await _actorSystem.Root.RequestAsync<VerifyVrfSignatureResponse>(
                    _pidCryptoKeySign,
                    new VerifyVrfSignatureRequest(Curve.decodePoint(publicKey, 0), vrfProof, message));
                return verifyVrfSignatureResponse.Signature.Xor(vrfSig)
                    ? VerifyResult.Succeed
                    : VerifyResult.UnableToVerify;
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Unable to verify Vrf signature");
            }

            return VerifyResult.UnableToVerify;
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
            try
            {
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(1)).Token;
                var sloth = new Sloth(ct);
                var x = System.Numerics.BigInteger.Parse(message.ByteToHex(), NumberStyles.AllowHexSpecifier);
                var y = System.Numerics.BigInteger.Parse(nonce.FromBytes());
                if (x.Sign <= 0) x = -x;
                var verifySloth = sloth.Verify(t, x, y);
                return verifySloth ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Unable to verify the delay function");
            }

            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public ulong Reward(ulong solution, decimal runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative().NotZero();
            var networkShare = NetworkShare(solution, runningDistribution);
            return networkShare.ConvertToUInt64();
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<decimal> GetRunningDistribution()
        {
            try
            {
                var runningDistributionTotal = Distribution;
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

                return runningDistributionTotal;
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "Unable to get the running distribution");
            }

            return 0;
        }

        /// <summary>
        /// </summary>
        /// <param name="solution"></param>
        /// <param name="runningDistribution"></param>
        /// <returns></returns>
        public decimal NetworkShare(ulong solution, decimal runningDistribution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            Guard.Argument(runningDistribution, nameof(runningDistribution)).NotNegative().NotZero();
            var r = Distribution - runningDistribution;
            var percentage = r / runningDistribution == 0 ? RewardPercentage : r / runningDistribution;
            if (percentage != RewardPercentage)
                percentage += percentage * Convert.ToDecimal("1".PadRight(percentage.LeadingZeros(), '0'));
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
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            Guard.Argument(previousNetworkShare, nameof(previousNetworkShare)).NotNegative().NotZero();
            Guard.Argument(runningDistributionTotal, nameof(runningDistributionTotal)).NotNegative().NotZero();
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
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            Guard.Argument(networkShare, nameof(networkShare)).NotNegative();
            var diff = Math.Truncate(solution * networkShare / 8192);
            diff = diff == 0 ? 1 : diff;
            return (uint)diff;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="vrfBytes"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        public async Task<ulong> Solution(byte[] vrfBytes, byte[] kernel)
        {
            Guard.Argument(vrfBytes, nameof(vrfBytes)).NotNull().MaxCount(96);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);
            var tcs = new TaskCompletionSource<ulong>();
            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(SolutionCancellationTimeout)).Token;
            await Task.Factory.StartNew(() =>
            {
                try
                {
                    long itr = 0;
                    var calculating = true;
                    var target = new BigInteger(1, Hasher.Hash(vrfBytes).HexToByte());
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

                    tcs.SetResult((ulong)itr);
                }
                catch (Exception ex)
                {
                    _logger.Here().Fatal(ex, "Unable to calculate solution");
                    tcs.SetResult(0);
                }
            }, ct);
            return tcs.Task.Result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="calculateVrfSig"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        public VerifyResult VerifyKernel(byte[] calculateVrfSig, byte[] kernel)
        {
            Guard.Argument(calculateVrfSig, nameof(calculateVrfSig)).NotNull().MaxCount(96);
            Guard.Argument(kernel, nameof(kernel)).NotNull().MaxCount(32);
            var v = new BigInteger(Hasher.Hash(calculateVrfSig).HexToByte());
            var T = new BigInteger(kernel);
            return v.CompareTo(T) <= 0 ? VerifyResult.Succeed : VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public long GetAdjustedTimeAsUnixTimestamp(uint timeStampMask) =>
            Util.GetAdjustedTimeAsUnixTimestamp() & ~timeStampMask;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public async Task<decimal> CurrentRunningDistribution(ulong solution)
        {
            Guard.Argument(solution, nameof(solution)).NotNegative().NotZero();
            var runningDistribution = await GetRunningDistribution();
            if (runningDistribution == Distribution) runningDistribution -= NetworkShare(solution, runningDistribution);
            var networkShare = NetworkShare(solution, runningDistribution);
            runningDistribution -= networkShare.ConvertToUInt64().DivWithNanoTan();
            return runningDistribution;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prevHash"></param>
        /// <param name="hash"></param>
        /// <returns></returns>
        public byte[] Kernel(byte[] prevHash, byte[] hash)
        {
            Guard.Argument(prevHash, nameof(prevHash)).NotNull().MaxCount(32);
            Guard.Argument(hash, nameof(hash)).NotNull().MaxCount(32);
            var txHashBig = new BigInteger(1, hash).Multiply(new BigInteger(Hasher.Hash(prevHash).HexToByte()));
            var kernel = Hasher.Hash(txHashBig.ToBytes()).HexToByte();
            return kernel;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="otherChain"></param>
        /// <returns></returns>
        public async Task<VerifyResult> VerifyForkRule(Block[] otherChain)
        {
            Guard.Argument(otherChain, nameof(otherChain)).NotNull().NotEmpty();
            try
            {
                var currentChain = await _unitOfWork.HashChainRepository.WhereAsync(x =>
                    new ValueTask<bool>(x.Height <= (ulong)otherChain.Length));
                var currentChainSolution = currentChain.OrderBy(x => x.Height)
                    .Aggregate(0UL, (ul, b) => ul + b.BlockPos.Solution);
                var otherChainSolution = otherChain.Where(x => x.Height <= (ulong)currentChain.Count - 1)
                    .OrderBy(x => x.Height).Aggregate(0UL, (ul, b) => ul + b.BlockPos.Solution);
                if (otherChainSolution <= currentChainSolution) return VerifyResult.Succeed;
            }
            catch (Exception ex)
            {
                _logger.Here().Fatal(ex, "Error while processing fork rule");
            }

            return VerifyResult.UnableToVerify;
        }

        /// <summary>
        /// </summary>
        /// <param name="m"></param>
        /// <param name="outputs"></param>
        /// <param name="keyOffset"></param>
        /// <param name="cols"></param>
        /// <param name="rows"></param>
        /// <returns></returns>
        private byte[] GenerateMlsag(byte[] m, Vout[] outputs, byte[] keyOffset, int cols, int rows)
        {
            Guard.Argument(m, nameof(m)).NotNull();
            Guard.Argument(outputs, nameof(outputs)).NotNull().NotEmpty();
            Guard.Argument(keyOffset, nameof(keyOffset)).NotNull().NotEmpty();
            Guard.Argument(cols, nameof(cols)).NotNegative().NotZero();
            Guard.Argument(rows, nameof(rows)).NotNegative().NotZero();

            var index = 0;
            var vOutputs = outputs.Select(x => x.T.ToString()).ToArray();
            if (vOutputs.Contains(CoinType.Coinbase.ToString()) && vOutputs.Contains(CoinType.Coinstake.ToString()))
            {
                index++;
            }

            var pcmOut = new Span<byte[]>(new[] { outputs[index].C, outputs[index + 1].C });
            var pcmIn = keyOffset.Split(33).Select(x => x).ToArray().AsSpan();
            using var mlsag = new MLSAG();
            var preparemlsag = mlsag.Prepare(m, null, pcmOut.Length, pcmOut.Length, cols, rows, pcmIn, pcmOut, null);
            if (preparemlsag) return m;

            _logger.Here().Fatal("Unable to verify the Multilayered Linkable Spontaneous Anonymous Group membership");
            return null;
        }
    }
}