// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using FlatSharp.Attributes;

namespace CYPCore.Models
{
    [FlatBufferTable]
    public class TrieModel
    {
        [FlatBufferItem(0)] public byte[] Key { get; set; }
        [FlatBufferItem(1)] public byte[] Value { get; set; }
    }
}