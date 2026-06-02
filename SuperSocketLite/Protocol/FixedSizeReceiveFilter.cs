using System;
using System.Buffers;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// FixedSizeReceiveFilter
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class FixedSizeReceiveFilter<TRequestInfo> : ISequenceReceiveFilter<TRequestInfo>, IOffsetAdapter, IReceiveFilterInitializer
    where TRequestInfo : IRequestInfo
{
    private int m_ParsedLength;

    private int m_Size;

    /// <summary>
    /// Gets the size of the fixed size Receive filter.
    /// </summary>
    public int Size
    {
        get { return m_Size; }
    }

    /// <summary>
    /// Null RequestInfo
    /// </summary>
    protected readonly static TRequestInfo? NullRequestInfo = default(TRequestInfo);

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedSizeReceiveFilter&lt;TRequestInfo&gt;"/> class.
    /// </summary>
    /// <param name="size">The size.</param>
    protected FixedSizeReceiveFilter(int size)
    {
        m_Size = size;
    }

    private int m_OrigOffset;

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        m_OrigOffset = session.SocketSession.OrigReceiveOffset;
        OnInitialized(appServer, session);
    }

    /// <summary>
    /// Called after the filter is initialized for a session.
    /// </summary>
    protected virtual void OnInitialized(IAppServer appServer, IAppSession session)
    {
    }

    /// <summary>
    /// Filters the specified session.
    /// </summary>
    /// <param name="readBuffer">The read buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <param name="rest">The rest.</param>
    /// <returns></returns>
    public virtual TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        rest = m_ParsedLength + length - m_Size;

        if (rest >= 0)
        {
            var requestInfo = ProcessMatchedRequest(readBuffer, offset - m_ParsedLength, m_Size, toBeCopied);
            InternalReset();
            return requestInfo;
        }
        else
        {
            m_ParsedLength += length;
            m_OffsetDelta = m_ParsedLength;
            rest = 0;

            var expectedOffset = offset + length;
            var newOffset = m_OrigOffset + m_OffsetDelta;

            if (newOffset < expectedOffset)
            {
                Buffer.BlockCopy(readBuffer, offset - m_ParsedLength + length, readBuffer, m_OrigOffset, m_ParsedLength);
            }

            return NullRequestInfo;
        }
    }

    /// <summary>
    /// Filters received data using ReadOnlySpan for better performance.
    /// </summary>
    /// <param name="buffer">The receive buffer as ReadOnlySpan.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <param name="rest">The rest, the length of the data which hasn't been parsed.</param>
    /// <returns></returns>
    public virtual TRequestInfo? Filter(ReadOnlySpan<byte> buffer, bool toBeCopied, out int rest)
    {
        rest = m_ParsedLength + buffer.Length - m_Size;

        if (rest >= 0)
        {
            var requestInfo = ProcessMatchedRequest(buffer.Slice(0, m_Size), toBeCopied);
            InternalReset();
            return requestInfo;
        }
        else
        {
            m_ParsedLength += buffer.Length;
            m_OffsetDelta = m_ParsedLength;
            rest = 0;

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

        if (buffer.Length < m_Size)
        {
            m_ParsedLength = ToInt32BufferSize(buffer.Length);
            m_OffsetDelta = 0;
            return NullRequestInfo;
        }

        var matched = buffer.Slice(0, m_Size);
        var requestInfo = matched.IsSingleSegment
            ? ProcessMatchedRequest(matched.First.Span, false)
            : ProcessMatchedRequest(matched.ToArray(), 0, m_Size, false);
        consumed = buffer.GetPosition(m_Size);
        examined = consumed;
        InternalReset();
        return requestInfo;
    }

    /// <summary>
    /// Filters the buffer after the server receive the enough size of data.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <returns></returns>
    protected abstract TRequestInfo? ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied);

    /// <summary>
    /// Filters the buffer using ReadOnlySpan after the server receives enough data.
    /// Default implementation converts span to array and calls the byte[] version.
    /// Override for zero-allocation processing.
    /// </summary>
    /// <param name="buffer">The buffer as ReadOnlySpan.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <returns></returns>
    protected virtual TRequestInfo? ProcessMatchedRequest(ReadOnlySpan<byte> buffer, bool toBeCopied)
    {
        return ProcessMatchedRequest(buffer.ToArray(), 0, buffer.Length, toBeCopied);
    }

    /// <summary>
    /// Gets the size of the rest buffer.
    /// </summary>
    /// <value>
    /// The size of the rest buffer.
    /// </value>
    public virtual int LeftBufferSize
    {
        get { return m_ParsedLength; }
    }

    /// <summary>
    /// Gets the next Receive filter.
    /// </summary>
    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter
    {
        get { return null; }
    }


    private int m_OffsetDelta;

    /// <summary>
    /// Gets the offset delta.
    /// </summary>
    int IOffsetAdapter.OffsetDelta
    {
        get { return m_OffsetDelta; }
    }

    /// <summary>
    /// Gets the filter state.
    /// </summary>
    /// <value>
    /// The filter state.
    /// </value>
    public FilterState State { get; protected set; }

    private void InternalReset()
    {
        m_ParsedLength = 0;
        m_OffsetDelta = 0;
    }

    /// <summary>
    /// Resets this instance.
    /// </summary>
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
