// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CYPCore.Helper;
using CYPCore.Messages;
using CYPCore.Models;
using CYPCore.Serf.Message;

namespace CYPCore.Serf
{
    public interface ISerfClient
    {
        ulong ClientId { get; }
        string ProcessError { get; set; }
        bool ProcessStarted { get; set; }
        int ProcessId { get; set; }

        string Name { get; set; }

        SerfConfigurationOptions SerfConfigurationOptions { get; }

        ApiConfigurationOptions ApiConfigurationOptions { get; }

        Task<TaskResult<int>> MembersCount(Guid tcpSessionId);

        Task<SerfError> Authenticate(string secret, Guid tcpSessionId);
        void Dispose();
        Task<SerfError> Handshake(Guid tcpSessionId);
        Task<(KeyActionResponse response, SerfError error)> InstallKey(string key, Guid tcpSessionId);
        Task<TaskResult<JoinMessage>> Join(IEnumerable<string> members, Guid tcpSessionId, bool replay = false);
        Task<SerfError> Leave(Guid tcpSessionId);
        Task<(KeyListResponse response, SerfError error)> ListKeys(Guid tcpSessionId);
        Task<TaskResult<MemberMessage>> Members(Guid tcpSessionId);
        Task<(KeyActionResponse response, SerfError error)> RemoveKey(string key, Guid tcpSessionId);
        Task<TaskResult<SerfError>> Connect(Guid tcpSessionId);
        Task<(KeyActionResponse response, SerfError error)> UseKey(string key, Guid tcpSessionId);
        TcpSession TcpSessionsAddOrUpdate(TcpSession tcpSession);
        TcpSession GetTcpSession(Guid sessionId);
        bool RemoveTcpSession(Guid tcpSessionId);
        Task<TaskResult<ulong>> GetClientID();
    }
}