using System.Threading.Tasks;

namespace CYPCore.Persistence
{
    public interface IStoredbContext
    {
        Storedb Store { get; }
        // Task<TEntity> SaveOrUpdateAsync<TEntity>(TEntity entity, byte[] key, string tableType);
    }
}