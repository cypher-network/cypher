using System.Globalization;
using System.Linq;

namespace CypherNetwork.Extensions;

public static class DecimalExtensions
{
    public static int LeadingZeros(this decimal value)
    {
        var zeroCount = value.ToString(CultureInfo.CurrentCulture)
            .Replace('.', ' ')
            .Replace(" ", string.Empty)
            .TakeWhile(c => c == '0')
            .Count();

        return zeroCount;
    }
}