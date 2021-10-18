using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CYPCore.Network
{
    /// <summary>
    /// 
    /// </summary>
    public class LeakyBucket
    {
        private readonly BucketConfiguration _bucketConfiguration;
        private readonly ConcurrentQueue<DateTime> _currentItems;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        private Task _leakTask;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bucketConfiguration"></param>
        public LeakyBucket(BucketConfiguration bucketConfiguration)
        {
            _bucketConfiguration = bucketConfiguration;
            _currentItems = new ConcurrentQueue<DateTime>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxWait"></param>
        public async Task Wait(TimeSpan? maxWait = null)
        {
            await _semaphore.WaitAsync(maxWait ?? TimeSpan.FromHours(1));
            try
            {
                _leakTask ??= Task.Factory.StartNew(Leak);

                while (true)
                {
                    if (_currentItems.Count >= _bucketConfiguration.MaxFill)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    _currentItems.Enqueue(DateTime.UtcNow);
                    return;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void Leak()
        {
            while (_currentItems.IsEmpty)
            {
                Thread.Sleep(1000);
            }

            while (true)
            {
                Thread.Sleep(_bucketConfiguration.LeakRateTimeSpan);
                for (var i = 0; i < _currentItems.Count && i < _bucketConfiguration.LeakRate; i++)
                {
                    _currentItems.TryDequeue(out _);
                }
            }
        }
    }
}