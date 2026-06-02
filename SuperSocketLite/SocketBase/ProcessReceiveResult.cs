using System;

namespace SuperSocketLite.SocketBase;

/// <summary>
/// Result of processing data from a PipeReader.
/// </summary>
public readonly struct ProcessReceiveResult
{
    public ProcessReceiveResult(SequencePosition consumed, SequencePosition examined)
    {
        Consumed = consumed;
        Examined = examined;
    }

    /// <summary>
    /// Gets the position up to which data was consumed.
    /// </summary>
    public SequencePosition Consumed { get; }

    /// <summary>
    /// Gets the position up to which data was examined.
    /// </summary>
    public SequencePosition Examined { get; }
}
