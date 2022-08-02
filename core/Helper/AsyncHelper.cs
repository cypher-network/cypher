using System;
using System.Threading;
using System.Threading.Tasks;

namespace CypherNetwork.Helper;

/// <summary>
/// 
/// </summary>
public static class AsyncHelper
{
    private static readonly TaskFactory MyTaskFactory = new(CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="func"></param>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    public static async Task<TResult> RunSyncAsync<TResult>(Func<Task<TResult>> func)
    {
        return await MyTaskFactory
            .StartNew(func)
            .Unwrap()
;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="func"></param>
    public static async Task RunSyncAsync(Func<Task> func)
    {
        await MyTaskFactory
            .StartNew<Task>(func)
            .Unwrap()
;
    }
}