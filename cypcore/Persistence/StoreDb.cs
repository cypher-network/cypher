// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using RocksDbSharp;

namespace CYPCore.Persistence
{
    public interface IStoreDb
    {
        RocksDb Rocks { get; }
    }

    public sealed class StoreDb : IStoreDb, IDisposable
    {
        private readonly string _name;
        private readonly int _value;

        private bool _disposedValue;
        public RocksDb Rocks { get; }

        public static readonly StoreDb BlockGraphTable = new(1, "BlockGraphTable");
        public static readonly StoreDb DataProtectionTable = new(2, "DataProtectionTable");
        public static readonly StoreDb DeliveredTable = new(3, "DeliveredTable");
        public static readonly StoreDb KeyImageTable = new(4, "KeyImageTable");
        public static readonly StoreDb StagingTable = new(5, "StagingTable");
        public static readonly StoreDb TransactionTable = new(6, "TransactionTable");

        private StoreDb(int value, string name)
        {
            _value = value;
            _name = name;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="folder"></param>
        public StoreDb(string folder)
        {
            var dataPath =
                Path.Combine(
                    Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) ??
                    throw new InvalidOperationException(), folder);

            var blockBasedTableOptions = BlockBasedTableOptions();
            var columnFamilies = ColumnFamilies(blockBasedTableOptions);
            var options = DbOptions();

            Rocks = RocksDb.Open(options, dataPath, columnFamilies);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="table"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static byte[] Key(string table, byte[] key)
        {
            Span<byte> dbKey = stackalloc byte[key.Length + table.Length];
            for (var i = 0; i < table.Length; i++)
            {
                dbKey[i] = (byte)table[i];
            }

            key.AsSpan().CopyTo(dbKey.Slice(table.Length));
            return dbKey.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockBasedTableOptions"></param>
        /// <returns></returns>
        private static ColumnFamilies ColumnFamilies(BlockBasedTableOptions blockBasedTableOptions)
        {
            var columnFamilies = new ColumnFamilies
            {
                {"default", new ColumnFamilyOptions().OptimizeForPointLookup(256)},
                {BlockGraphTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)},
                {DataProtectionTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)},
                {DeliveredTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)},
                {KeyImageTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)},
                {StagingTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)},
                {TransactionTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions)}
            };
            return columnFamilies;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static DbOptions DbOptions()
        {
            var options = new DbOptions()
                .EnableStatistics()
                .SetCreateMissingColumnFamilies()
                .SetCreateIfMissing()
                .SetMaxBackgroundFlushes(2)
                .SetMaxBackgroundCompactions(Environment.ProcessorCount)
                .SetMaxOpenFiles(-1);
            return options;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockBasedTableOptions"></param>
        /// <returns></returns>
        private static ColumnFamilyOptions ColumnFamilyOptions(BlockBasedTableOptions blockBasedTableOptions)
        {
            var columnFamilyOptions = new ColumnFamilyOptions()
                .SetMemtableHugePageSize(2 * 1024 * 1024)
                .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong)8))
                .SetBlockBasedTableFactory(blockBasedTableOptions)
                .SetWriteBufferSize(64 * 1024 * 1024)
                .SetTargetFileSizeBase(64 * 1024 * 1024)
                .SetMaxBytesForLevelBase(512 * 1024 * 1024)
                .SetCompactionStyle(Compaction.Level)
                .SetLevel0FileNumCompactionTrigger(8)
                .SetLevel0SlowdownWritesTrigger(17)
                .SetLevel0StopWritesTrigger(24)
                .SetMaxWriteBufferNumber(3)
                .SetMaxBytesForLevelMultiplier(8)
                .SetNumLevels(4);
            return columnFamilyOptions;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static BlockBasedTableOptions BlockBasedTableOptions()
        {
            var blockBasedTableOptions = new BlockBasedTableOptions()
                .SetFilterPolicy(BloomFilterPolicy.Create(10, false))
                .SetWholeKeyFiltering(false)
                .SetFormatVersion(4)
                .SetIndexType(BlockBasedTableIndexType.Hash)
                .SetBlockSize(16 * 1024)
                .SetCacheIndexAndFilterBlocks(true)
                .SetBlockCache(Cache.CreateLru(32 * 1024 * 1024))
                .SetPinL0FilterAndIndexBlocksInCache(true);
            return blockBasedTableOptions;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        private void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                Rocks?.Dispose();
            }

            _disposedValue = true;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int ToValue()
        {
            return _value;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return _name;
        }
    }
}