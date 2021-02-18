
using RocksDbSharp;

namespace CYPCore.Persistence
{
    public interface IStoreDb
    {
        RocksDb Rocks { get; }
    }
}