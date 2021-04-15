using System.Globalization;
using System.Linq;

namespace CYPCore.Extensions
{
    public static class DecimalExtentions
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
}