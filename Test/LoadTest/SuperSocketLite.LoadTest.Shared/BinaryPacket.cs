using System.Buffers.Binary;

namespace SuperSocketLite.LoadTest.Shared;

public sealed record BinaryPacket(short PacketId, sbyte Value1, byte[] Body)
{
    public const int HeaderSize = 5;

    public static byte[] Encode(short packetId, sbyte value1, ReadOnlySpan<byte> body)
    {
        var totalSize = checked(HeaderSize + body.Length);
        if (totalSize > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(body), "Packet is larger than Int16 totalSize can represent.");

        var buffer = new byte[totalSize];
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(2, 2), packetId);
        buffer[4] = unchecked((byte)value1);
        body.CopyTo(buffer.AsSpan(HeaderSize));
        return buffer;
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
