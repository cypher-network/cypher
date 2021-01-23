using Serilog;

namespace CYPCore.Persistence
{
    public class StoredbContext : IStoredbContext
    {
        public Storedb Store { get; }

        private readonly ILogger _logger;

        public StoredbContext(string folder, ILogger logger)
        {
            _logger = logger;

            Store = new Storedb(folder);
            Store.InitAndRecover();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <param name="entity"></param>
        /// <param name="key"></param>
        /// <param name="tableType"></param>
        /// <returns></returns>
        //public Task<TEntity> SaveOrUpdateAsync<TEntity>(TEntity entity, byte[] key, string tableType)
        //{
        //    try
        //    {
        //        using var session = Store.db.NewSession(new StoreFunctions());

        //        var blockKey = new StoreKey { tableType = tableType, key = key };

        //        var output = new StoreOutput();
        //        var readStatus = session.Read(ref blockKey, ref output);

        //        switch (readStatus)
        //        {
        //            case Status.OK:
        //                {
        //                    var input = new StoreInput
        //                    {
        //                        value = Helper.Util.SerializeProto(entity)
        //                    };

        //                    var rmwStatus = session.RMW(ref blockKey, ref input);
        //                    break;
        //                }

        //            case Status.NOTFOUND:
        //                {
        //                    var blockvalue = new StoreValue { value = Helper.Util.SerializeProto(entity) };
        //                    var addStatus = session.Upsert(ref blockKey, ref blockvalue);

        //                    if (addStatus != Status.OK)
        //                        throw new Exception();
        //                    break;
        //                }
        //        }

        //        var blockvalue = new StoreValue { value = Helper.Util.SerializeProto(entity) };
        //        var storeContext = new StoreContext();

        //        var addStatus = session.Upsert(ref blockKey, ref blockvalue, storeContext, 0);

        //        if (addStatus != Status.OK)
        //            throw new Exception();

        //        session.CompletePending(true);
        //        Store.Checkpoint();
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"<<< StoredbContext.SaveOrUpdate >>>: {ex}");
        //    }

        //    return Task.FromResult(entity);
        //}
    }
}
