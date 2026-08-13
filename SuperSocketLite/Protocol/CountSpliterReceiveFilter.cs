using System.Buffers;
using System.Text;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// This Receive filter is designed for this kind protocol:
/// each request has fixed count part which splited by a char(byte)
/// for instance, request is defined like this "#12122#23343#4545456565#343435446#",
/// because this request is splited into many parts by 5 '#', we can create a Receive filter by CountSpliterRequestFilter((byte)'#', 5)
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class CountSpliterReceiveFilter<TRequestInfo> : IReceiveFilter<TRequestInfo>, IOffsetAdapter, ISequenceReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    private int _total;

    private int _spliterFoundCount;

    private readonly byte _spliter;

    private readonly int _spliterCount;

    /// <summary>Null request info instance</summary>
    protected static readonly TRequestInfo? NullRequestInfo = default(TRequestInfo);

    protected CountSpliterReceiveFilter(byte spliter, int spliterCount)
    {
        _spliter = spliter;
        _spliterCount = spliterCount;
    }

    /// <summary>Filters the specified session.</summary>
    public TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        int parsedLen = 0;

        for (int i = 0; i < length; i++)
        {
            if(readBuffer[offset + i] == _spliter)
            {
                _spliterFoundCount++;

                if(_spliterFoundCount == _spliterCount)
                {
                    parsedLen = i + 1;
                    break;
                }
            }
        }

        //Not found enougth spliter
        if(parsedLen == 0)
        {
            //Move current requestInfo's offset to orginal offset
            if (OffsetDelta != _total)
            {
                Buffer.BlockCopy(readBuffer, offset - _total, readBuffer, offset - OffsetDelta, _total + length);

                _total += length;
                OffsetDelta = _total;
            }
            else
            {
                _total += length;
                OffsetDelta += length;
            }
            
            rest = 0;
            return NullRequestInfo;
        }

        rest = length - parsedLen;
        var finalTotal = _total + parsedLen;

        var requestInfo = ProcessMatchedRequest(readBuffer, offset - _total, finalTotal);

        InternalReset();

        if (rest == 0)
        {
            OffsetDelta = 0;
        }
        else
        {
            OffsetDelta += parsedLen;
        }

        return requestInfo;
    }

    /// <summary>Zero-copy parse straight from the receive pipe.</summary>
    /// <param name="buffer">The received data available from PipeReader.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="examined">The position up to which data was examined.</param>
    /// <returns>The parsed request, or null before the configured number of spliters has arrived.</returns>
    /// <remarks>
    /// The matched request spans from the start of the buffer through the last spliter, inclusive,
    /// exactly like the byte[] overload. This path is only used when the server has no
    /// RawDataReceived handler registered.
    /// </remarks>
    TRequestInfo? ISequenceReceiveFilter<TRequestInfo>.Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        var reader = new SequenceReader<byte>(buffer);
        var found = 0;

        while (found < _spliterCount && reader.TryAdvanceTo(_spliter, advancePastDelimiter: true))
            found++;

        if (found < _spliterCount)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            return NullRequestInfo;
        }

        var requestEnd = reader.Position;
        consumed = requestEnd;
        examined = requestEnd;

        var data = SequenceFilterHelper.AsArraySegment(buffer.Slice(buffer.Start, requestEnd));
        return ProcessMatchedRequest(data.Array!, data.Offset, data.Count);
    }

    /// <summary>Processes the matched request.</summary>
    protected abstract TRequestInfo? ProcessMatchedRequest(byte[] readBuffer, int offset, int length);

    /// <summary>Gets the size of the rest buffer.</summary>
    public int LeftBufferSize => _total;

    /// <summary>Gets the next Receive filter.</summary>
    public IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;

    private void InternalReset()
    {
        _total = 0;
        _spliterFoundCount = 0;
    }

    /// <summary>Resets this instance.</summary>
    public void Reset()
    {
        InternalReset();
        OffsetDelta = 0;
    }

    /// <summary>Gets the offset delta relative original receiving offset which will be used for next round receiving.</summary>
    public int OffsetDelta { get; private set; }

    /// <summary>Gets the filter state.</summary>
    public FilterState State { get; protected set; }
}

/// <summary>
/// This Receive filter is designed for this kind protocol:
/// each request has fixed count part which splited by a char(byte)
/// for instance, request is defined like this "#12122#23343#4545456565#343435446#",
/// because this request is splited into many parts by 5 '#', we can create a Receive filter by CountSpliterRequestFilter((byte)'#', 5)
/// </summary>
public class CountSpliterReceiveFilter : CountSpliterReceiveFilter<StringRequestInfo>
{
    private readonly Encoding _encoding;

    private readonly int _keyIndex;

    private readonly char _spliter;

    public CountSpliterReceiveFilter(byte spliter, int spliterCount)
        : this(spliter, spliterCount, Encoding.ASCII)
    {
        
    }

    public CountSpliterReceiveFilter(byte spliter, int spliterCount, Encoding encoding)
        : this(spliter, spliterCount, encoding, 0)
    {

    }

    public CountSpliterReceiveFilter(byte spliter, int spliterCount, Encoding encoding, int keyIndex)
        : base(spliter, spliterCount)
    {
        _encoding = encoding;
        _keyIndex = keyIndex;
        _spliter = (char)spliter;
    }

    /// <summary>Processes the matched request.</summary>
    protected override StringRequestInfo? ProcessMatchedRequest(byte[] readBuffer, int offset, int length)
    {
        //ignore the first and the last spliter
        var body = _encoding.GetString(readBuffer, offset + 1, length - 2);
        var array = body.Split(_spliter);
        return new StringRequestInfo(array[_keyIndex], body, array);
    }
}
