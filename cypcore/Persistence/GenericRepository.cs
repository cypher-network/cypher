// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

using Microsoft.Extensions.Logging;

namespace CYPCore.Persistence
{
    public class GenericRepository<T> : Repository<T>, IGenericRepository<T>
    {
        private string _tableName;

        public string Table => _tableName;

        private readonly IStoredbContext _storedbContext;
        private readonly ILogger _logger;

        public GenericRepository(IStoredbContext storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            _storedbContext = storedbContext;
            _logger = logger;
        }
    }
}
