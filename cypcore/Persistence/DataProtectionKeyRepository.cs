// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Collections.ObjectModel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using CYPCore.Extentions;

using FASTER.core;

using Dawn;

namespace CYPCore.Persistence
{
    public class DataProtectionKeyRepository : IDataProtectionKeyRepository
    {
        private const string DataProtection = "DataProtection";

        private readonly IStoredbContext _storedbContext;
        private readonly ILogger<DataProtectionKeyRepository> _logger;

        public DataProtectionKeyRepository(IStoredbContext storedbContext)
        {
            _storedbContext = storedbContext;
            _logger = NullLogger<DataProtectionKeyRepository>.Instance;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IReadOnlyCollection<XElement> GetAllElements()
        {
            var elements = new List<XElement>();

            try
            {
                var session = _storedbContext.Store.db.NewSession(new StoreFunctions());
                using var scanner = _storedbContext.Store.db.Log.Scan(_storedbContext.Store.db.Log.BeginAddress, _storedbContext.Store.db.Log.TailAddress);

                while (scanner.GetNext(out RecordInfo recordInfo, out StoreKey storeKey, out StoreValue storeValue))
                {                    
                    if (storeKey.tableType == DataProtection)
                    {
                        var xElement = Helper.Util.DeserializeProto<XElement>(storeValue.value);
                        elements.Add(xElement);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< DataProtectionKeyRepository.GetAllElements >>>: {ex}");
            }

            return new ReadOnlyCollection<XElement>(elements);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="element"></param>
        /// <param name="friendlyName"></param>
        public void StoreElement(XElement element, string friendlyName)
        {
            Save(element, friendlyName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="element"></param>
        /// <param name="friendlyName"></param>
        private void Save(XElement element, string friendlyName)
        {
            Guard.Argument(element, nameof(element)).NotNull();
            Guard.Argument(friendlyName, nameof(friendlyName)).NotWhiteSpace().NotNull();

            try
            {
                var session = _storedbContext.Store.db.NewSession(new StoreFunctions());

                var blockKey = new StoreKey { tableType = DataProtection, key = friendlyName.ToBytes() };

                var output = new StoreOutput();
                var block = session.Read(ref blockKey, ref output);

                if (null == output.value.value)
                {
                    var input = new StoreInput
                    {
                        value = Helper.Util.SerializeProto(element)
                    };

                    session.RMW(ref blockKey, ref input);
                }
                else
                {
                    var blockvalue = new StoreValue { value = Helper.Util.SerializeProto(element) };
                    var addStatus = session.Upsert(ref blockKey, ref blockvalue);

                    if (addStatus != Status.OK)
                        throw new Exception();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< DataProtectionKeyRepository.Save >>>: {ex}");
            }
        }
    }
}
