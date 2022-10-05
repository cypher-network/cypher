using System;
using System.Collections.Generic;
using System.Net;

namespace CypherNetworkNode.Configuration
{
    public class IPService
    {
        public IPService(string name, Uri uri)
        {
            Name = name;
            Uri = uri;
        }

        public readonly string Name;
        public readonly Uri Uri;

        public override string ToString()
        {
            return $"{Name} ({Uri})";
        }

        public IPAddress Read()
        {
            using var client = new WebClient();
            var response = client.DownloadString(Uri);
            return IPAddress.Parse(response);
        }
    }

    public class IPServices
    {
        public static IList<IPService> Services { get; } = new List<IPService>()
        {
            new("ident.me", new Uri("https://v4.ident.me")),
            new("ipify.org", new Uri("https://api.ipify.org")),
            new("my-ip.io", new Uri("https://api4.my-ip.io/ip.txt")),
            new("seeip.org", new Uri("https://ip4.seeip.org"))
        };
    }
}