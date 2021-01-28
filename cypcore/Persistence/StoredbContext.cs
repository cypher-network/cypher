
namespace CYPCore.Persistence
{
    public class StoredbContext : IStoredbContext
    {
        public Storedb Store { get; }

        public StoredbContext(string folder)
        { 
            Store = new Storedb(folder);
            Store.InitAndRecover();
        }
    }
}
