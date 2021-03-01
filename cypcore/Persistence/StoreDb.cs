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

        public static readonly StoreDb DeliveredTable = new(1, "DeliveredTable");
        public static readonly StoreDb MemoryPoolTable = new(2, "MemoryPoolTable");
        public static readonly StoreDb InterpretedTable = new(3, "InterpretedTable");
        public static readonly StoreDb StagingTable = new(4, "StagingTable");
        public static readonly StoreDb SeenBlockHeaderTable = new(5, "SeenBlockHeaderTable");
        public static readonly StoreDb DataProtectionTable = new(6, "DataProtectionTable");
        public static readonly StoreDb TransactionTable = new(7, "TransactionTable");
        public static readonly StoreDb KeyImageTable = new(8, "KeyImageTable");

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
            var options = DbOptions();
            var columnFamilies = ColumnFamilies(blockBasedTableOptions);

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
                {
                    DeliveredTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    DataProtectionTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    MemoryPoolTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    StagingTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    SeenBlockHeaderTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    TransactionTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    KeyImageTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                },
                {
                    InterpretedTable.ToString(), new ColumnFamilyOptions()
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong) 8))
                        .SetBlockBasedTableFactory(blockBasedTableOptions)
                }
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
                .SetMaxBackgroundCompactions(8);
            return options;
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
                .SetCacheIndexAndFilterBlocks(true);
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