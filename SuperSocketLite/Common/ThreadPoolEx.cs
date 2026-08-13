namespace SuperSocketLite.Common;

/// <summary>Thread pool extension class</summary>
public static class ThreadPoolEx
{
    /// <summary>Resets the thread pool.</summary>
    public static bool ResetThreadPool(int? maxWorkingThreads, int? maxCompletionPortThreads, int? minWorkingThreads, int? minCompletionPortThreads)
    {
        if (maxWorkingThreads.HasValue || maxCompletionPortThreads.HasValue)
        {
            int oldMaxWorkingThreads, oldMaxCompletionPortThreads;

            ThreadPool.GetMaxThreads(out oldMaxWorkingThreads, out oldMaxCompletionPortThreads);

            if (!maxWorkingThreads.HasValue)
                maxWorkingThreads = oldMaxWorkingThreads;

            if (!maxCompletionPortThreads.HasValue)
                maxCompletionPortThreads = oldMaxCompletionPortThreads;

            if (maxWorkingThreads.Value != oldMaxWorkingThreads
                || maxCompletionPortThreads.Value != oldMaxCompletionPortThreads)
            {
                if (!ThreadPool.SetMaxThreads(maxWorkingThreads.Value, maxCompletionPortThreads.Value))
                    return false;
            }
        }

        if (minWorkingThreads.HasValue || minCompletionPortThreads.HasValue)
        {
            int oldMinWorkingThreads, oldMinCompletionPortThreads;

            ThreadPool.GetMinThreads(out oldMinWorkingThreads, out oldMinCompletionPortThreads);

            if (!minWorkingThreads.HasValue)
                minWorkingThreads = oldMinWorkingThreads;

            if (!minCompletionPortThreads.HasValue)
                minCompletionPortThreads = oldMinCompletionPortThreads;

            if (minWorkingThreads.Value != oldMinWorkingThreads
                || minCompletionPortThreads.Value != oldMinCompletionPortThreads)
            {
                if (!ThreadPool.SetMinThreads(minWorkingThreads.Value, minCompletionPortThreads.Value))
                    return false;
            }
        }

        return true;
    }
}
