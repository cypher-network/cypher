using System;
using System.Collections.Generic;
using System.Net;

namespace CypherNetworkNode.Configuration
{
    public class IPService
    {
        public IPService(string name, Uri uri)
        {
            _name = name;
            _uri = uri;
        }

        private readonly string _name;
        private readonly Uri _uri;

        public override string ToString()
        {
            return $"{_name} ({_uri})";
        }

        public IPAddress Read()
        {
            using var client = new WebClient();
            var response = client.DownloadString(_uri);
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