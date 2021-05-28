// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;
using MessagePack;

namespace CYPCore.Models
{
    [MessagePackObject]
    public class Dep : object
    {
        [Key(0)] public Interpreted Block { get; set; }
        [Key(1)] public IList<Interpreted> Deps { get; set; } = new List<Interpreted>();
        [Key(2)] public Interpreted Prev { get; set; }
    }
}
