using rxcypcore.Serf;
using Serilog;

namespace rxcypcore.Network
{
    public interface ILocalNode
    {
    }

    public class LocalNode : ILocalNode
    {
        //private readonly ISerfClient _serfClient;
        //private readonly ILogger _logger;

        public LocalNode(/*ISerfClient serfClient,*//* ILogger logger*/)
        {
            //_serfClient = serfClient;
            //_logger = logger.ForContext("SourceContext", nameof(LocalNode));

            //_logger.Information("Initiated local node");
        }
    }
}