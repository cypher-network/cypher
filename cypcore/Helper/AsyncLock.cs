using System;
using System.Threading;
using System.Threading.Tasks;

namespace CYPCore.Helper
{
    public class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;

        public AsyncLock()
        {
            _semaphoreSlim = new SemaphoreSlim(1, 1);
        }

        public AsyncLock(SemaphoreSlim semaphoreSlim)
        {
            this._semaphoreSlim = semaphoreSlim;
        }

        public async Task<AsyncLock> LockAsync()
        {
            await _semaphoreSlim.WaitAsync();
            return this;
        }

        public void Dispose()
        {
            _semaphoreSlim.Release();
            GC.SuppressFinalize(this);
        }
    }
}
