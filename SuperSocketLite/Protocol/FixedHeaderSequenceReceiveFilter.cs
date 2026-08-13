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
    private readonly int _headerSize;
    private int _leftBufferSize;
    private int _maxRequestLength = int.MaxValue;
    private long _lastConsumedLength;
    private byte[]? _legacyBuffer;
    private int _legacyBufferLength;

    protected FixedHeaderSequenceReceiveFilter(int headerSize)
    {
        if (headerSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(headerSize));

        _headerSize = headerSize;
    }

    public int LeftBufferSize => _leftBufferSize;

    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;

    public FilterState State { get; protected set; }

    protected int HeaderSize => _headerSize;

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        _maxRequestLength = session.Config.MaxRequestLength;
    }

    public TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;
        _lastConsumedLength = 0;

        if (buffer.Length < _headerSize)
        {
            _leftBufferSize = SequenceFilterHelper.ToInt32BufferSize(buffer.Length);
            return default;
        }

        var header = buffer.Slice(0, _headerSize);
        var bodyLength = GetBodyLengthFromHeader(header);

        if (!ValidateBodyLength(bodyLength))
        {
            State = FilterState.Error;
            _leftBufferSize = SequenceFilterHelper.ToInt32BufferSize(buffer.Length);
            return default;
        }

        var requestLength = _headerSize + bodyLength;

        if (buffer.Length < requestLength)
        {
            _leftBufferSize = SequenceFilterHelper.ToInt32BufferSize(buffer.Length);
            return default;
        }

        consumed = buffer.GetPosition(requestLength);
        examined = consumed;
        _lastConsumedLength = requestLength;
        _leftBufferSize = 0;

        var body = bodyLength == 0
            ? ReadOnlySequence<byte>.Empty
            : buffer.Slice(_headerSize, bodyLength);

        return ResolveRequestInfo(header, body);
    }

    public TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        var previousLength = _legacyBufferLength;
        var totalLength = checked(previousLength + length);
        ReadOnlySequence<byte> sequence;

        if (previousLength == 0)
        {
            sequence = new ReadOnlySequence<byte>(new ReadOnlyMemory<byte>(readBuffer, offset, length));
        }
        else
        {
            EnsureLegacyBufferCapacity(totalLength);
            Buffer.BlockCopy(readBuffer, offset, _legacyBuffer!, previousLength, length);
            _legacyBufferLength = totalLength;
            sequence = new ReadOnlySequence<byte>(new ReadOnlyMemory<byte>(_legacyBuffer, 0, totalLength));
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
                Buffer.BlockCopy(readBuffer, offset, _legacyBuffer!, 0, length);
                _legacyBufferLength = length;
            }

            return default;
        }

        var consumedLength = checked((int)_lastConsumedLength);
        var consumedFromCurrentBuffer = Math.Max(0, consumedLength - previousLength);
        rest = Math.Max(0, length - consumedFromCurrentBuffer);
        ClearLegacyBuffer();
        return requestInfo;
    }

    public virtual void Reset()
    {
        State = FilterState.Normal;
        _leftBufferSize = 0;
        _lastConsumedLength = 0;
        ClearLegacyBuffer();
    }

    protected abstract int GetBodyLengthFromHeader(ReadOnlySequence<byte> header);

    protected virtual bool ValidateBodyLength(int bodyLength)
    {
        if (bodyLength < 0)
            return false;

        return _maxRequestLength <= 0 || _headerSize + bodyLength < _maxRequestLength;
    }

    protected abstract TRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body);

    private void EnsureLegacyBufferCapacity(int requiredLength)
    {
        if (_legacyBuffer != null && _legacyBuffer.Length >= requiredLength)
            return;

        var newLength = Math.Max(requiredLength, _headerSize * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newLength);

        if (_legacyBuffer != null)
        {
            if (_legacyBufferLength > 0)
                Buffer.BlockCopy(_legacyBuffer, 0, newBuffer, 0, _legacyBufferLength);

            ArrayPool<byte>.Shared.Return(_legacyBuffer);
        }

        _legacyBuffer = newBuffer;
    }

    private void ClearLegacyBuffer()
    {
        if (_legacyBuffer == null)
            return;

        ArrayPool<byte>.Shared.Return(_legacyBuffer);
        _legacyBuffer = null;
        _legacyBufferLength = 0;
    }
}
