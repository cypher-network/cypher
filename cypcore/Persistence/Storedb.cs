using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FASTER.core;
using Microsoft.Extensions.Hosting;

namespace CYPCore.Persistence
{
    public class Storedb : IStoredb
    {
        private readonly string _checkpointPath;
        private const long LogSize = 1L << 20;

        public FasterKV<StoreKey, StoreValue> Database { get; }
        private readonly IDevice _log;
        private readonly IDevice _objLog;
        private readonly DeviceLogCommitCheckpointManager _checkpointManager;
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _autoCheckPoint;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="folder"></param>
        /// 
        public Storedb(IHostApplicationLifetime applicationLifetime, string folder)
        {
            var dataPath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), folder);
            var storehlogFolder = Path.Combine(dataPath, "Store-hlog.log");
            var storehlogObjFolder = Path.Combine(dataPath, "Store-hlog-obj.log");

            _checkpointPath = Path.Combine(dataPath, "checkpoints");
            _checkpointManager = new DeviceLogCommitCheckpointManager(new LocalStorageNamedDeviceFactory(), new DefaultCheckpointNamingScheme(_checkpointPath));

            _log = Devices.CreateLogDevice(storehlogFolder, preallocateFile: false);
            _objLog = Devices.CreateLogDevice(storehlogObjFolder, preallocateFile: false);

            Database = new FasterKV
                <StoreKey, StoreValue>(
                    LogSize,
                    new LogSettings
                    {
                        LogDevice = _log,
                        ObjectLogDevice = _objLog,
                    },
                    new CheckpointSettings
                    {
                        CheckpointManager = _checkpointManager,
                        CheckPointType = CheckpointType.FoldOver
                    },
                    new SerializerSettings<StoreKey, StoreValue>
                    {
                        keySerializer = () => new StoreKeySerializer(),
                        valueSerializer = () => new StoreValueSerializer()
                    }
                );

            _autoCheckPoint = new Thread(() => AutoCheckpointing(Database, _checkpointManager, _cts.Token));
            _autoCheckPoint.Start();

            applicationLifetime.ApplicationStopping.Register(() =>
            {
                try
                {
                    _cts.Cancel();
                }
                catch (Exception)
                {

                }
            });
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
        /// <param name="fasterKV"></param>
        /// <param name="checkpointManager"></param>
        /// <param name="cancellationToken"></param>
        private static void AutoCheckpointing(FasterKV<StoreKey, StoreValue> fasterKV, DeviceLogCommitCheckpointManager checkpointManager, CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Thread.Sleep(1200000);

                    checkpointManager.PurgeAll();
                    _ = fasterKV.TakeFullCheckpointAsync(CheckpointType.FoldOver, cancellationToken).GetAwaiter().GetResult();
                }
            }
            catch (TaskCanceledException)
            { }
            catch (Exception)
            { }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Database.Dispose();

            _log.Dispose();
            _objLog.Dispose();
            _cts.Dispose();
        }
    }
}
