// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using Dawn;

namespace CypherNetwork.Extensions;

public static class ExtensionMethods
{
    public static TResult IfNotNull<T, TResult>(this T target, Func<T, TResult> getValue) where T : class
    {
        return target == null ? default : getValue(target);
    }

    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
            action(item);
    }

    public static string FromSecureString(this SecureString secureString)
    {
        var unmanagedString = IntPtr.Zero;
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

    public static ulong MulCoin(this ulong value)
    {
        return value * Ledger.LedgerConstant.Coin;
    }

    public static decimal DivCoin(this ulong value)
    {
        return Convert.ToDecimal(value) / Ledger.LedgerConstant.Coin;;
    }

    public static decimal DivWithAttoTan(this ulong value)
    {
        return Convert.ToDecimal(value) / 1000_000_000_000_000_000;
    }
    
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