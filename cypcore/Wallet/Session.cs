// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Security;
using CYPCore.Models;
using CYPCore.Persistence;

namespace CYPCore.Wallet
{
    /// <summary>
    /// 
    /// </summary>
    public class Session
    {
        public MemStore<Transaction> MemStoreTransactions { get; } = new();
        public Vout Spending { get; set; }
        public SecureString Seed { get; init; }
        public SecureString Passphrase { get; init; }
        public string SenderAddress { get; set; }
        public string RecipientAddress { get; set; }
        public SecureString KeySet { get; set; }
        public ulong Amount { get; set; }
        public ulong Change { get; set; }
        public ulong Reward { get; set; }
    }
}