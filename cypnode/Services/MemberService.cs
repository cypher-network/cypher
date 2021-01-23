﻿// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Serilog;

using CYPCore.Cryptography;
using CYPCore.Serf;
using CYPCore.Serf.Message;

namespace CYPNode.Services
{
    public class MemberService : IMemberService
    {
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signingProvider;
        private readonly ILogger _logger;

        private TcpSession _tcpSession;

        public MemberService(ISerfClient serfClient, ISigning signingProvider, ILogger logger)
        {
            _serfClient = serfClient;
            _signingProvider = signingProvider;
            _logger = logger.ForContext<MemberService>();

            Ready();
        }

        /// <summary>
        /// 
        /// </summary>
        public void Ready()
        {
            _tcpSession = _serfClient.TcpSessionsAddOrUpdate(
                new TcpSession(_serfClient.SerfConfigurationOptions.Listening)
                .Connect(_serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task<IEnumerable<Members>> GetMembers()
        {
            var log = _logger.ForContext("Method", "GetMembers");
            
            var members = Enumerable.Empty<Members>();

            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (tcpSession.Ready)
                {
                    var connectResult = _serfClient.Connect(tcpSession.SessionId);

                    var membersResult = await _serfClient.Members(tcpSession.SessionId);
                    if (!membersResult.Success)
                    {
                        return null;
                    }

                    members = membersResult.Value.Members;
                }
            }
            catch (Exception ex)
            {
                log.Error("Cannot get members {@Error}", ex);
            }

            return members;
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task<byte[]> GetPublicKey()
        {
            var log = _logger.ForContext("Method", "GetPublicKey");
            
            byte[] publicKey = null;

            try
            {
                publicKey = await _signingProvider.GePublicKey(_signingProvider.DefaultSigningKeyName);
            }
            catch (Exception ex)
            {
                log.Error("Cannot get public key {@Error}", ex);
            }

            return publicKey;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetCount()
        {
            var log = _logger.ForContext("Method", "GetCount");
            
            int count = 0;

            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (tcpSession.Ready)
                {
                    var connectResult = _serfClient.Connect(tcpSession.SessionId);

                    var membersCountResult = await _serfClient.MembersCount(tcpSession.SessionId);
                    if (!membersCountResult.Success)
                    {
                        return 0;
                    }

                    count = membersCountResult.Value;
                }
            }
            catch (Exception ex)
            {
                log.Error("Cannot get member count {@Error}", ex);
            }

            return count;
        }
    }
}
