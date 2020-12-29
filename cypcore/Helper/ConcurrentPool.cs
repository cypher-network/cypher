using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CYPCore.Ledger;

namespace CYPCore.Helper
{
    public class ConcurrentPool<T> : ConcurrentQueue<T>
    {
        private readonly int _poolSize;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly IData<T> _dataProvider;

        public ConcurrentPool()
        {
        }

        private ConcurrentPool(IEnumerable<T> collection)
            : base(collection)
        {
        }

        public ConcurrentPool(int poolSize, IData<T> dataProvider
            , bool initialFueling = true)
        {
            _poolSize = poolSize;
            _dataProvider = dataProvider;

            if (initialFueling) FillCartridge().Wait();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        public new void Enqueue(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.Enqueue(item);
            _signal.Release();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        private void _Enqueue(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.Enqueue(item);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<T> DequeueAsync()
        {
            await _signal.WaitAsync();

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(TimeSpan timeSpan)
        {
            await _signal.WaitAsync(timeSpan);

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(int milliseconds)
        {
            await _signal.WaitAsync(milliseconds);

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(int milliseconds, CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(milliseconds, cancellationToken);

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeSpan"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(TimeSpan timeSpan, CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(timeSpan, cancellationToken);

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<T> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);

            T result;
            if (Count > 0)
            {
                TryDequeue(out result);
                _signal.Release();
            }
            else
            {
                await FillCartridge();
                if (Count == 0) throw new QueueException();
                TryDequeue(out result);
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task FillCartridge()
        {
            var enumerable = await _dataProvider.GeData(_poolSize - Count);
            foreach (var item in enumerable)
            {
                _Enqueue(item);
            }

            _signal.Release();
        }
    }
}
