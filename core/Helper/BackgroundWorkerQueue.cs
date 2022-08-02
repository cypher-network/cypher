using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CypherNetwork.Helper;

public interface IBackgroundWorkerQueue
{
    /// <summary>
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// </summary>
    /// <param name="workItem"></param>
    /// <exception cref="ArgumentNullException"></exception>
    void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
}

/// <summary>
/// </summary>
public class BackgroundWorkerQueue : IBackgroundWorkerQueue
{
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _workItems = new();

    /// <summary>
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);

        return workItem;
    }

    /// <summary>
    /// </summary>
    /// <param name="workItem"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
    {
        if (workItem == null) throw new ArgumentNullException(nameof(workItem));

        _workItems.Enqueue(workItem);
        _signal.Release();
    }
}