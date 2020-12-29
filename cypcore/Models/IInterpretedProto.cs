// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Models
{
    public interface IInterpretedProto
    {
        string Hash { get; set; }
        ulong Node { get; set; }
        ulong Round { get; set; }
        TransactionProto Transaction { get; set; }
        string PublicKey { get; set; }
        string Signature { get; set; }
        string PreviousHash { get; set; }

        string ToString();
        byte[] ToHash();
    }
}