using System.Buffers;
using System.Buffers.Binary;

namespace SuperSocketLite.LoadTest.Shared;

public sealed record BinaryPacket(short PacketId, sbyte Value1, byte[] Body)
{
    public const int HeaderSize = 5;

    /// <summary>
    /// 본문 앞부분에 상관 ID를 싣는 데 쓰는 바이트 수입니다.
    /// 서버가 본문을 그대로 돌려주므로 응답에서 어떤 요청에 대한 것인지 되찾을 수 있습니다.
    /// </summary>
    public const int CorrelationSize = sizeof(long);

    /// <summary>
    /// 본문 앞 <see cref="CorrelationSize"/>바이트에 상관 ID를 써 넣은 본문을 만듭니다.
    /// 본문이 그보다 짧으면 늘리고, 길면 앞부분만 덮어써서 페이로드 크기를 유지합니다.
    /// </summary>
    public static byte[] WithCorrelationId(ReadOnlySpan<byte> body, long correlationId)
    {
        var result = new byte[Math.Max(body.Length, CorrelationSize)];
        body.CopyTo(result);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(0, CorrelationSize), correlationId);
        return result;
    }

    /// <summary>응답 본문에서 상관 ID를 읽습니다. 본문이 짧으면 실패합니다.</summary>
    public static bool TryReadCorrelationId(ReadOnlySpan<byte> body, out long correlationId)
    {
        if (body.Length < CorrelationSize)
        {
            correlationId = 0;
            return false;
        }

        correlationId = BinaryPrimitives.ReadInt64LittleEndian(body.Slice(0, CorrelationSize));
        return true;
    }

    public static byte[] Encode(short packetId, sbyte value1, ReadOnlySpan<byte> body)
    {
        var buffer = new byte[SizeOf(body.Length)];
        Encode(buffer, packetId, value1, body);
        return buffer;
    }

    public static byte[] Encode(short packetId, sbyte value1, ReadOnlySequence<byte> body)
    {
        var buffer = new byte[SizeOf(body.Length)];
        Encode(buffer, packetId, value1, body);
        return buffer;
    }

    /// <summary>패킷 하나를 담는 데 필요한 바이트 수입니다.</summary>
    public static int SizeOf(long bodyLength)
    {
        var totalSize = checked(HeaderSize + bodyLength);
        if (totalSize > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(bodyLength), "Packet is larger than Int16 totalSize can represent.");

        return (int)totalSize;
    }

    /// <summary>
    /// 패킷을 <paramref name="destination"/>에 써 넣고 쓴 바이트 수를 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 버퍼를 호출자가 마련하므로(스택 또는 <see cref="ArrayPool{T}"/>) 패킷당 배열 할당이 없습니다.
    /// 필요한 크기는 <see cref="SizeOf"/>로 미리 구합니다.
    /// </remarks>
    public static int Encode(Span<byte> destination, short packetId, sbyte value1, ReadOnlySpan<byte> body)
    {
        var totalSize = WriteHeader(destination, packetId, value1, body.Length);
        body.CopyTo(destination.Slice(HeaderSize));
        return totalSize;
    }

    /// <summary>
    /// 여러 조각에 걸쳐 있는 본문을 <paramref name="destination"/>에 이어 붙여 패킷을 만듭니다.
    /// 수신 파이프에서 그대로 넘어온 본문을 배열로 펴지 않고 보낼 때 씁니다.
    /// </summary>
    public static int Encode(Span<byte> destination, short packetId, sbyte value1, ReadOnlySequence<byte> body)
    {
        var totalSize = WriteHeader(destination, packetId, value1, body.Length);
        body.CopyTo(destination.Slice(HeaderSize));
        return totalSize;
    }

    private static int WriteHeader(Span<byte> destination, short packetId, sbyte value1, long bodyLength)
    {
        var totalSize = SizeOf(bodyLength);
        if (destination.Length < totalSize)
            throw new ArgumentException($"Destination needs {totalSize} bytes but has {destination.Length}.", nameof(destination));

        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), packetId);
        destination[4] = unchecked((byte)value1);
        return totalSize;
    }

    public static bool TryDecode(ReadOnlySpan<byte> buffer, out BinaryPacket? packet, out int consumed)
    {
        packet = null;
        consumed = 0;

        if (buffer.Length < HeaderSize)
            return false;

        var totalSize = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2));
        if (totalSize < HeaderSize || buffer.Length < totalSize)
            return false;

        var packetId = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2));
        var value1 = unchecked((sbyte)buffer[4]);
        var body = buffer.Slice(HeaderSize, totalSize - HeaderSize).ToArray();

        packet = new BinaryPacket(packetId, value1, body);
        consumed = totalSize;
        return true;
    }
}
