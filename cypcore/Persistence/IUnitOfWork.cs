// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using Microsoft.AspNetCore.DataProtection.Repositories;

namespace CYPCore.Persistence
{
    public interface IUnitOfWork
    {
        IStoredb Storedb { get; }
        IXmlRepository DataProtectionKeys { get; }
        IDataProtectionPayloadRepository DataProtectionPayload { get; }
        IInterpretedRepository InterpretedRepository { get; }
        IMemPoolRepository MemPoolRepository { get; }
        IStagingRepository StagingRepository { get; }
        IDeliveredRepository DeliveredRepository { get; }
        ISeenBlockHeaderRepository SeenBlockHeaderRepository { get; }
    }
}
