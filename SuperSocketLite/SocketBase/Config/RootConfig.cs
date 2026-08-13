using SuperSocketLite.Common;


namespace SuperSocketLite.SocketBase.Config;

/// <summary>Root configuration model</summary>
[Serializable]
public class RootConfig : IRootConfig
{
    public RootConfig(IRootConfig rootConfig)
    {
        rootConfig.CopyPropertiesTo(this);
    }

    public RootConfig()
    {
        int maxWorkingThread, maxCompletionPortThreads;
        ThreadPool.GetMaxThreads(out maxWorkingThread, out maxCompletionPortThreads);
        MaxWorkingThreads = maxWorkingThread;
        MaxCompletionPortThreads = maxCompletionPortThreads;

        int minWorkingThread, minCompletionPortThreads;
        ThreadPool.GetMinThreads(out minWorkingThread, out minCompletionPortThreads);
        MinWorkingThreads = minWorkingThread;
        MinCompletionPortThreads = minCompletionPortThreads;            
    }

    

    /// <summary>Gets/Sets the max working threads.</summary>
    public int MaxWorkingThreads { get; set; }

    /// <summary>Gets/sets the min working threads.</summary>
    public int MinWorkingThreads { get; set; }

    /// <summary>Gets/sets the max completion port threads.</summary>
    public int MaxCompletionPortThreads { get; set; }

    /// <summary>Gets/sets the min completion port threads.</summary>
    public int MinCompletionPortThreads { get; set; }
}
