// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Autofac;
using Serilog;

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

        public SerfService(ISerfClient serfClient, ISigning signing, ILogger logger)
        {
            _serfClient = serfClient;
            _signing = signing;
            _logger = logger.ForContext<SerfService>();

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
            var log = _logger.ForContext("Method", "StartAsync");
            
            if (_serfClient.ProcessStarted)
                return;

            if (IsRunning())
            {
                log.Warning("Serf is already running. It's OK if you are running on a different port.");
            }

            var useExisting = await TryUseExisting();
            if (useExisting)
            {
                _serfClient.ProcessStarted = true;
                log.Information("Process ID cannot be found at this moment.");
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

                _serfClient.Name = $"{_serfClient.SerfConfigurationOptions.NodeName}-{Helper.Util.SHA384ManagedHash(Guid.NewGuid().ToString().ToBytes()).ByteToHex()}";
                _serfClient.P2PConnectionOptions.ClientId = Helper.Util.HashToId(pubKey.ByteToHex());

                var serfPath = GetFilePath();

                log.Information("Serf assembly path: {@SerfPath}", serfPath);

                //  Chmod before attempting to execute serf on Linux and Mac
                if (new OSPlatform[] { OSPlatform.Linux, OSPlatform.OSX }.Contains(Helper.Util.GetOSPlatform()))
                {
                    log.Information("Granting execute permission on serf assembly");

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
                            log.Information("Process started; ID: {@ID}", started.ProcessId.ToString());
                            _serfClient.ProcessId = started.ProcessId;
                            break;
                        case StandardOutputCommandEvent stdOut:
                            if (stdOut.Text.Contains("agent: Serf agent starting"))
                            {
                                log.Information("Serf has started!");
                                _serfClient.ProcessStarted = true;
                            }
                            log.Information("Out> {@Out}", stdOut.Text);
                            break;
                        case StandardErrorCommandEvent stdErr:
                            log.Error("Err> {@Err}", stdErr.Text);
                            _serfClient.ProcessError = stdErr.Text;
                            break;
                        case ExitedCommandEvent exited:
                            log.Information("Process exited; Code: {@ExitCode}", exited.ExitCode);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Fatal("Exception starting Serf: {@Exception}", ex);
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
            var log = _logger.ForContext("Method", "TryUseExisting");
            
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
                log.Fatal("Exception using existing Serf: {@Exception}", ex);
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
            var log = _logger.ForContext("Method", "JoinSeedNodes");
            
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
                    log.Error("Error joining seed nodes: {@Error}", ((SerfError)joinResult.NonSuccessMessage).Error);
                    return;
                }

                log.Information("Serf might still be trying to join the seed nodes. Number of nodes joined: {@PeerCount}", joinResult.Value.Peers);
            }
            catch (Exception ex)
            {
                _logger.Fatal("Could not create Serf RPC address: {@Exception}", ex);
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