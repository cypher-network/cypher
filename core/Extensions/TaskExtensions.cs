#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace CypherNetwork.Extensions;

public static class TaskExtensions
{
    private static Action<Exception>? _onException;
    private static bool _shouldAlwaysRethrowException;

    public static void SwallowException(this Task task)
    {
        task.ContinueWith(_ => { });
    }

    /// <summary>
    ///     Safely execute the ValueTask without waiting for it to complete before moving to the next line of code; commonly
    ///     known as "Fire And Forget". Inspired by John Thiriet's blog post, "Removing Async Void":
    ///     https://johnthiriet.com/removing-async-void/.
    /// </summary>
    /// <param name="task">ValueTask.</param>
    /// <param name="onException">
    ///     If an exception is thrown in the ValueTask, <c>onException</c> will execute. If onException
    ///     is null, the exception will be re-thrown
    /// </param>
    /// <param name="continueOnCapturedContext">
    ///     If set to <c>true</c>, continue on captured context; this will ensure that the
    ///     Synchronization Context returns to the calling thread. If set to <c>false</c>, continue on a different context;
    ///     this will allow the Synchronization Context to continue on a different thread
    /// </param>
    public static void SafeFireAndForget(this ValueTask task, in Action<Exception>? onException = null,
        in bool continueOnCapturedContext = false)
    {
        HandleSafeFireAndForget(task, continueOnCapturedContext, onException);
    }


    /// <summary>
    ///     Safely execute the ValueTask without waiting for it to complete before moving to the next line of code; commonly
    ///     known as "Fire And Forget". Inspired by John Thiriet's blog post, "Removing Async Void":
    ///     https://johnthiriet.com/removing-async-void/.
    /// </summary>
    /// <param name="task">ValueTask.</param>
    /// <param name="onException">
    ///     If an exception is thrown in the Task, <c>onException</c> will execute. If onException is
    ///     null, the exception will be re-thrown
    /// </param>
    /// <param name="continueOnCapturedContext">
    ///     If set to <c>true</c>, continue on captured context; this will ensure that the
    ///     Synchronization Context returns to the calling thread. If set to <c>false</c>, continue on a different context;
    ///     this will allow the Synchronization Context to continue on a different thread
    /// </param>
    /// <typeparam name="TException">Exception type. If an exception is thrown of a different type, it will not be handled</typeparam>
    public static void SafeFireAndForget<TException>(this ValueTask task, in Action<TException>? onException = null,
        in bool continueOnCapturedContext = false) where TException : Exception
    {
        HandleSafeFireAndForget(task, continueOnCapturedContext, onException);
    }


    /// <summary>
    ///     Safely execute the Task without waiting for it to complete before moving to the next line of code; commonly known
    ///     as "Fire And Forget". Inspired by John Thiriet's blog post, "Removing Async Void":
    ///     https://johnthiriet.com/removing-async-void/.
    /// </summary>
    /// <param name="task">Task.</param>
    /// <param name="onException">
    ///     If an exception is thrown in the Task, <c>onException</c> will execute. If onException is
    ///     null, the exception will be re-thrown
    /// </param>
    /// <param name="continueOnCapturedContext">
    ///     If set to <c>true</c>, continue on captured context; this will ensure that the
    ///     Synchronization Context returns to the calling thread. If set to <c>false</c>, continue on a different context;
    ///     this will allow the Synchronization Context to continue on a different thread
    /// </param>
    public static void SafeFireAndForget(this Task task, in Action<Exception>? onException = null,
        in bool continueOnCapturedContext = false)
    {
        HandleSafeFireAndForget(task, continueOnCapturedContext, onException);
    }

    /// <summary>
    ///     Safely execute the Task without waiting for it to complete before moving to the next line of code; commonly known
    ///     as "Fire And Forget". Inspired by John Thiriet's blog post, "Removing Async Void":
    ///     https://johnthiriet.com/removing-async-void/.
    /// </summary>
    /// <param name="task">Task.</param>
    /// <param name="onException">
    ///     If an exception is thrown in the Task, <c>onException</c> will execute. If onException is
    ///     null, the exception will be re-thrown
    /// </param>
    /// <param name="continueOnCapturedContext">
    ///     If set to <c>true</c>, continue on captured context; this will ensure that the
    ///     Synchronization Context returns to the calling thread. If set to <c>false</c>, continue on a different context;
    ///     this will allow the Synchronization Context to continue on a different thread
    /// </param>
    /// <typeparam name="TException">Exception type. If an exception is thrown of a different type, it will not be handled</typeparam>
    public static void SafeFireAndForget<TException>(this Task task, in Action<TException>? onException = null,
        in bool continueOnCapturedContext = false) where TException : Exception
    {
        HandleSafeFireAndForget(task, continueOnCapturedContext, onException);
    }


    public static void SetDefaultExceptionHandling(in Action<Exception> onException)
    {
        _onException = onException ?? throw new ArgumentNullException(nameof(onException));
    }

    private static async void HandleSafeFireAndForget<TException>(ValueTask valueTask, bool continueOnCapturedContext,
        Action<TException>? onException) where TException : Exception
    {
        try
        {
            await valueTask.ConfigureAwait(continueOnCapturedContext);
        }
        catch (TException ex) when (_onException != null || onException != null)
        {
            HandleException(ex, onException);

            if (_shouldAlwaysRethrowException)
                throw;
        }
    }

    private static async void HandleSafeFireAndForget<TException>(Task task, bool continueOnCapturedContext,
        Action<TException>? onException) where TException : Exception
    {
        try
        {
            await task.ConfigureAwait(continueOnCapturedContext);
        }
        catch (TException ex) when (_onException != null || onException != null)
        {
            HandleException(ex, onException);

            if (_shouldAlwaysRethrowException)
                throw;
        }
    }

    private static void HandleException<TException>(in TException exception, in Action<TException>? onException)
        where TException : Exception
    {
        _onException?.Invoke(exception);
        onException?.Invoke(exception);
    }

    public static async void SafeForgetAsync(this Task task, ILogger logger)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
        }
    }

    public static Task WaitOneAsync(this WaitHandle waitHandle)
    {
        if (waitHandle == null)
            throw new ArgumentNullException(nameof(waitHandle));

        var tcs = new TaskCompletionSource<bool>();
        var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
            delegate { tcs.TrySetResult(true); }, null, -1, true);
        var t = tcs.Task;
        t.ContinueWith(antecedent => rwh.Unregister(null));
        return t;
    }

    public static async Task<bool> WaitOneAsync(this WaitHandle handle, int millisecondsTimeout,
        CancellationToken cancellationToken)
    {
        RegisteredWaitHandle? registeredHandle = null;
        var tokenRegistration = default(CancellationTokenRegistration);
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            registeredHandle = ThreadPool.RegisterWaitForSingleObject(
                handle,
                (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
                tcs,
                millisecondsTimeout,
                true);
            tokenRegistration = cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
                tcs);
            return await tcs.Task;
        }
        finally
        {
            registeredHandle?.Unregister(null);
            await tokenRegistration.DisposeAsync();
        }
    }

    public static Task<bool> WaitOneAsync(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return handle.WaitOneAsync((int)timeout.TotalMilliseconds, cancellationToken);
    }

    public static Task<bool> WaitOneAsync(this WaitHandle handle, CancellationToken cancellationToken)
    {
        return handle.WaitOneAsync(Timeout.Infinite, cancellationToken);
    }
}