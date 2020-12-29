// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using ProtoBuf;

namespace CYPCore.Models
{
    [ProtoContract]
    public class CredentialsProto
    {
        [ProtoMember(1)]
        public string Identifier { get; set; }
        [ProtoMember(2)]
        public string Passphrase { get; set; }
    }
}
