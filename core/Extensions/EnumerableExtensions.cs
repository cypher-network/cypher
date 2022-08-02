// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace CypherNetwork.Extensions;

public static class EnumerableExtensions
{
    public static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> source, int len)
    {
        if (len == 0)
            throw new ArgumentNullException();

        var enumer = source.GetEnumerator();
        while (enumer.MoveNext()) yield return Take(enumer.Current, enumer, len);
    }

    private static IEnumerable<T> Take<T>(T head, IEnumerator<T> tail, int len)
    {
        while (true)
        {
            yield return head;
            if (--len == 0)
                break;
            if (tail.MoveNext())
                head = tail.Current;
            else
                break;
        }
    }

    public static IEnumerable<T> TryAdd<T>(this IEnumerable<T> items, T item)
    {
        var list = items.ToList();
        list.Add(item);

        return list.Select(i => i);
    }

    public static IEnumerable<T> TryInsert<T>(this IEnumerable<T> items, int index, T item)
    {
        var list = items.ToList();
        list.Insert(index, item);

        return list.Select(i => i);
    }

    /// <summary>
    ///     ∞
    /// </summary>
    /// <param name="source"></param>
    /// <param name="count"></param>
    /// <typeparam name="TSource"></typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static TSource[] ToArray<TSource>(this IEnumerable<TSource> source, int count)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var array = new TSource[count];
        var i = 0;
        foreach (var item in source) array[i++] = item;
        return array;
    }

    public static IEnumerable<(T item, int index)> WithIndex<T>(this IEnumerable<T> source)
    {
        return source.Select((item, index) => (item, index));
    }
}