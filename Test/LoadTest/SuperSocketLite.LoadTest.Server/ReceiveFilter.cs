using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketEngine.Protocol;

namespace SuperSocketLite.LoadTest.Server;

public sealed class ReceiveFilter : FixedHeaderReceiveFilter<LoadTestRequestInfo>
{
    public ReceiveFilter()
        : base(LoadTestRequestInfo.HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buffer = stackalloc byte[LoadTestRequestInfo.HeaderSize];
        header.CopyTo(buffer);
        var totalSize = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2));
        if (totalSize < LoadTestRequestInfo.HeaderSize)
            throw new InvalidOperationException($"Invalid packet total size {totalSize}.");

        return totalSize - LoadTestRequestInfo.HeaderSize;
    }

    protected override LoadTestRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buffer = stackalloc byte[LoadTestRequestInfo.HeaderSize];
        header.CopyTo(buffer);
        return new LoadTestRequestInfo(
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2)),
            unchecked((sbyte)buffer[4]),
            body.ToArray());
    }
}
