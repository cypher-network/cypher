using System;
using System.Threading.Tasks;

namespace CYPCore.Persistence
{
    public interface IStoredb
    {
        Task<Guid> Checkpoint();
        void Dispose();
        bool InitAndRecover();
    }
}