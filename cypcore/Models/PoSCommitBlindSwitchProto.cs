using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class PoSCommitBlindSwitchProto : object
    {
        [FlatBufferItem(0)] public virtual string Balance { get; set; }
        [FlatBufferItem(1)] public virtual string Difficulty { get; set; }
        [FlatBufferItem(2)] public virtual string Difference { get; set; }
    }
}
