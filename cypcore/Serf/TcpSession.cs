// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace CYPCore.Serf
{
    public class TcpSession : IEqualityComparer<TcpSession>
    {
        public Guid SessionId { get; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream TransportStream { get; set; }

        public bool Ready { get; private set; }

        private readonly string _listening;

        public TcpSession(string listening)
        {
            _listening = listening;
            SessionId = Guid.NewGuid();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        public TcpSession Connect(string rpc)
        {
            try
            {
                var endpoint = Helper.Util.TryParseAddress(rpc);
                var tcpClient = new TcpClient(endpoint.Address.ToString(), endpoint.Port);

                TcpClient = tcpClient;
                TransportStream = tcpClient.GetStream();

                Ready = true;
            }
            catch (Exception)
            {
                Ready = false;
            }

            return this;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool CloseConnection()
        {
            bool close;
            try
            {
                TransportStream.Close();
                TcpClient.Close();

                close = true;
            }
            catch (Exception)
            {
                throw;
            }

            return close;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Equals(TcpSession x, TcpSession y)
        {
            return x.SessionId == y.SessionId;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tcpSession"></param>
        /// <returns></returns>
        public int GetHashCode(TcpSession tcpSession)
        {
            TcpSession tcp = tcpSession;
            return tcp.SessionId.GetHashCode();
        }
    }
}
