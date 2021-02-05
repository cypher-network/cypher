using System.Linq;
using CYPCore.Extentions;
using FASTER.core;

namespace CYPCore.Persistence
{
    public class StoreKey : IFasterEqualityComparer<StoreKey>
    {
        public byte[] key;
        public string tableType;

        public virtual long GetHashCode64(ref StoreKey key)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(key.tableType);
            byte[] b = bytes.ToArray().Concat(key.key).ToArray();

            var hash256 = Helper.Util.SHA384ManagedHash(b);

            long res = 0;
            foreach (byte bt in hash256)
                res = res * 31 * 31 * bt + 17;

            return res;
        }

        public virtual bool Equals(ref StoreKey k1, ref StoreKey k2)
        {
            return k1.key.Xor(k2.key) && k1.tableType == k2.tableType;
        }
    }
}
