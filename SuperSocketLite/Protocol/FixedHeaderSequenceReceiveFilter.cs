using System;
using System.Buffers;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// Fixed-header receive filter for the Pipelines receive path.
/// The filter leaves incomplete data unconsumed in the PipeReader instead of copying it into a carry buffer.
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class FixedHeaderSequenceReceiveFilter<TRequestInfo> : ISequenceReceiveFilter<TRequestInfo>, IReceiveFilterInitializer
    where TRequestInfo : IRequestInfo
{
    private readonly int m_HeaderSize;
    private int m_LeftBufferSize;
    private int m_MaxRequestLength = int.MaxValue;
    private long m_LastConsumedLength;
    private byte[]? m_LegacyBuffer;
    private int m_LegacyBufferLength;

    protected FixedHeaderSequenceReceiveFilter(int headerSize)
    {
        if (headerSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(headerSize));

        m_HeaderSize = headerSize;
    }

    public int LeftBufferSize => m_LeftBufferSize;

    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;

    public FilterState State { get; protected set; }

    protected int HeaderSize => m_HeaderSize;

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        m_MaxRequestLength = session.Config.MaxRequestLength;
    }

    public TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;
        m_LastConsumedLength = 0;

        if (buffer.Length < m_HeaderSize)
        {
            m_LeftBufferSize = ToInt32BufferSize(buffer.Length);
            return default;
        }

        var header = buffer.Slice(0, m_HeaderSize);
        var bodyLength = GetBodyLengthFromHeader(header);

        if (!ValidateBodyLength(bodyLength))
        {
            State = FilterState.Error;
            m_LeftBufferSize = ToInt32BufferSize(buffer.Length);
            return default;
        }

        var requestLength = m_HeaderSize + bodyLength;

        if (buffer.Length < requestLength)
        {
            m_LeftBufferSize = ToInt32BufferSize(buffer.Length);
            return default;
        }

        consumed = buffer.GetPosition(requestLength);
        examined = consumed;
        m_LastConsumedLength = requestLength;
        m_LeftBufferSize = 0;

        var body = bodyLength == 0
            ? ReadOnlySequence<byte>.Empty
            : buffer.Slice(m_HeaderSize, bodyLength);

        return ResolveRequestInfo(header, body);
    }

    public TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        var previousLength = m_LegacyBufferLength;
        var totalLength = checked(previousLength + length);
        ReadOnlySequence<byte> sequence;

        if (previousLength == 0)
        {
            sequence = new ReadOnlySequence<byte>(new ReadOnlyMemory<byte>(readBuffer, offset, length));
        }
        else
        {
            EnsureLegacyBufferCapacity(totalLength);
            Buffer.BlockCopy(readBuffer, offset, m_LegacyBuffer!, previousLength, length);
            m_LegacyBufferLength = totalLength;
            sequence = new ReadOnlySequence<byte>(new ReadOnlyMemory<byte>(m_LegacyBuffer, 0, totalLength));
        }

        var requestInfo = Filter(sequence, out _, out _);

        if (requestInfo == null)
        {
            rest = 0;

            if (State == FilterState.Error)
            {
                ClearLegacyBuffer();
                return default;
            }

            if (previousLength == 0)
            {
                EnsureLegacyBufferCapacity(length);
                Buffer.BlockCopy(readBuffer, offset, m_LegacyBuffer!, 0, length);
                m_LegacyBufferLength = length;
            }

            return default;
        }

        var consumedLength = checked((int)m_LastConsumedLength);
        var consumedFromCurrentBuffer = Math.Max(0, consumedLength - previousLength);
        rest = Math.Max(0, length - consumedFromCurrentBuffer);
        ClearLegacyBuffer();
        return requestInfo;
    }

    public virtual TRequestInfo? Filter(ReadOnlySpan<byte> buffer, bool toBeCopied, out int rest)
    {
        return Filter(buffer.ToArray(), 0, buffer.Length, toBeCopied, out rest);
    }

    public virtual void Reset()
    {
        State = FilterState.Normal;
        m_LeftBufferSize = 0;
        m_LastConsumedLength = 0;
        ClearLegacyBuffer();
    }

    protected abstract int GetBodyLengthFromHeader(ReadOnlySequence<byte> header);

    protected virtual bool ValidateBodyLength(int bodyLength)
    {
        if (bodyLength < 0)
            return false;

        return m_MaxRequestLength <= 0 || m_HeaderSize + bodyLength < m_MaxRequestLength;
    }

    protected abstract TRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body);

    private static int ToInt32BufferSize(long length)
    {
        return length > int.MaxValue ? int.MaxValue : (int)length;
    }

    private void EnsureLegacyBufferCapacity(int requiredLength)
    {
        if (m_LegacyBuffer != null && m_LegacyBuffer.Length >= requiredLength)
            return;

        var newLength = Math.Max(requiredLength, m_HeaderSize * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newLength);

        if (m_LegacyBuffer != null)
        {
            if (m_LegacyBufferLength > 0)
                Buffer.BlockCopy(m_LegacyBuffer, 0, newBuffer, 0, m_LegacyBufferLength);

            ArrayPool<byte>.Shared.Return(m_LegacyBuffer);
        }

        m_LegacyBuffer = newBuffer;
    }

    private void ClearLegacyBuffer()
    {
        if (m_LegacyBuffer == null)
            return;

        ArrayPool<byte>.Shared.Return(m_LegacyBuffer);
        m_LegacyBuffer = null;
        m_LegacyBufferLength = 0;
    }
}
