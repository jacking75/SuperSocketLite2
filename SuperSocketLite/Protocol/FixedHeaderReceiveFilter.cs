using System.Buffers;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// Receive filter for a protocol with a fixed length header that carries the body length.
/// Implement <see cref="GetBodyLengthFromHeader"/> and <see cref="ResolveRequestInfo"/> to support
/// your own binary protocol.
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
/// <remarks>
/// An incomplete request is left in the receive pipe rather than accumulated in a buffer of the
/// filter's own, so a request split over many reads costs no copying at all.
/// </remarks>
public abstract class FixedHeaderReceiveFilter<TRequestInfo> : IReceiveFilter<TRequestInfo>, IReceiveFilterInitializer
    where TRequestInfo : IRequestInfo
{
    private int _maxRequestLength = int.MaxValue;

    /// <param name="headerSize">Size of the header.</param>
    protected FixedHeaderReceiveFilter(int headerSize)
    {
        if (headerSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(headerSize));

        HeaderSize = headerSize;
    }

    /// <summary>Gets the size of the fixed header.</summary>
    protected int HeaderSize { get; }

    /// <summary>Gets the next Receive filter.</summary>
    public virtual IReceiveFilter<TRequestInfo>? NextReceiveFilter => null;

    /// <summary>Gets the filter state.</summary>
    public FilterState State { get; protected set; }

    void IReceiveFilterInitializer.Initialize(IAppServer appServer, IAppSession session)
    {
        _maxRequestLength = session.Config.MaxRequestLength;
    }

    /// <summary>Filters one header-plus-body request out of the receive pipe.</summary>
    public TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length < HeaderSize)
            return default;

        var header = buffer.Slice(0, HeaderSize);
        var bodyLength = GetBodyLengthFromHeader(header);

        if (!ValidateBodyLength(bodyLength))
        {
            State = FilterState.Error;
            return default;
        }

        var requestLength = HeaderSize + bodyLength;

        if (buffer.Length < requestLength)
            return default;

        consumed = buffer.GetPosition(requestLength);
        examined = consumed;

        var body = bodyLength == 0
            ? ReadOnlySequence<byte>.Empty
            : buffer.Slice(HeaderSize, bodyLength);

        return ResolveRequestInfo(header, body);
    }

    /// <summary>Resets this instance.</summary>
    public virtual void Reset()
    {
        State = FilterState.Normal;
    }

    /// <summary>Reads the body length out of the header.</summary>
    /// <param name="header">Exactly <see cref="HeaderSize"/> bytes; it may span several pipe segments.</param>
    protected abstract int GetBodyLengthFromHeader(ReadOnlySequence<byte> header);

    /// <summary>
    /// Rejects a body length that is negative or would make the request exceed MaxRequestLength.
    /// Returning false puts the filter into <see cref="FilterState.Error"/> and closes the session.
    /// </summary>
    protected virtual bool ValidateBodyLength(int bodyLength)
    {
        if (bodyLength < 0)
            return false;

        return _maxRequestLength <= 0 || HeaderSize + bodyLength < _maxRequestLength;
    }

    /// <summary>Resolves the matched header and body into a request info.</summary>
    protected abstract TRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body);
}
