// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CypherNetwork.Extensions;

public static class ArrayExtensions
{
    public static T[] Reverse<T>(this T[] array)
    {
        var n = array.Length;
        var aux = new T[n];

        for (var i = 0; i < n; i++) aux[n - 1 - i] = array[i];

        return aux;
    }
}