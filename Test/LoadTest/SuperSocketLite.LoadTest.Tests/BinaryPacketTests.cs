using System.Buffers.Binary;
using System.Reflection;
using SuperSocketLite.LoadTest.Shared;
using SuperSocketLite.LoadTest.Server;

namespace SuperSocketLite.LoadTest.Tests;

internal static class BinaryPacketTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(EncodeWritesFiveByteLittleEndianHeader), EncodeWritesFiveByteLittleEndianHeader);
        yield return new TestCase(nameof(RoundTripsPacketWithoutBody), RoundTripsPacketWithoutBody);
        yield return new TestCase(nameof(RoundTripsPacketWithBody), RoundTripsPacketWithBody);
        yield return new TestCase(nameof(DecodeFailsWhenTotalSizeIsSmallerThanHeader), DecodeFailsWhenTotalSizeIsSmallerThanHeader);
        yield return new TestCase(nameof(ReceiveFilterRejectsHeaderTotalSizeSmallerThanHeader), ReceiveFilterRejectsHeaderTotalSizeSmallerThanHeader);
    }

    private static void EncodeWritesFiveByteLittleEndianHeader()
    {
        var encoded = BinaryPacket.Encode(101, -7, new byte[] { 1, 2, 3 });

        AssertEx.Equal(5, BinaryPacket.HeaderSize);
        AssertEx.Equal(8, BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(0, 2)));
        AssertEx.Equal(101, BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(2, 2)));
        AssertEx.Equal(unchecked((byte)-7), encoded[4]);
    }

    private static void RoundTripsPacketWithoutBody()
    {
        var encoded = BinaryPacket.Encode(203, 11, Array.Empty<byte>());

        AssertEx.True(BinaryPacket.TryDecode(encoded, out var packet, out var consumed), "Packet should decode.");
        AssertEx.Equal(encoded.Length, consumed);
        AssertEx.Equal((short)203, packet!.PacketId);
        AssertEx.Equal((sbyte)11, packet.Value1);
        AssertEx.Equal(0, packet.Body.Length);
    }

    private static void RoundTripsPacketWithBody()
    {
        var body = new byte[] { 9, 8, 7, 6 };
        var encoded = BinaryPacket.Encode(101, 1, body);

        AssertEx.True(BinaryPacket.TryDecode(encoded, out var packet, out var consumed), "Packet should decode.");
        AssertEx.Equal(encoded.Length, consumed);
        AssertEx.Equal((short)101, packet!.PacketId);
        AssertEx.Equal((sbyte)1, packet.Value1);
        AssertEx.SequenceEqual(body, packet.Body);
    }

    private static void DecodeFailsWhenTotalSizeIsSmallerThanHeader()
    {
        var encoded = new byte[BinaryPacket.HeaderSize];
        BinaryPrimitives.WriteInt16LittleEndian(encoded.AsSpan(0, 2), BinaryPacket.HeaderSize - 1);

        AssertEx.False(BinaryPacket.TryDecode(encoded, out var packet, out var consumed), "Invalid total size should fail.");
        AssertEx.Equal(0, consumed);
        AssertEx.True(packet is null, "Packet should be null on failure.");
    }

    private static void ReceiveFilterRejectsHeaderTotalSizeSmallerThanHeader()
    {
        var header = new byte[BinaryPacket.HeaderSize];
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(0, 2), BinaryPacket.HeaderSize - 1);
        var filter = new ReceiveFilter();
        var method = typeof(ReceiveFilter).GetMethod("GetBodyLengthFromHeader", BindingFlags.Instance | BindingFlags.NonPublic);

        AssertEx.True(method is not null, "ReceiveFilter should expose protected GetBodyLengthFromHeader.");
        AssertEx.Throws<TargetInvocationException>(() => method!.Invoke(filter, [new System.Buffers.ReadOnlySequence<byte>(header)]));
    }
}
