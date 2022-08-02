using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace CypherNetwork.Helper;

/// <summary>
/// </summary>
public class LongRunningService : BackgroundService
{
    private readonly IBackgroundWorkerQueue _queue;

    /// <summary>
    /// </summary>
    /// <param name="queue"></param>
    public LongRunningService(IBackgroundWorkerQueue queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// </summary>
    /// <param name="stoppingToken"></param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _queue.DequeueAsync(stoppingToken);
            await workItem(stoppingToken);
        }
    }
}