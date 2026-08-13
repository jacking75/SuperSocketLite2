using System;
using System.Buffers;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// ReceiveFilter for the protocol that each request has bengin and end mark
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class BeginEndMarkReceiveFilter<TRequestInfo> : ReceiveFilterBase<TRequestInfo>, ISequenceReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    private readonly SearchMarkState<byte> m_BeginSearchState;
    private readonly SearchMarkState<byte> m_EndSearchState;

    private bool m_FoundBegin = false;

    /// <summary>
    /// Null request info
    /// </summary>
    protected TRequestInfo? NullRequestInfo = default(TRequestInfo);

    /// <summary>
    /// Initializes a new instance of the <see cref="BeginEndMarkReceiveFilter&lt;TRequestInfo&gt;"/> class.
    /// </summary>
    /// <param name="beginMark">The begin mark.</param>
    /// <param name="endMark">The end mark.</param>
    protected BeginEndMarkReceiveFilter(byte[] beginMark, byte[] endMark)
    {
        m_BeginSearchState = new SearchMarkState<byte>(beginMark);
        m_EndSearchState = new SearchMarkState<byte>(endMark);
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
        rest = 0;

        int searchEndMarkOffset;
        int searchEndMarkLength;

        //prev macthed begin mark length
        int prevMatched = 0;
        int totalParsed = 0;

        if (!m_FoundBegin)
        {
            prevMatched = m_BeginSearchState.Matched;
            int pos = readBuffer.SearchMark(offset, length, m_BeginSearchState, out totalParsed);
            
            if (pos < 0)
            {
                //Don't cache invalid data
                if (prevMatched > 0 || (m_BeginSearchState.Matched > 0 && length != m_BeginSearchState.Matched))
                {
                    State = FilterState.Error;
                    return NullRequestInfo;
                }

                return NullRequestInfo;
            }
            else //Found the matched begin mark
            {
                //But not at the beginning
                if(pos != offset)
                {
                    State = FilterState.Error;
                    return NullRequestInfo;
                }
            }

            //Found start mark
            m_FoundBegin = true;

            searchEndMarkOffset = pos + m_BeginSearchState.Mark.Length - prevMatched;

            //This block only contain (part of)begin mark
            if (offset + length <= searchEndMarkOffset)
            {
                AddArraySegment(m_BeginSearchState.Mark, 0, m_BeginSearchState.Mark.Length, false);
                return NullRequestInfo;
            }

            searchEndMarkLength = offset + length - searchEndMarkOffset;
        }
        else//Already found begin mark
        {
            searchEndMarkOffset = offset;
            searchEndMarkLength = length;
        }

        while (true)
        {
            var prevEndMarkMatched = m_EndSearchState.Matched;
            var parsedLen = 0;
            var endPos = readBuffer.SearchMark(searchEndMarkOffset, searchEndMarkLength, m_EndSearchState, out parsedLen);

            //Haven't found end mark
            if (endPos < 0)
            {
                rest = 0;
                if(prevMatched > 0)//Also cache the prev matched begin mark
                    AddArraySegment(m_BeginSearchState.Mark, 0, prevMatched, false);
                AddArraySegment(readBuffer, offset, length, toBeCopied);
                return NullRequestInfo;
            }

            totalParsed += parsedLen;
            rest = length - totalParsed;

            byte[] commandData = new byte[BufferSegments.Count + prevMatched + totalParsed];

            if (BufferSegments.Count > 0)
                BufferSegments.CopyTo(commandData, 0, 0, BufferSegments.Count);

            if(prevMatched > 0)
                Array.Copy(m_BeginSearchState.Mark, 0, commandData, BufferSegments.Count, prevMatched);

            Array.Copy(readBuffer, offset, commandData, BufferSegments.Count + prevMatched, totalParsed);

            var requestInfo = ProcessMatchedRequest(commandData, 0, commandData.Length);

            if (!ReferenceEquals(requestInfo, NullRequestInfo))
            {
                Reset();
                return requestInfo;
            }

            if (rest > 0)
            {
                searchEndMarkOffset = endPos + m_EndSearchState.Mark.Length;
                searchEndMarkLength = rest;
                continue;
            }

            //Not match
            if(prevMatched > 0)//Also cache the prev matched begin mark
                AddArraySegment(m_BeginSearchState.Mark, 0, prevMatched, false);
            AddArraySegment(readBuffer, offset, length, toBeCopied);
            return NullRequestInfo;
        }
    }

    /// <summary>
    /// Zero-copy parse straight from the receive pipe.
    /// </summary>
    /// <param name="buffer">The received data available from PipeReader.</param>
    /// <param name="consumed">The position up to which data was consumed.</param>
    /// <param name="examined">The position up to which data was examined.</param>
    /// <returns>The parsed request, or null when the request is not complete yet.</returns>
    /// <remarks>
    /// As in the byte[] overload the matched request includes both the begin and the end mark, and
    /// data that does not start with the begin mark puts the filter into
    /// <see cref="FilterState.Error"/>. This path is only used when the server has no
    /// RawDataReceived handler registered.
    /// </remarks>
    TRequestInfo? ISequenceReceiveFilter<TRequestInfo>.Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        var beginMark = m_BeginSearchState.Mark;
        var comparableLength = (int)Math.Min(buffer.Length, beginMark.Length);

        //The begin mark must sit at the very start of the request; a mismatch is fatal even when
        //only part of it has arrived.
        if (!SequenceFilterHelper.StartsWith(buffer, beginMark, comparableLength))
        {
            State = FilterState.Error;
            return NullRequestInfo;
        }

        if (buffer.Length < beginMark.Length)
            return NullRequestInfo;

        var reader = new SequenceReader<byte>(buffer);
        reader.Advance(beginMark.Length);

        var endMark = m_EndSearchState.Mark;

        //ProcessMatchedRequest may reject a match (an end mark that appears inside the body), in
        //which case the byte[] overload keeps looking for the next one - mirror that here.
        while (reader.TryReadTo(out ReadOnlySequence<byte> _, endMark, advancePastDelimiter: true))
        {
            var requestEnd = reader.Position;
            var data = SequenceFilterHelper.AsArraySegment(buffer.Slice(buffer.Start, requestEnd));
            var requestInfo = ProcessMatchedRequest(data.Array!, data.Offset, data.Count);

            if (!ReferenceEquals(requestInfo, NullRequestInfo))
            {
                consumed = requestEnd;
                examined = requestEnd;
                Reset();
                return requestInfo;
            }
        }

        return NullRequestInfo;
    }

    /// <summary>
    /// Processes the matched request.
    /// </summary>
    /// <param name="readBuffer">The read buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns></returns>
    protected abstract TRequestInfo? ProcessMatchedRequest(byte[] readBuffer, int offset, int length);

    /// <summary>
    /// Resets this instance.
    /// </summary>
    public override void Reset()
    {
        m_BeginSearchState.Matched = 0;
        m_EndSearchState.Matched = 0;
        m_FoundBegin = false;
        base.Reset();
    }
}
