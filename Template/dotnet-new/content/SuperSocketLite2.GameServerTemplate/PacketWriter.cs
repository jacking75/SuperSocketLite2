using System.Buffers;
using System.Buffers.Binary;

namespace SuperSocketLite2.GameServerTemplate;

/// <summary>응답 패킷을 만들어 보낸다.</summary>
/// <remarks>
/// 응답마다 배열을 새로 만들지 않는다. 작은 패킷은 스택에, 큰 패킷은 <see cref="ArrayPool{T}"/>
/// 에서 빌려 쓴다. 둘 다 이 메서드가 리턴하면 사라지는 버퍼이므로 <c>Send</c> 가 아니라
/// 반드시 <c>SendCopied</c> 로 보낸다 — <c>Send</c> 는 zero-copy라 배열을 참조로만 들고 간다.
/// </remarks>
internal static class PacketWriter
{
    /// <summary>스택에 담아 보낼 응답의 상한. 이보다 크면 풀에서 빌린다.</summary>
    private const int StackBufferSize = 512;

    public static void Send(NetworkSession session, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            Write(packet, packetId, body);
            session.SendCopied(packet.Slice(0, totalSize));
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            Write(rented, packetId, body);
            session.SendCopied(rented.AsSpan(0, totalSize));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static void Send(NetworkSession session, short packetId, ReadOnlySpan<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + body.Length;

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            Write(packet, packetId, body);
            session.SendCopied(packet.Slice(0, totalSize));
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            Write(rented, packetId, body);
            session.SendCopied(rented.AsSpan(0, totalSize));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void Write(Span<byte> destination, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        WriteHeader(destination, packetId, totalSize);
        body.CopyTo(destination.Slice(PacketRequestInfo.HeaderSize));
    }

    private static void Write(Span<byte> destination, short packetId, ReadOnlySpan<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + body.Length;

        WriteHeader(destination, packetId, totalSize);
        body.CopyTo(destination.Slice(PacketRequestInfo.HeaderSize));
    }

    private static void WriteHeader(Span<byte> destination, short packetId, int totalSize)
    {
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), packetId);
    }
}
