// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace CYPCore.Extensions
{
    public static class EnumerableExtensions
    {

        public static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> source, int len)
        {
            if (len == 0)
                throw new ArgumentNullException();

            var enumer = source.GetEnumerator();
            while (enumer.MoveNext())
            {
                yield return Take(enumer.Current, enumer, len);
            }
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
            List<T> list = items.ToList();
            list.Add(item);

            return list.Select(i => i);
        }

        public static IEnumerable<T> TryInsert<T>(this IEnumerable<T> items, int index, T item)
        {
            List<T> list = items.ToList();
            list.Insert(index, item);

            return list.Select(i => i);
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>
            (this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            var seenKeys = new HashSet<TKey>();
            foreach (var element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        public static IEnumerable<(T item, int index)> WithIndex<T>(this IEnumerable<T> source)
        {
            return source.Select((item, index) => (item, index));
        }
    }
}
