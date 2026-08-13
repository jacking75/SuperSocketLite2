using System.Buffers;
using System.Text;
using SuperSocketLite.Common;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Terminator Receive filter</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class TerminatorReceiveFilter<TRequestInfo> : ReceiveFilterBase<TRequestInfo>, IOffsetAdapter, IReceiveFilterInitializer, ISequenceReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    private readonly SearchMarkState<byte> _searchState;

    private IAppSession? _session;

    /// <summary>Gets the session assosiated with the Receive filter.</summary>
    protected IAppSession? Session => _session;

    /// <summary>Null RequestInfo</summary>
    protected static readonly TRequestInfo? NullRequestInfo = default(TRequestInfo);

    private int _parsedLengthInBuffer = 0;

    protected TerminatorReceiveFilter(byte[] terminator)
    {
        _searchState = new SearchMarkState<byte>(terminator);
    }

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        _session = session;
    }

    /// <summary>Filters received data of the specific session into request info.</summary>
    /// <param name="offset">The offset of the current received data in this read buffer.</param>
    /// <param name="length">The length of the current received data.</param>
    /// <param name="rest">The rest, the length of the data which hasn't been parsed.</param>
    /// <returns>return the parsed TRequestInfo</returns>
    public override TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        rest = 0;

        int prevMatched = _searchState.Matched;

        int result = readBuffer.SearchMark(offset, length, _searchState);

        if (result < 0)
        {
            if (_offsetDelta != _parsedLengthInBuffer)
            {
                Buffer.BlockCopy(readBuffer, offset - _parsedLengthInBuffer, readBuffer, offset - _offsetDelta, _parsedLengthInBuffer + length);

                _parsedLengthInBuffer += length;
                _offsetDelta = _parsedLengthInBuffer;
            }
            else
            {
                _parsedLengthInBuffer += length;

                if (_parsedLengthInBuffer >= _session!.Config.ReceiveBufferSize)
                {
                    this.AddArraySegment(readBuffer, offset + length - _parsedLengthInBuffer, _parsedLengthInBuffer, toBeCopied);
                    _parsedLengthInBuffer = 0;
                    _offsetDelta = 0;

                    return NullRequestInfo;
                }

                _offsetDelta += length;
            }

            return NullRequestInfo;
        }

        var findLen = result - offset;
        var currentMatched = _searchState.Mark.Length - prevMatched;

        //The prev matched part is not belong to the current matched terminator mark
        if (prevMatched > 0 && findLen != 0)
        {
            //rest prevMatched to 0
            prevMatched = 0;
            currentMatched = _searchState.Mark.Length;
        }

        rest = length - findLen - currentMatched;

        TRequestInfo? requestInfo;

        if (findLen > 0)
        {
            if(this.BufferSegments != null && this.BufferSegments.Count > 0)
            {
                this.AddArraySegment(readBuffer, offset - _parsedLengthInBuffer, findLen + _parsedLengthInBuffer, toBeCopied);
                requestInfo = ProcessMatchedRequest(BufferSegments, 0, BufferSegments.Count);
            }
            else
            {
                requestInfo = ProcessMatchedRequest(readBuffer, offset - _parsedLengthInBuffer, findLen + _parsedLengthInBuffer);
            }
        }
        else if (prevMatched > 0)
        {
            if (_parsedLengthInBuffer > 0)
            {
                if (_parsedLengthInBuffer < prevMatched)
                {
                    BufferSegments.TrimEnd(prevMatched - _parsedLengthInBuffer);
                    requestInfo = ProcessMatchedRequest(BufferSegments, 0, BufferSegments.Count);
                }
                else
                {
                    if (this.BufferSegments != null && this.BufferSegments.Count > 0)
                    {
                        this.AddArraySegment(readBuffer, offset - _parsedLengthInBuffer, _parsedLengthInBuffer - prevMatched, toBeCopied);
                        requestInfo = ProcessMatchedRequest(BufferSegments, 0, BufferSegments.Count);
                    }
                    else
                    {
                        requestInfo = ProcessMatchedRequest(readBuffer, offset - _parsedLengthInBuffer, _parsedLengthInBuffer - prevMatched);
                    }
                }
            }
            else
            {
                BufferSegments.TrimEnd(prevMatched);
                requestInfo = ProcessMatchedRequest(BufferSegments, 0, BufferSegments.Count);
            }
        }
        else
        {
            if (this.BufferSegments != null && this.BufferSegments.Count > 0)
            {
                if (_parsedLengthInBuffer > 0)
                {
                    this.BufferSegments.AddSegment(readBuffer, offset, _parsedLengthInBuffer);
                }

                requestInfo = ProcessMatchedRequest(BufferSegments, 0, BufferSegments.Count);
            }
            else
            {
                requestInfo = ProcessMatchedRequest(readBuffer, offset - _parsedLengthInBuffer, _parsedLengthInBuffer);
            }
        }

        InternalReset();

        if(rest == 0)
        {
            _offsetDelta = 0;
        }
        else
        {
            _offsetDelta += (length - rest);
        }

        return requestInfo;
    }

    /// <summary>
    /// Zero-copy parse straight from the receive pipe: an incomplete request stays in the pipe
    /// instead of being copied into the session's carry buffer on every read.
    /// </summary>
    /// <param name="buffer">The received data available from PipeReader.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="examined">The position up to which data was examined.</param>
    /// <returns>The parsed request, or null when the terminator has not arrived yet.</returns>
    /// <remarks>
    /// This path is only used when the server has no RawDataReceived handler registered; otherwise
    /// the session falls back to the byte[] overload.
    /// </remarks>
    TRequestInfo? ISequenceReceiveFilter<TRequestInfo>.Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        var reader = new SequenceReader<byte>(buffer);

        // TryReadTo handles a multi-byte terminator that straddles a segment boundary.
        if (!reader.TryReadTo(out ReadOnlySequence<byte> body, _searchState.Mark, advancePastDelimiter: true))
        {
            consumed = buffer.Start;
            examined = buffer.End;
            return NullRequestInfo;
        }

        consumed = reader.Position;
        examined = consumed;

        var data = SequenceFilterHelper.AsArraySegment(body);
        return ProcessMatchedRequest(data.Array!, data.Offset, data.Count);
    }

    private void InternalReset()
    {
        _parsedLengthInBuffer = 0;
        _searchState.Matched = 0;
        base.Reset();
    }

    /// <summary>Resets this instance.</summary>
    public override void Reset()
    {
        InternalReset();
        _offsetDelta = 0;
    }


    private TRequestInfo? ProcessMatchedRequest(ArraySegmentList data, int offset, int length)
    {
        var targetData = data.ToArrayData(offset, length);
        return ProcessMatchedRequest(targetData, 0, length);
    }

    /// <summary>Resolves the specified data to TRequestInfo.</summary>
    protected abstract TRequestInfo? ProcessMatchedRequest(byte[] data, int offset, int length);

    
    private int _offsetDelta;

    int IOffsetAdapter.OffsetDelta => _offsetDelta;
}

/// <summary>TerminatorRequestFilter</summary>
public class TerminatorReceiveFilter : TerminatorReceiveFilter<StringRequestInfo>
{
    private readonly Encoding _encoding;
    private readonly IRequestInfoParser<StringRequestInfo> _requestParser;

    public TerminatorReceiveFilter(byte[] terminator, Encoding encoding)
        : this(terminator, encoding, BasicRequestInfoParser.DefaultInstance)
    {
        
    }
    public TerminatorReceiveFilter(byte[] terminator, Encoding encoding, IRequestInfoParser<StringRequestInfo> requestParser)
        : base(terminator)
    {
        _encoding = encoding;
        _requestParser = requestParser;
    }

    /// <summary>Resolves the specified data to StringRequestInfo.</summary>
    protected override StringRequestInfo? ProcessMatchedRequest(byte[] data, int offset, int length)
    {
        if(length == 0)
            return _requestParser.ParseRequestInfo(string.Empty);

        return _requestParser.ParseRequestInfo(_encoding.GetString(data, offset, length));
    }
}
