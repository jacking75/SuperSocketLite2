using System;
using System.Buffers;
using System.Text;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// FixedHeaderReceiveFilter,
/// it is the Receive filter base for the protocol which define fixed length header and the header contains the request body length,
/// you can implement your own Receive filter for this kind protocol easily by inheriting this class 
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class FixedHeaderReceiveFilter<TRequestInfo> : FixedSizeReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    private bool m_FoundHeader = false;

    private ArraySegment<byte> m_Header;

    private int m_BodyLength;

    private int m_MaxRequestLength = int.MaxValue;

    private int m_SequenceLeftBufferSize;

    private ArraySegmentList m_BodyBuffer = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedHeaderReceiveFilter&lt;TRequestInfo&gt;"/> class.
    /// </summary>
    /// <param name="headerSize">Size of the header.</param>
    protected FixedHeaderReceiveFilter(int headerSize)
        : base(headerSize)
    {

    }

    /// <summary>
    /// Gets the buffered request size including a parsed header and any accumulated body bytes.
    /// </summary>
    public override int LeftBufferSize
    {
        get
        {
            if (m_SequenceLeftBufferSize > 0)
                return m_SequenceLeftBufferSize;

            if (!m_FoundHeader)
                return base.LeftBufferSize;

            return Size + (m_BodyBuffer?.Count ?? 0);
        }
    }

    /// <summary>
    /// Called after the filter is initialized for a session.
    /// </summary>
    protected override void OnInitialized(IAppServer appServer, IAppSession session)
    {
        m_MaxRequestLength = session.Config.MaxRequestLength;
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
    public override TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        if (!m_FoundHeader)
            return base.Filter(readBuffer, offset, length, toBeCopied, out rest);

        if (m_BodyBuffer == null || m_BodyBuffer.Count == 0)
        {
            if (length < m_BodyLength)
            {
                if (m_BodyBuffer == null)
                    m_BodyBuffer = new ArraySegmentList();

                m_BodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                return NullRequestInfo;
            }
            else if (length == m_BodyLength)
            {
                rest = 0;
                m_FoundHeader = false;
                return ResolveRequestInfo(m_Header, readBuffer, offset, length);
            }
            else
            {
                rest = length - m_BodyLength;
                m_FoundHeader = false;
                return ResolveRequestInfo(m_Header, readBuffer, offset, m_BodyLength);
            }
        }
        else
        {
            int required = m_BodyLength - m_BodyBuffer.Count;

            if (length < required)
            {
                m_BodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                return NullRequestInfo;
            }
            else if (length == required)
            {
                m_BodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                m_FoundHeader = false;
                var requestInfo = ResolveRequestInfo(m_Header, m_BodyBuffer.ToArrayData());
                m_BodyBuffer.ClearSegements();
                return requestInfo;
            }
            else
            {
                m_BodyBuffer.AddSegment(readBuffer, offset, required, toBeCopied);
                rest = length - required;
                m_FoundHeader = false;
                var requestInfo = ResolveRequestInfo(m_Header, m_BodyBuffer.ToArrayData(0, m_BodyLength));
                m_BodyBuffer.ClearSegements();
                return requestInfo;
            }
        }
    }

    /// <summary>
    /// Filters a fixed-header request directly from the Pipelines sequence path.
    /// Incomplete requests are left unconsumed in the PipeReader.
    /// </summary>
    public override TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length < Size)
        {
            m_SequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        var header = buffer.Slice(0, Size);
        var bodyLength = GetBodyLengthFromHeader(header.IsSingleSegment ? header.First.Span : header.ToArray());

        if (!ValidateBodyLength(bodyLength))
        {
            State = FilterState.Error;
            m_SequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        var requestLength = Size + bodyLength;

        if (buffer.Length < requestLength)
        {
            m_SequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        consumed = buffer.GetPosition(requestLength);
        examined = consumed;
        m_SequenceLeftBufferSize = 0;

        var headerSegment = new ArraySegment<byte>(header.ToArray());

        if (bodyLength == 0)
            return ResolveRequestInfo(headerSegment, null, 0, 0);

        var body = buffer.Slice(Size, bodyLength).ToArray();
        return ResolveRequestInfo(headerSegment, body, 0, body.Length);
    }

    /// <summary>
    /// Processes the fix size request.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <returns></returns>
    protected override TRequestInfo? ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied)
    {
        m_FoundHeader = true;

        m_BodyLength = GetBodyLengthFromHeader(buffer, offset, Size);

        if (!ValidateBodyLength(m_BodyLength))
        {
            State = FilterState.Error;
            m_FoundHeader = false;
            return NullRequestInfo;
        }

        if (toBeCopied)
            m_Header = new ArraySegment<byte>(buffer.CloneRange(offset, Size));
        else
            m_Header = new ArraySegment<byte>(buffer, offset, Size);

        if (m_BodyLength > 0)
            return NullRequestInfo;

        m_FoundHeader = false;
        return ResolveRequestInfo(m_Header, null, 0, 0);//Empty body
    }

    /// <summary>
    /// Processes the fix size request using ReadOnlySpan.
    /// </summary>
    /// <param name="buffer">The buffer as ReadOnlySpan.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <returns></returns>
    protected override TRequestInfo? ProcessMatchedRequest(ReadOnlySpan<byte> buffer, bool toBeCopied)
    {
        m_FoundHeader = true;

        m_BodyLength = GetBodyLengthFromHeader(buffer);

        if (!ValidateBodyLength(m_BodyLength))
        {
            State = FilterState.Error;
            m_FoundHeader = false;
            return NullRequestInfo;
        }

        if (toBeCopied)
            m_Header = new ArraySegment<byte>(buffer.Slice(0, Size).ToArray());
        else
            m_Header = new ArraySegment<byte>(buffer.Slice(0, Size).ToArray());

        if (m_BodyLength > 0)
            return NullRequestInfo;

        m_FoundHeader = false;
        return ResolveRequestInfo(m_Header, null, 0, 0);//Empty body
    }

    private TRequestInfo? ResolveRequestInfo(ArraySegment<byte> header, byte[] bodyBuffer)
    {
        return ResolveRequestInfo(header, bodyBuffer, 0, bodyBuffer.Length);
    }

    /// <summary>
    /// Gets the body length from header.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns></returns>
    protected abstract int GetBodyLengthFromHeader(byte[] header, int offset, int length);

    /// <summary>
    /// Gets the body length from header using ReadOnlySpan.
    /// Default implementation converts to array and calls the byte[] version.
    /// </summary>
    /// <param name="header">The header as ReadOnlySpan.</param>
    /// <returns></returns>
    protected virtual int GetBodyLengthFromHeader(ReadOnlySpan<byte> header)
    {
        return GetBodyLengthFromHeader(header.ToArray(), 0, header.Length);
    }

    /// <summary>
    /// Validates the body length before body bytes are accumulated.
    /// </summary>
    protected virtual bool ValidateBodyLength(int bodyLength)
    {
        if (bodyLength < 0)
            return false;

        return m_MaxRequestLength <= 0 || Size + bodyLength < m_MaxRequestLength;
    }

    /// <summary>
    /// Resolves the request data.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <param name="bodyBuffer">The body buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns></returns>
    protected abstract TRequestInfo? ResolveRequestInfo(ArraySegment<byte> header, byte[]? bodyBuffer, int offset, int length);

    /// <summary>
    /// Resets this instance.
    /// </summary>
    public override void Reset()
    {
        base.Reset();
        m_FoundHeader = false;
        m_BodyLength = 0;
        m_SequenceLeftBufferSize = 0;
        m_BodyBuffer?.ClearSegements();
    }

    private static int ToInt32BufferSize(long length)
    {
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }
}
