using System;
using System.IO;
using System.Threading.Tasks;
using FASTER.core;

namespace CYPCore.Persistence
{
    public class Storedb : IStoredb
    {
        private readonly string _dataFolder;

        public FasterKV<StoreKey, StoreValue> db;
        public IDevice log;
        public IDevice objLog;

        public Storedb(string folder)
        {
            _dataFolder = folder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool InitAndRecover()
        {
            var logSize = 1L << 20; // 1M cache lines of 64 bytes each = 64MB hash table

            var currentDirectory = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), _dataFolder);

            var storehlogFolder = Path.Combine(currentDirectory, "Store-hlog.log");
            var storehlogObjFolder = Path.Combine(currentDirectory, "Store-hlog-obj.log");

            log = Devices.CreateLogDevice(storehlogFolder, preallocateFile: false);
            objLog = Devices.CreateLogDevice(storehlogObjFolder, preallocateFile: false);

            var checkpointFolder = Path.Combine(currentDirectory, "checkpoints");

            db = new FasterKV
                <StoreKey, StoreValue>(
                    logSize,
                    new LogSettings
                    {
                        LogDevice = log,
                        ObjectLogDevice = objLog,
                        MutableFraction = 0.3,
                        PageSizeBits = 15,
                        MemorySizeBits = 20
                    },
                    new CheckpointSettings
                    {
                        CheckpointDir = checkpointFolder
                    },
                    new SerializerSettings<StoreKey, StoreValue>
                    {
                        keySerializer = () => new StoreKeySerializer(),
                        valueSerializer = () => new StoreValueSerializer()
                    }
                );           

            if (Directory.Exists(checkpointFolder))
            {
                db.Recover();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Guid Checkpoint()
        {
            db.TakeFullCheckpoint(out Guid token);
            db.CompleteCheckpointAsync().GetAwaiter().GetResult();

            return token;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            db.Dispose();
            log.Dispose();
            objLog.Dispose();
        }
    }
}
