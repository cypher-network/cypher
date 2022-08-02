using System.Linq;

namespace CypherNetwork.Extensions;

public static class DoubleExtensions
{
    public static double FromExponential(this double d, int deci)
    {
        var n = string.Empty;

        d.ToString().Take(deci).ForEach(x => n += x.ToString());
        d = double.Parse($"{n:g}");

        return d;
    }
}