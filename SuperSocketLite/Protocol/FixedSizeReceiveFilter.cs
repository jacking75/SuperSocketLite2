using System.Buffers;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>Receive filter for a protocol whose every request has the same fixed size.</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class FixedSizeReceiveFilter<TRequestInfo> : IReceiveFilter<TRequestInfo>, IReceiveFilterInitializer
    where TRequestInfo : IRequestInfo
{
    /// <summary>Gets the size of the fixed size Receive filter.</summary>
    public int Size { get; }

    /// <summary>Null RequestInfo</summary>
    protected readonly static TRequestInfo? NullRequestInfo = default(TRequestInfo);

    protected FixedSizeReceiveFilter(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        Size = size;
    }

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        OnInitialized(appServer, session);
    }

    /// <summary>Called after the filter is initialized for a session.</summary>
    protected virtual void OnInitialized(IAppServer appServer, IAppSession session)
    {
    }

    /// <summary>Filters one fixed-size block out of the receive pipe.</summary>
    public virtual TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length < Size)
            return NullRequestInfo;

        var matched = buffer.Slice(0, Size);

        consumed = buffer.GetPosition(Size);
        examined = consumed;

        return ProcessMatchedRequest(matched);
    }

    /// <summary>Resolves the matched block into a request info.</summary>
    /// <param name="buffer">Exactly <see cref="Size"/> bytes; it may span several pipe segments.</param>
    protected abstract TRequestInfo? ProcessMatchedRequest(ReadOnlySequence<byte> buffer);

    /// <summary>Gets the next Receive filter.</summary>
    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;

    /// <summary>Gets the filter state.</summary>
    public FilterState State { get; protected set; }

    /// <summary>Resets this instance.</summary>
    public virtual void Reset()
    {
        State = FilterState.Normal;
    }
}
