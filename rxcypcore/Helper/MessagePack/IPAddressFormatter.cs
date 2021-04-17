using System.Net;
using MessagePack;
using MessagePack.Formatters;

namespace rxcypcore.Helper.MessagePack
{
    public sealed class IPAddressFormatter : IMessagePackFormatter<IPAddress>
    {
        public static readonly IPAddressFormatter Instance = new();

        public IPAddress Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            var ipaddress = reader.ReadString();
            return IPAddress.Parse(ipaddress);
        }

        public void Serialize(ref MessagePackWriter writer, IPAddress address, MessagePackSerializerOptions options)
        {
            writer.Write(address.ToString());
        }
    }
}