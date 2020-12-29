using System;

using FASTER.core;

namespace CYPCore.Persistence
{
    public class StoreKeySerializer : BinaryObjectSerializer<StoreKey>
    {
        public override void Deserialize(out StoreKey obj)
        {
            obj = new StoreKey();
            var bytesr = new byte[4];
            reader.Read(bytesr, 0, 4);
            var sizet = BitConverter.ToInt32(bytesr);
            var bytes = new byte[sizet];
            reader.Read(bytes, 0, sizet);
            obj.tableType = System.Text.Encoding.UTF8.GetString(bytes);

            bytesr = new byte[4];
            reader.Read(bytesr, 0, 4);
            var size = BitConverter.ToInt32(bytesr);
            obj.key = new byte[size];
            reader.Read(obj.key, 0, size);
        }

        public override void Serialize(ref StoreKey obj)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(obj.tableType);
            var len = BitConverter.GetBytes(bytes.Length);
            writer.Write(len);
            writer.Write(bytes);

            len = BitConverter.GetBytes(obj.key.Length);
            writer.Write(len);
            writer.Write(obj.key);
        }
    }
}
