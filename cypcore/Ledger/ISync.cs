//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Threading.Tasks;

namespace CYPCore.Ledger
{
    public interface ISync
    {
        bool SyncRunning { get; }

        Task Check();
        Task Synchronize(Uri uri, long skip, long take);
    }
}