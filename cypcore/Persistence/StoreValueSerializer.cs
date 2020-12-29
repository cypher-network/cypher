using System;

using FASTER.core;

namespace CYPCore.Persistence
{
    public class StoreValueSerializer : BinaryObjectSerializer<StoreValue>
    {
        public override void Deserialize(out StoreValue obj)
        {
            obj = new StoreValue();
            var bytesr = new byte[4];
            reader.Read(bytesr, 0, 4);
            int size = BitConverter.ToInt32(bytesr);
            obj.value = reader.ReadBytes(size);
        }

        public override void Serialize(ref StoreValue obj)
        {
            var len = BitConverter.GetBytes(obj.value.Length);
            writer.Write(len);
            writer.Write(obj.value);
        }
    }
}
