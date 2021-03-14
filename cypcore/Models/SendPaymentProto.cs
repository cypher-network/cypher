// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class SendPaymentProto : object
    {
        [FlatBufferItem(0)] public virtual ulong Amount { get; set; }
        [FlatBufferItem(1)] public virtual string Address { get; set; }
        [FlatBufferItem(2)] public virtual CredentialsProto Credentials { get; set; }
        [FlatBufferItem(3)] public virtual ulong Fee { get; set; }
        [FlatBufferItem(4)] public virtual string Memo { get; set; }
        [FlatBufferItem(5, DefaultValue = SessionType.Coin)] public virtual SessionType SessionType { get; set; }
    }
}
