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
    public static Task AsyncRun(this ILogProvider logProvider, Action task)
    {
        return Task.Run(() =>
        {
            try
            {
                task();
            }
            catch (Exception e)
            {
                var logger = logProvider.Logger;

                if (logger != null && logger.IsErrorEnabled)
                {
                    logger.Error(e.ToString());
                }
            }
        });
    }
}
