
using Microsoft.Extensions.Hosting;

namespace CYPCore.Persistence
{
    public class StoredbContext : IStoredbContext
    {
        public Storedb Store { get; }

        public StoredbContext(IHostApplicationLifetime applicationLifetime, string folder)
        {
            Store = new Storedb(applicationLifetime, folder);
            Store.InitAndRecover();
        }
    }
}
