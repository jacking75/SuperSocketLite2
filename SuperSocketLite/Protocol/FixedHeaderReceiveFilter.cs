using System.Buffers;
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
    private bool _foundHeader = false;

    private ArraySegment<byte> _header;

    private int _bodyLength;

    private int _maxRequestLength = int.MaxValue;

    private int _sequenceLeftBufferSize;

    private ArraySegmentList _bodyBuffer = null!;

    protected FixedHeaderReceiveFilter(int headerSize)
        : base(headerSize)
    {

    }

    /// <summary>Gets the buffered request size including a parsed header and any accumulated body bytes.</summary>
    public override int LeftBufferSize
    {
        get
        {
            if (_sequenceLeftBufferSize > 0)
                return _sequenceLeftBufferSize;

            if (!_foundHeader)
                return base.LeftBufferSize;

            return Size + (_bodyBuffer?.Count ?? 0);
        }
    }

    /// <summary>Called after the filter is initialized for a session.</summary>
    protected override void OnInitialized(IAppServer appServer, IAppSession session)
    {
        _maxRequestLength = session.Config.MaxRequestLength;
    }

    /// <summary>Filters the specified session.</summary>
    public override TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        if (!_foundHeader)
            return base.Filter(readBuffer, offset, length, toBeCopied, out rest);

        if (_bodyBuffer == null || _bodyBuffer.Count == 0)
        {
            if (length < _bodyLength)
            {
                if (_bodyBuffer == null)
                    _bodyBuffer = new ArraySegmentList();

                _bodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                return NullRequestInfo;
            }
            else if (length == _bodyLength)
            {
                rest = 0;
                _foundHeader = false;
                return ResolveRequestInfo(_header, readBuffer, offset, length);
            }
            else
            {
                rest = length - _bodyLength;
                _foundHeader = false;
                return ResolveRequestInfo(_header, readBuffer, offset, _bodyLength);
            }
        }
        else
        {
            int required = _bodyLength - _bodyBuffer.Count;

            if (length < required)
            {
                _bodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                return NullRequestInfo;
            }
            else if (length == required)
            {
                _bodyBuffer.AddSegment(readBuffer, offset, length, toBeCopied);
                rest = 0;
                _foundHeader = false;
                var requestInfo = ResolveRequestInfo(_header, _bodyBuffer.ToArrayData());
                _bodyBuffer.ClearSegements();
                return requestInfo;
            }
            else
            {
                _bodyBuffer.AddSegment(readBuffer, offset, required, toBeCopied);
                rest = length - required;
                _foundHeader = false;
                var requestInfo = ResolveRequestInfo(_header, _bodyBuffer.ToArrayData(0, _bodyLength));
                _bodyBuffer.ClearSegements();
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
            _sequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        var header = buffer.Slice(0, Size);
        var bodyLength = GetBodyLengthFromHeader(header.IsSingleSegment ? header.First.Span : header.ToArray());

        if (!ValidateBodyLength(bodyLength))
        {
            State = FilterState.Error;
            _sequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        var requestLength = Size + bodyLength;

        if (buffer.Length < requestLength)
        {
            _sequenceLeftBufferSize = ToInt32BufferSize(buffer.Length);
            return NullRequestInfo;
        }

        consumed = buffer.GetPosition(requestLength);
        examined = consumed;
        _sequenceLeftBufferSize = 0;

        var headerSegment = new ArraySegment<byte>(header.ToArray());

        if (bodyLength == 0)
            return ResolveRequestInfo(headerSegment, null, 0, 0);

        var body = buffer.Slice(Size, bodyLength).ToArray();
        return ResolveRequestInfo(headerSegment, body, 0, body.Length);
    }

    /// <summary>Processes the fix size request.</summary>
    protected override TRequestInfo? ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied)
    {
        _foundHeader = true;

        _bodyLength = GetBodyLengthFromHeader(buffer, offset, Size);

        if (!ValidateBodyLength(_bodyLength))
        {
            State = FilterState.Error;
            _foundHeader = false;
            return NullRequestInfo;
        }

        if (toBeCopied)
            _header = new ArraySegment<byte>(buffer.CloneRange(offset, Size));
        else
            _header = new ArraySegment<byte>(buffer, offset, Size);

        if (_bodyLength > 0)
            return NullRequestInfo;

        _foundHeader = false;
        return ResolveRequestInfo(_header, null, 0, 0);//Empty body
    }

    /// <summary>Processes the fix size request using ReadOnlySpan.</summary>
    /// <param name="buffer">The buffer as ReadOnlySpan.</param>
    protected override TRequestInfo? ProcessMatchedRequest(ReadOnlySpan<byte> buffer, bool toBeCopied)
    {
        _foundHeader = true;

        _bodyLength = GetBodyLengthFromHeader(buffer);

        if (!ValidateBodyLength(_bodyLength))
        {
            State = FilterState.Error;
            _foundHeader = false;
            return NullRequestInfo;
        }

        //ReadOnlySpan cannot outlive this call, so the header is always copied out.
        _header = new ArraySegment<byte>(buffer.Slice(0, Size).ToArray());

        if (_bodyLength > 0)
            return NullRequestInfo;

        _foundHeader = false;
        return ResolveRequestInfo(_header, null, 0, 0);//Empty body
    }

    private TRequestInfo? ResolveRequestInfo(ArraySegment<byte> header, byte[] bodyBuffer)
    {
        return ResolveRequestInfo(header, bodyBuffer, 0, bodyBuffer.Length);
    }

    /// <summary>Gets the body length from header.</summary>
    protected abstract int GetBodyLengthFromHeader(byte[] header, int offset, int length);

    /// <summary>
    /// Gets the body length from header using ReadOnlySpan.
    /// Default implementation converts to array and calls the byte[] version.
    /// </summary>
    /// <param name="header">The header as ReadOnlySpan.</param>
    protected virtual int GetBodyLengthFromHeader(ReadOnlySpan<byte> header)
    {
        return GetBodyLengthFromHeader(header.ToArray(), 0, header.Length);
    }

    /// <summary>Validates the body length before body bytes are accumulated.</summary>
    protected virtual bool ValidateBodyLength(int bodyLength)
    {
        if (bodyLength < 0)
            return false;

        return _maxRequestLength <= 0 || Size + bodyLength < _maxRequestLength;
    }

    /// <summary>Resolves the request data.</summary>
    protected abstract TRequestInfo? ResolveRequestInfo(ArraySegment<byte> header, byte[]? bodyBuffer, int offset, int length);

    /// <summary>Resets this instance.</summary>
    public override void Reset()
    {
        base.Reset();
        _foundHeader = false;
        _bodyLength = 0;
        _sequenceLeftBufferSize = 0;
        _bodyBuffer?.ClearSegements();
    }

    private static int ToInt32BufferSize(long length)
    {
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }
}
