﻿//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using CYPCore.Serf;
using CYPCore.Models;
using CYPCore.Persistence;
using CYPCore.Serf.Message;
using CYPCore.Services.Rest;

namespace CYPCore.Ledger
{
    public class Sync : ISync
    {
        public bool SyncRunning { get; private set; }

        private const int BatchSize = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ISerfClient _serfClient;
        private readonly IValidator _validator;
        private readonly ILogger _logger;
        private readonly TcpSession _tcpSession;

        public Sync(IUnitOfWork unitOfWork, ISerfClient serfClient, IValidator validator, ILogger<Sync> logger)
        {
            _unitOfWork = unitOfWork;
            _serfClient = serfClient;
            _validator = validator;
            _logger = logger;

            _tcpSession = serfClient.TcpSessionsAddOrUpdate(new TcpSession(
                serfClient.SerfConfigurationOptions.Listening).Connect(serfClient.SerfConfigurationOptions.RPC));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Check()
        {
            SyncRunning = true;

            try
            {
                _logger.LogInformation("<<< Sync.SyncCheck >>>: Checking block height.");

                var tcpSession = _serfClient.GetTcpSession(_tcpSession.SessionId);
                if (!tcpSession.Ready)
                {
                    SyncRunning = false;
                    return;
                }

                List<Members> members = null;

                await _serfClient.Connect(tcpSession.SessionId);
                var membersResult = await _serfClient.Members(tcpSession.SessionId);

                if (!membersResult.Success)
                {
                    _logger.LogCritical("<<< Sync.SyncCheck >>>: Failed to get membership.");
                    return;
                }

                BlockHeight local = new(), remote = null;

                members = membersResult.Value.Members.ToList();

                foreach (var member in members)
                {
                    if (_serfClient.Name == member.Name || member.Status != "alive")
                        continue;

                    local.Height = await _unitOfWork.DeliveredRepository.CountAsync();

                    Uri.TryCreate($"{member.Tags["localhost"]}", UriKind.RelativeOrAbsolute, out Uri uri);

                    var blockRestApi = new BlockRestService(uri);

                    remote = await blockRestApi.Height();

                    _logger.LogInformation($"<<< Sync.SyncCheck >>>: Local node block height ({local.Height}). Network block height ({remote.Height}).");

                    if (local.Height < remote.Height)
                    {   
                        await Synchronize(uri, (int)local.Height);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Sync.SyncCheck >>>: {ex}");
            }

            SyncRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task Synchronize(Uri uri, int skip)
        {
            var throttler = new SemaphoreSlim(int.MaxValue);

            try
            {
                var allTasks = new List<Task>();

                await throttler.WaitAsync();

                allTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var blockRestApi = new BlockRestService(uri);
                        var blockHeaders = await blockRestApi.Range(skip, BatchSize);

                        if (blockHeaders.Any())
                        {
                            foreach (var blockHeader in blockHeaders)
                            {
                                try
                                {
                                    bool verified = await _validator.VerifyBlockHeader(blockHeader);
                                    if (!verified)
                                    {
                                        return;
                                    }

                                    var saved = await _unitOfWork.DeliveredRepository.PutAsync(blockHeader, blockHeader.ToIdentifier());
                                    if (saved == null)
                                    {
                                        _logger.LogError($"<<< Sync.Synchronize >>>: Unable to save block header: {blockHeader.MrklRoot}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogCritical($"<<< Sync.Synchronize >>>: {ex.Message}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }));

                try
                {
                    await Task.WhenAll(allTasks);
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger.LogError($"<<< Sync.Synchronize >>>: Failed to synchronize node: {ex}");
            }
            finally
            {
                throttler.Dispose();
            }
        }
    }
}
