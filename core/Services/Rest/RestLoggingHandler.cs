using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CypherNetwork.Extensions;
using Serilog;

namespace CypherNetwork.Services.Rest;

public class RestLoggingHandler : DelegatingHandler
{
    private readonly ILogger _logger;

    public RestLoggingHandler(ILogger logger, HttpMessageHandler innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _logger.Here().Debug("HTTP Request {@Method} {@Scheme}://{@Host}:{@Port}{@Path}",
            request.Method,
            request.RequestUri?.Scheme, request.RequestUri?.Host, request.RequestUri?.Port.ToString(),
            request.RequestUri?.PathAndQuery);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        _logger.Here().Debug("HTTP Response {@StatusCode}", response.StatusCode);

        return response;
    }
}