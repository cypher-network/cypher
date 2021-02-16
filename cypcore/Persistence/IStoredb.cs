using LinqDb;

namespace CYPCore.Persistence
{
    public interface IStoredb
    {
        Db RockDb { get; }
    }
}