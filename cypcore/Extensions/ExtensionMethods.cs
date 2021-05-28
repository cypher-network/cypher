// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using Dawn;

namespace CYPCore.Extensions
{
    public static class ExtensionMethods
    {
        public static TResult IfNotNull<T, TResult>(this T target, Func<T, TResult> getValue) where T : class
        {
            return target == null ? default : getValue(target);
        }

        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (T item in source)
                action(item);
        }

        public static string ToUnSecureString(this SecureString secureString)
        {
            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }

        public static ulong MulWithNanoTan(this ulong value) => value * 1000_000_000;
        public static decimal DivWithNanoTan(this ulong value) => Convert.ToDecimal(value) / 1000_000_000;
        public static decimal DivWithAttoTan(this ulong value) => Convert.ToDecimal(value) / 1000_000_000_000_000_000;

        // public static ulong ConvertToUInt64(this double value)
        // {
        //     ulong amount;
        //
        //     var parts = value.ToString(CultureInfo.CurrentCulture).Split(new char[] { '.', ',' });
        //     var part1 = (ulong)Math.Truncate(value);
        //
        //     if (parts.Length == 1)
        //         amount = part1.MulWithNaT();
        //     else
        //     {
        //         var part2 = (ulong)((value - part1) * ulong.Parse("1".PadRight(parts[1].Length + 1, '0')) + 0.5);
        //         amount = part1.MulWithNaT() + ulong.Parse(part2.ToString());
        //     }
        //
        //     return amount;
        // }

        public static ulong ConvertToUInt64(this decimal value)
        {
            Guard.Argument(value, nameof(value)).NotZero().NotNegative();
            var amount = (ulong)(value * 1000_000_000);
            return amount;
        }

        public static string ShorterString(this string value, int front = 4, int back = 4)
        {
            return $"{value[..front]}...{value.Substring(value.Length - back, back)}";
        }
    }
}
