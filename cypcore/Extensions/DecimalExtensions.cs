using System;
using System.Globalization;
using System.Linq;
using CYPCore.Extentions;
using Dawn;

namespace CYPCore.Extensions
{
    public static class DecimalExtensions
    {
        public static decimal FromExponential(this decimal d, int deci)
        {
            var n = string.Empty;

            d.ToString(CultureInfo.InvariantCulture).Take(deci).ForEach(x => n += x.ToString());
            d = decimal.Parse(string.Format("{0:g}", n));

            return d;
        }

        public static decimal DivWithNaT(this ulong value) => Convert.ToDecimal(value) / 1000_000_000;

        public static ulong ConvertToUInt64(this decimal value)
        {
            Guard.Argument(value, nameof(value)).NotZero().NotNegative();
            var amount = (ulong)(value * 1000_000_000);
            return amount;
        }

    }

}
