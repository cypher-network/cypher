﻿// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

using CYPCore.Models;

namespace CYPCore.Services
{
    public interface IMemoryPoolService
    {
        Task<bool> AddMemoryPool(byte[] pool);
        Task AddMemoryPools(byte[] pools);
        Task<byte[]> AddTransaction(TransactionProto tx);
        Task<int> GetTransactionCount();
    }
}