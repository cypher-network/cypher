using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
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
                        MutableFraction = 0.3,
                        PageSizeBits = 15,
                        MemorySizeBits = 20
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
            if (!Directory.Exists(_checkpointPath)) return true;

            Database.Recover();
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<Guid> Checkpoint()
        {
            Database.TakeFullCheckpoint(out Guid token);
            await Database.CompleteCheckpointAsync();

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
