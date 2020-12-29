// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Collections.Generic;

namespace CYPCore.Models
{
    public class SeedNode
    {
        public IEnumerable<string> Seeds { get; }

        public SeedNode(IEnumerable<string> seeds)
        {
            Seeds = seeds;
        }
    }
}