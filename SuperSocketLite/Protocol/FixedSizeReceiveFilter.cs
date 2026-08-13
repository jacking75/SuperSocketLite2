using System.Buffers;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>FixedSizeReceiveFilter</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class FixedSizeReceiveFilter<TRequestInfo> : ISequenceReceiveFilter<TRequestInfo>, IOffsetAdapter, IReceiveFilterInitializer
    where TRequestInfo : IRequestInfo
{
    private int _parsedLength;

    private int _size;

    /// <summary>Gets the size of the fixed size Receive filter.</summary>
    public int Size => _size;

    /// <summary>Null RequestInfo</summary>
    protected readonly static TRequestInfo? NullRequestInfo = default(TRequestInfo);

    protected FixedSizeReceiveFilter(int size)
    {
        _size = size;
    }

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        OnInitialized(appServer, session);
    }

    /// <summary>Called after the filter is initialized for a session.</summary>
    protected virtual void OnInitialized(IAppServer appServer, IAppSession session)
    {
    }

    /// <summary>Filters the specified session.</summary>
    public virtual TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        rest = _parsedLength + length - _size;

        if (rest >= 0)
        {
            var requestInfo = ProcessMatchedRequest(readBuffer, offset - _parsedLength, _size, toBeCopied);
            InternalReset();
            return requestInfo;
        }
        else
        {
            _parsedLength += length;
            _offsetDelta = _parsedLength;
            rest = 0;

            //The carry buffer always starts at offset 0, so unparsed bytes are moved to the front
            //whenever the filter has not yet caught up with the end of the current read.
            if (_offsetDelta < offset + length)
            {
                Buffer.BlockCopy(readBuffer, offset - _parsedLength + length, readBuffer, 0, _parsedLength);
            }

            return NullRequestInfo;
        }
    }

    /// <summary>
    /// Filters received data directly from the Pipelines sequence path.
    /// Incomplete data is left unconsumed in the PipeReader.
    /// </summary>
    public virtual TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length < _size)
        {
            _parsedLength = ToInt32BufferSize(buffer.Length);
            _offsetDelta = 0;
            return NullRequestInfo;
        }

        var matched = buffer.Slice(0, _size);
        var requestInfo = matched.IsSingleSegment
            ? ProcessMatchedRequest(matched.First.Span, false)
            : ProcessMatchedRequest(matched.ToArray(), 0, _size, false);
        consumed = buffer.GetPosition(_size);
        examined = consumed;
        InternalReset();
        return requestInfo;
    }

    /// <summary>Filters the buffer after the server receive the enough size of data.</summary>
    protected abstract TRequestInfo? ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied);

    /// <summary>
    /// Filters the buffer using ReadOnlySpan after the server receives enough data.
    /// Default implementation converts span to array and calls the byte[] version.
    /// Override for zero-allocation processing.
    /// </summary>
    /// <param name="buffer">The buffer as ReadOnlySpan.</param>
    protected virtual TRequestInfo? ProcessMatchedRequest(ReadOnlySpan<byte> buffer, bool toBeCopied)
    {
        return ProcessMatchedRequest(buffer.ToArray(), 0, buffer.Length, toBeCopied);
    }

    /// <summary>Gets the size of the rest buffer.</summary>
    public virtual int LeftBufferSize => _parsedLength;

    /// <summary>Gets the next Receive filter.</summary>
    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;


    private int _offsetDelta;

    /// <summary>Gets the offset delta.</summary>
    int IOffsetAdapter.OffsetDelta => _offsetDelta;

    /// <summary>Gets the filter state.</summary>
    public FilterState State { get; protected set; }

    private void InternalReset()
    {
        _parsedLength = 0;
        _offsetDelta = 0;
    }

    /// <summary>Resets this instance.</summary>
    public virtual void Reset()
    {
        InternalReset();
        State = FilterState.Normal;
    }

    private static int ToInt32BufferSize(long length)
    {
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }
}
