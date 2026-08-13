namespace SuperSocketLite.SocketBase;

/// <summary>
/// Async extension class
/// </summary>
public static class Async
{
    /// <summary>
    /// Runs the task on the thread pool and logs any exception it throws through
    /// <paramref name="logProvider"/>, so a faulted task can never go unobserved.
    /// </summary>
    /// <param name="logProvider">The log provider.</param>
    /// <param name="task">The task.</param>
    /// <returns></returns>
    public static Task AsyncRun(this ILogProvider logProvider, Action task)
    {
        return Task.Factory.StartNew(task, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
            .ContinueWith(t =>
            {
                var logger = logProvider.Logger;

                if (logger == null || !logger.IsErrorEnabled)
                    return;

                var innerExceptions = t.Exception!.InnerExceptions;

                for (var i = 0; i < innerExceptions.Count; i++)
                {
                    logger.Error(innerExceptions[i].ToString());
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
}
