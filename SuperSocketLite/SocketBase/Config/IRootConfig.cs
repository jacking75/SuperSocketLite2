namespace SuperSocketLite.SocketBase.Config;

/// <summary>The root configuration interface</summary>
public interface IRootConfig
{
    /// <summary>Gets the max working threads.</summary>
    int MaxWorkingThreads { get; }

    /// <summary>Gets the min working threads.</summary>
    int MinWorkingThreads { get; }

    /// <summary>Gets the max completion port threads.</summary>
    int MaxCompletionPortThreads { get; }

    /// <summary>Gets the min completion port threads.</summary>
    int MinCompletionPortThreads { get; }

}
