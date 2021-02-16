
// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.IO;

using LinqDb;

namespace CYPCore.Persistence
{
    public class Storedb : IStoredb, IDisposable
    {
        private bool _disposedValue;

        public Db RockDb { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="folder"></param>
        public Storedb(string folder)
        {
            string dataPath = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory), folder);

            RockDb = new(dataPath);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    RockDb?.Dispose();
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
