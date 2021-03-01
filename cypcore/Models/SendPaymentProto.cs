// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class SendPaymentProto
    {
        [ProtoMember(1)] public ulong Amount { get; set; }
        [ProtoMember(2)] public string Address { get; set; }
        [ProtoMember(3)] public CredentialsProto Credentials { get; set; }
        [ProtoMember(4)] public ulong Fee { get; set; }
        [ProtoMember(5)] public string Memo { get; set; }
        [ProtoMember(6)] public SessionType SessionType { get; set; }
    }
}
