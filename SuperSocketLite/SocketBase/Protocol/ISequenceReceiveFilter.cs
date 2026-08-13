using System.Buffers;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>
/// Opt-in receive filter for the Pipelines receive path.
/// Implementations parse directly from ReadOnlySequence without the AppSession carry buffer.
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface ISequenceReceiveFilter<TRequestInfo> : IReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>Filters received data from a ReadOnlySequence.</summary>
    /// <param name="buffer">The received data available from PipeReader.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="examined">The position up to which data was examined.</param>
    /// <returns>A request when a full request is available; otherwise null.</returns>
    TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);
}
