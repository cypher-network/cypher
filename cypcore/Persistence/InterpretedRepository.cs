// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Dawn;

using CYPCore.Models;
using System.Linq;


namespace CYPCore.Persistence
{
    public class InterpretedRepository : Repository<InterpretedProto>, IInterpretedRepository
    {
        private const string TableInterpreted = "Interpreted";

        public string Table => TableInterpreted;

        private readonly IStoredbContext _storedbContext;
        private readonly ILogger _logger;

        public InterpretedRepository(IStoredbContext storedbContext, ILogger logger)
            : base(storedbContext, logger)
        {
            _storedbContext = storedbContext;
            _logger = logger;

            SetTableType(TableInterpreted);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <returns></returns>
        public Task<IEnumerable<InterpretedProto>> RangeAsync(int skip, int take)
        {
            Guard.Argument(skip, nameof(skip)).NotNegative();
            Guard.Argument(take, nameof(take)).NotNegative();

            var blocks = Enumerable.Empty<InterpretedProto>();

            try
            {
                using var iterateAsync = CreateIterateAsync();
                blocks = iterateAsync.Iterate().Skip(skip).Take(take).ToEnumerable();
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< BlockIDRepository.GetRange >>>: {ex}");
            }

            return Task.FromResult(blocks);
        }
    }
}
