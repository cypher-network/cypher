using System;
using System.Threading;
using CYPCore.Serf.Message;

namespace CYPCore.Serf
{
    sealed class TransactionContext : IDisposable
    {
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public ResponseHeader Header { get; set; }
        public byte[] ResponseBuffer { get; set; }

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                try
                {
                    CancellationTokenSource?.Dispose();
                    Header = default;
                    ResponseBuffer = null;
                }
                catch (NullReferenceException)
                {

                }
            }

            _disposed = true;
        }
    }
}