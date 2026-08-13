using SuperSocketLite.Common;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Receive filter base class</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class ReceiveFilterBase<TRequestInfo> : IReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    private ArraySegmentList _bufferSegments = null!;

    /// <summary>Gets the buffer segments which can help you parse your request info conviniently.</summary>
    protected ArraySegmentList BufferSegments => _bufferSegments;

    protected ReceiveFilterBase()
    {
        _bufferSegments = new ArraySegmentList();
    }

    /// <param name="previousRequestFilter">The previous Receive filter.</param>
    protected ReceiveFilterBase(ReceiveFilterBase<TRequestInfo> previousRequestFilter)
    {
        Initialize(previousRequestFilter);
    }

    /// <summary>Initializes the specified previous Receive filter.</summary>
    /// <param name="previousRequestFilter">The previous Receive filter.</param>
    public void Initialize(ReceiveFilterBase<TRequestInfo> previousRequestFilter)
    {
        _bufferSegments = previousRequestFilter.BufferSegments;
    }

    


    /// <summary>Filters received data of the specific session into request info.</summary>
    /// <param name="offset">The offset of the current received data in this read buffer.</param>
    /// <param name="length">The length of the current received data.</param>
    /// <param name="rest">The rest, the length of the data which hasn't been parsed.</param>
    public abstract TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest);

    /// <summary>Gets the size of the rest buffer.</summary>
    public int LeftBufferSize => _bufferSegments.Count;

    /// <summary>Gets or sets the next Receive filter.</summary>
    public IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; protected set; }

    

    /// <summary>Adds the array segment.</summary>
    protected void AddArraySegment(byte[] buffer, int offset, int length, bool toBeCopied)
    {
        _bufferSegments.AddSegment(buffer, offset, length, toBeCopied);
    }

    /// <summary>Clears the buffer segments.</summary>
    protected void ClearBufferSegments()
    {
        _bufferSegments.ClearSegements();
    }

    /// <summary>Resets this instance to initial state.</summary>
    public virtual void Reset()
    {
        if(_bufferSegments != null && _bufferSegments.Count > 0)
            _bufferSegments.ClearSegements();
    }

    /// <summary>Gets the filter state.</summary>
    public FilterState State { get; protected set; }
}
