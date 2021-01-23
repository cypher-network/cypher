// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPNode.Services
{
    public interface ITransactionService
    {
        Task<byte[]> AddTransaction(TransactionProto transaction);
        Task<byte[]> GetTransaction(byte[] txnId);
    }
}
