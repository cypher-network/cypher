using System;
using System.IO;
using System.Linq;
using FASTER.core;

namespace CYPCore.Persistence
{
    public class Storedb : IStoredb
    {
        private readonly string _checkpointPath;
        private const long LogSize = 1L << 20;

        public FasterKV<StoreKey, StoreValue> Database { get; }
        private readonly IDevice _log;
        private readonly IDevice _objLog;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="folder"></param>
        /// 
        public Storedb(string folder)
        {
            var dataPath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), folder);
            var storehlogFolder = Path.Combine(dataPath, "Store-hlog.log");
            var storehlogObjFolder = Path.Combine(dataPath, "Store-hlog-obj.log");

            _checkpointPath = Path.Combine(dataPath, "checkpoints");

            _log = Devices.CreateLogDevice(storehlogFolder, preallocateFile: false);
            _objLog = Devices.CreateLogDevice(storehlogObjFolder, preallocateFile: false);

            Database = new FasterKV
                <StoreKey, StoreValue>(
                    LogSize,
                    new LogSettings
                    {
                        LogDevice = _log,
                        ObjectLogDevice = _objLog,
                        MutableFraction = 0.9,
                        PageSizeBits = 25,
                        SegmentSizeBits = 30,
                        MemorySizeBits = 34
                    },
                    new CheckpointSettings
                    {
                        CheckpointDir = _checkpointPath,
                        CheckPointType = CheckpointType.FoldOver
                    },
                    new SerializerSettings<StoreKey, StoreValue>
                    {
                        keySerializer = () => new StoreKeySerializer(),
                        valueSerializer = () => new StoreValueSerializer()
                    }
                );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool InitAndRecover()
        {
            if (!Directory.Exists(_checkpointPath)) return false;

            Database.Recover();

            Checkpoint();

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Guid Checkpoint()
        {
            Guid token = default;

            try
            {
                Database.TakeFullCheckpoint(out token);
                Database.CompleteCheckpointAsync().GetAwaiter().GetResult();

                var indexCheckpointDir = new DirectoryInfo($"{_checkpointPath}/cpr-checkpoints");

                int counter = 0;
                foreach (DirectoryInfo info in indexCheckpointDir.GetDirectories().OrderByDescending(f => f.LastWriteTime))
                {
                    if (info.Name == token.ToString())
                        continue;

                    if (++counter < 2)
                        continue;

                    Directory.Delete(info.FullName, true);
                }

                var hlogCheckpointDir = new DirectoryInfo($"{_checkpointPath }/index-checkpoints");

                counter = 0;
                foreach (DirectoryInfo info in hlogCheckpointDir.GetDirectories().OrderByDescending(f => f.LastWriteTime))
                {
                    if (info.Name == token.ToString())
                        continue;

                    if (++counter < 2)
                        continue;

                    Directory.Delete(info.FullName, true);
                }
            }
            catch
            { }

            return token;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Database.Dispose();

            _log.Dispose();
            _objLog.Dispose();
        }
    }
}
