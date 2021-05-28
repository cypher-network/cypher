// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class GenericDataList<T>
    {
        [Key(0)] public IList<T> Data { get; set; } = new List<T>();
    }
}