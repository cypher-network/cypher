// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Autofac;

using CliWrap;
using CliWrap.EventStream;

using CYPCore.Extentions;
using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Cryptography;
using System.Runtime.InteropServices;

namespace CYPCore.Services
{
    public class SerfService : ISerfService, IStartable
    {
        private readonly ISerfClient _serfClient;
        private readonly ISigning _signing;
        private readonly ILogger _logger;
        private readonly TcpSession _tcpSession;

        public SerfService(ISerfClient serfClient, ISigning signing, ILogger<SerfService> logger)
        {
            _serfClient = serfClient;
            _signing = signing;
            _logger = logger;

            _tcpSession = _serfClient.TcpSessionsAddOrUpdate(new TcpSession(
                serfClient.SerfConfigurationOptions.Listening).Connect(_serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start()
        {
            // Empty
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken stoppingToken)
        {
            if (_serfClient.ProcessStarted)
                return;

            if (IsRunning())
            {
                _logger.LogWarning("Serf is already running. It's OK if you are running on a different port.");
            }

            var useExisting = await TryUseExisting();
            if (useExisting)
            {
                _serfClient.ProcessStarted = true;
                _logger.LogInformation("Process Id cannot be found at this moment.");
                return;
            }

            try
            {
                stoppingToken.Register(() =>
                {
                    var process = Process.GetProcessById(_serfClient.ProcessId);
                    process?.Kill();
                });
                
                var pubKey = await _signing.GePublicKey(_signing.DefaultSigningKeyName);

                _serfClient.Name = $"{_serfClient.SerfConfigurationOptions.NodeName}-{Helper.Util.SHA384ManagedHash(pubKey).ByteToHex()}";
                _serfClient.P2PConnectionOptions.ClientId = Helper.Util.HashToId(pubKey.ByteToHex());

                var serfPath = GetFilePath();

                _logger.LogInformation($"Serf assembly path: {serfPath}");

                //  Chmod before attempting to execute serf on Linux and Mac
                if (new OSPlatform[] { OSPlatform.Linux, OSPlatform.OSX }.Contains(Helper.Util.GetOSPlatform()))
                {
                    _logger.LogInformation("Granting execute permission on serf assembly");

                    var chmodCmd = Cli.Wrap("chmod")
                       .WithArguments(a => a
                       .Add("+x")
                       .Add(serfPath));

                    await chmodCmd.ExecuteAsync();
                }

                var cmd = Cli.Wrap(serfPath)
                    .WithArguments(a => a
                    .Add("agent")
                    .Add($"-bind={_serfClient.SerfConfigurationOptions.Listening}")
                    .Add($"-rpc-addr={_serfClient.SerfConfigurationOptions.RPC}")
                    .Add($"-advertise={_serfClient.SerfConfigurationOptions.Advertise}")
                    .Add($"-encrypt={ _serfClient.SerfConfigurationOptions.Encrypt}")
                    .Add($"-node={_serfClient.Name}")
                    .Add($"-snapshot={_serfClient.SerfConfigurationOptions.SnapshotPath}")
                    .Add($"-rejoin={_serfClient.SerfConfigurationOptions.Rejoin}")
                    .Add($"-broadcast-timeout={_serfClient.SerfConfigurationOptions.BroadcasTimeout}")
                    .Add($"-retry-max={_serfClient.SerfConfigurationOptions.RetryMax}")
                    .Add($"-log-level={_serfClient.SerfConfigurationOptions.Loglevel}")
                    .Add("-tag")
                    .Add($"pubkey={pubKey.ByteToHex()}")
                    .Add("-tag")
                    .Add($"p2pblockport={_serfClient.P2PConnectionOptions.GetBlockSocketIPEndPoint().Port}")
                    .Add("-tag")
                    .Add($"p2pmempoolport={_serfClient.P2PConnectionOptions.GetMempoolSocketIPEndPoint().Port}"));

                await foreach (var cmdEvent in cmd.ListenAsync(stoppingToken))
                {
                    switch (cmdEvent)
                    {
                        case StartedCommandEvent started:
                            _logger.LogInformation($"Process started; ID: {started.ProcessId}");
                            _serfClient.ProcessId = started.ProcessId;
                            break;
                        case StandardOutputCommandEvent stdOut:
                            if (stdOut.Text.Contains("agent: Serf agent starting"))
                            {
                                _logger.LogInformation("Serf has started!");
                                _serfClient.ProcessStarted = true;
                            }
                            _logger.LogInformation($"Out> {stdOut.Text}");
                            break;
                        case StandardErrorCommandEvent stdErr:
                            _logger.LogError($"Err> {stdErr.Text}");
                            _serfClient.ProcessError = stdErr.Text;
                            break;
                        case ExitedCommandEvent exited:
                            _logger.LogInformation($"Process exited; Code: {exited.ExitCode}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SerfService.StartAsync >>>: {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static bool IsRunning(string name = "serf")
        {
            return Process.GetProcessesByName(name).Length > 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private Task<bool> TryUseExisting()
        {
            var cancellationToken = new CancellationTokenSource();
            bool existing = false;

            try
            {
                Task.Run(async () => { existing = await TryReconnect(cancellationToken); },
                    cancellationToken.Token);

                while (true)
                {
                    if (cancellationToken.Token.IsCancellationRequested)
                        cancellationToken.Token.ThrowIfCancellationRequested();

                    Task.Delay(100, cancellationToken.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< SerfService.UseExisting >>>: {ex}");
            }

            return Task.FromResult(existing);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="existing"></param>
        /// <returns></returns>
        private async Task<bool> TryReconnect(CancellationTokenSource cancellationToken)
        {
            bool connect = false;

            try
            {
                if (IsRunning())
                {
                    var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                    var connectResult = await _serfClient.Connect(tcpSession.SessionId);

                    if (!connectResult.Success)
                    {
                        cancellationToken.Cancel();
                        return connect;
                    }

                    var membersResult = await _serfClient.Members(tcpSession.SessionId);
                    if (!membersResult.Success)
                    {
                        cancellationToken.Cancel();
                        return connect;
                    }

                    var pubkey = await _signing.GePublicKey(_signing.DefaultSigningKeyName);
                    var hasMember = membersResult.Value.Members.FirstOrDefault(x => x.Tags["pubkey"] == pubkey.ByteToHex());
                    if (hasMember != null)
                    {
                        connect = true;
                    }
                }
                else
                {
                    cancellationToken.Cancel();
                }
            }
            catch (OperationCanceledException)
            {

            }

            return connect;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seedNode"></param>
        public async Task JoinSeedNodes(SeedNode seedNode)
        {
            try
            {
                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (!tcpSession.Ready)
                {
                    tcpSession = tcpSession.Connect(_serfClient.SerfConfigurationOptions.RPC);
                    await _serfClient.Connect(tcpSession.SessionId);
                }

                var joinResult = await _serfClient.Join(seedNode.Seeds, tcpSession.SessionId);

                if (!joinResult.Success)
                {
                    _logger.LogError($"<<< SerfService.JoinSeedNodes >>>: {((SerfError)joinResult.NonSuccessMessage).Error}");
                    return;
                }

                _logger.LogInformation($"<<< SerfService.JoinSeedNodes >>>: Serf might still be trying to join the seed nodes. Number of nodes joined {joinResult.Value.Peers}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"<<< SerfService.JoinSeedNodes >>>: Could not create Serf RPC address {ex}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private string GetFilePath()
        {
            var entryAssemblyPath = Helper.Util.EntryAssemblyPath();
            var platform = Helper.Util.GetOSPlatform();
            string folder = platform.ToString().ToLowerInvariant();

            return Path.Combine(entryAssemblyPath, $"Serf/Terminal/{folder}/serf"); ;
        }
    }
}