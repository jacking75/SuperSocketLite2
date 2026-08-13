using System.Buffers;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Receive filter interface</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
/// <remarks>
/// A filter parses straight out of the session's receive pipe. Data it cannot turn into a request
/// yet stays in the pipe - the filter never copies it into a carry buffer of its own - so an
/// incomplete request costs nothing until the rest of it arrives.
/// </remarks>
public interface IReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>Filters the data available in the receive pipe into a request info.</summary>
    /// <param name="buffer">The received data available from PipeReader.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="examined">The position up to which data was examined.</param>
    /// <returns>A request when a full request is available; otherwise null.</returns>
    TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);

    /// <summary>Gets the next Receive filter.</summary>
    IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; }

    /// <summary>Resets this instance to initial state.</summary>
    void Reset();

    /// <summary>Gets the filter state.</summary>
    FilterState State { get; }
}
