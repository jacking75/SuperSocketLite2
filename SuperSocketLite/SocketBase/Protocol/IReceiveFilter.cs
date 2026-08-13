namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Receive filter interface</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface IReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>Filters received data of the specific session into request info.</summary>
    /// <param name="offset">The offset of the current received data in this read buffer.</param>
    /// <param name="length">The length of the current received data.</param>
    /// <param name="rest">The rest, the length of the data which hasn't been parsed.</param>
    TRequestInfo? Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest);

    /// <summary>Gets the size of the rest buffer.</summary>
    int LeftBufferSize { get; }

    /// <summary>Gets the next Receive filter.</summary>
    IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; }

    /// <summary>Resets this instance to initial state.</summary>
    void Reset();


    /// <summary>Gets the filter state.</summary>
    FilterState State { get; }
}
