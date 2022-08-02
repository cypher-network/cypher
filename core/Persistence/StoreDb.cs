// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;
using CypherNetwork.Extensions;
using RocksDbSharp;

namespace CypherNetwork.Persistence;

/// <summary>
/// </summary>
public interface IStoreDb
{
    RocksDb Rocks { get; }
}

/// <summary>
/// </summary>
public sealed class StoreDb : IStoreDb, IDisposable
{
    public static readonly StoreDb DataProtectionTable = new(1, "DataProtectionTable");
    public static readonly StoreDb HashChainTable = new(2, "HashChainTable");
    public static readonly StoreDb TransactionOutputTable = new(3, "TransactionOutputTable");

    private readonly string _name;
    private readonly byte[] _nameBytes;
    private readonly int _value;

    private bool _disposedValue;

    private StoreDb(int value, string name)
    {
        _value = value;
        _name = name;
        _nameBytes = name.ToBytes();
    }

    /// <summary>
    /// </summary>
    /// <param name="folder"></param>
    public StoreDb(string folder)
    {
        try
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
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }

    public RocksDb Rocks { get; }

    /// <summary>
    /// </summary>
    /// <param name="table"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public static byte[] Key(string table, byte[] key)
    {
        Span<byte> dbKey = stackalloc byte[key.Length + table.Length];
        for (var i = 0; i < table.Length; i++) dbKey[i] = (byte)table[i];
        key.AsSpan().CopyTo(dbKey[table.Length..]);
        return dbKey.ToArray();
    }

    /// <summary>
    /// </summary>
    /// <param name="blockBasedTableOptions"></param>
    /// <returns></returns>
    private static ColumnFamilies ColumnFamilies(BlockBasedTableOptions blockBasedTableOptions)
    {
        var columnFamilies = new ColumnFamilies
        {
            { "default", new ColumnFamilyOptions().OptimizeForPointLookup(256) },
            { DataProtectionTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions) },
            { HashChainTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions) },
            { TransactionOutputTable.ToString(), ColumnFamilyOptions(blockBasedTableOptions) }
        };
        return columnFamilies;
    }

    /// <summary>
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
            .SetKeepLogFileNum(1)
            .SetDeleteObsoleteFilesPeriodMicros(21600000000)
            .SetManifestPreallocationSize(4194304)
            .SetMaxManifestFileSize(1073741824)
            .SetWalRecoveryMode(Recovery.PointInTime)
            .SetMaxOpenFiles(-1)
            .SetEnableWriteThreadAdaptiveYield(true)
            .SetAllowConcurrentMemtableWrite(true)
            .SetMaxBackgroundCompactions(-1)
            .SetStatsDumpPeriodSec(100)
            .SetParanoidChecks();
        return options;
    }

    /// <summary>
    /// </summary>
    /// <param name="blockBasedTableOptions"></param>
    /// <returns></returns>
    private static ColumnFamilyOptions ColumnFamilyOptions(BlockBasedTableOptions blockBasedTableOptions)
    {
        var columnFamilyOptions = new ColumnFamilyOptions()
            .SetMemtableHugePageSize(2 * 1024 * 1024)
            .SetPrefixExtractor(SliceTransform.CreateFixedPrefix(8))
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
    /// </summary>
    /// <param name="disposing"></param>
    private void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        if (disposing) Rocks?.Dispose();

        _disposedValue = true;
    }

    /// <summary>
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
    public byte[] ToBytes()
    {
        return _nameBytes;
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return _name;
    }
}