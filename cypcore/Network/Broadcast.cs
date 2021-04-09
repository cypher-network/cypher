// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CYPCore.Extensions;
using CYPCore.Models;
using Dawn;
using Rx.Http;
using Serilog;

namespace CYPCore.Network
{
    public class Broadcast
    {
        private const sbyte Open = 0;
        private const sbyte Tripped = 1;
        private const string Pending = "Pending";
        private const string Unavailable = "Unavailable";

        private long _circuitStatus;
        private readonly SemaphoreSlim _semaphore;

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public Broadcast(ILogger logger, int maxConcurrentRequests)
        {
            _logger = logger;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _semaphore = new SemaphoreSlim(maxConcurrentRequests);
            _circuitStatus = Open;

            ServicePointManager.DefaultConnectionLimit = maxConcurrentRequests;
            ServicePointManager.MaxServicePointIdleTime = 2500;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="topicType"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public async Task<string> Send(byte[] data, TopicType topicType, string host)
        {
            Guard.Argument(data, nameof(data)).NotNull();
            Guard.Argument(host, nameof(data)).NotNull().NotEmpty().NotWhiteSpace();

            try
            {
                if (Uri.TryCreate($"{host}", UriKind.Absolute, out var uri))
                {
                    await _semaphore.WaitAsync();

                    switch (topicType)
                    {
                        case TopicType.AddBlockGraph:
                            {
                                var http = new RxHttpClient(_httpClient, null);
                                http.Post($"{host}/chain/blockgraph", data)
                                    .Subscribe(response =>
                                        {
                                            if (response.StatusCode == HttpStatusCode.OK) return;
                                            TripCircuit(reason: $"Status not OK. Status={response.StatusCode}");
                                        },
                                        exception =>
                                        {
                                            _logger.Here().Error(exception, "HttpRequestException for {@Host}", host);
                                        });
                                break;
                            }
                        case TopicType.AddTransaction:
                            {
                                var http = new RxHttpClient(_httpClient, null);
                                http.Post($"{host}/mem/transaction", data)
                                    .Subscribe(response =>
                                        {
                                            if (response.StatusCode == HttpStatusCode.OK) return;
                                            TripCircuit(reason: $"Status not OK. Status={response.StatusCode}");
                                        },
                                        exception =>
                                        {
                                            _logger.Here().Error(exception, "HttpRequestException for {@Host}", host);
                                        });
                                break;
                            }
                    }
                }
                else
                {
                    _logger.Here().Error("Cannot create URI for host {@Host}", host);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                TripCircuit(reason: $"Timeout");
                return Unavailable;
            }
            finally
            {
                _semaphore.Release();
            }

            return Pending;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="reason"></param>
        private void TripCircuit(string reason)
        {
            if (Interlocked.CompareExchange(ref _circuitStatus, Tripped, Open) == Open)
            {
                _logger.Here().Warning("Tripping circuit because {@reason}", reason);
            }
        }
    }
}