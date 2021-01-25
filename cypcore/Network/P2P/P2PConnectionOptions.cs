// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;

namespace CYPCore.Network.P2P
{
    public class P2PConnectionOptions
    {
        public ulong ClientId { get; set; }
        public string TcpServerBlock { get; set; }
        public string TcpServerMempool { get; set; }
        public bool UseTls { get; set; }
        public bool UseCleanSession { get; set; }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public System.Net.IPEndPoint GetBlockSocketIPEndPoint()
        {
            return Helper.Util.TryParseAddress(TcpServerBlock);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public System.Net.IPEndPoint GetMempoolSocketIPEndPoint()
        {
            return Helper.Util.TryParseAddress(TcpServerMempool);
        }
    }
}
