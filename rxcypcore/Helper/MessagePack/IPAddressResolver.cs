using System;
using System.Collections.Generic;
using System.Net;
using MessagePack;
using MessagePack.Formatters;
using rxcypcore.Serf;
using rxcypcore.Serf.Messages;

namespace rxcypcore.Helper.MessagePack
{
    public sealed class IPAddressResolver : IFormatterResolver
    {
        public static IFormatterResolver Instance = new IPAddressResolver();

        IPAddressResolver()
        {
        }

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

            static FormatterCache() =>
                Formatter = (IMessagePackFormatter<T>)IPAddressResolverFormatter.GetFormatter(typeof(T));
        }
    }

    internal static class IPAddressResolverFormatter
    {
        private static readonly Dictionary<Type, object> FormatterMap = new()
        {
            { typeof(IPAddress), IPAddressFormatter.Instance }
        };

        internal static object GetFormatter(Type t)
        {
            if (FormatterMap.TryGetValue(t, out var formatter))
            {
                return formatter;
            }

            return null;
        }
    }
}