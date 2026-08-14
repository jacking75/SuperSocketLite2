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
        yield return new TestCase(nameof(CorrelationIdSurvivesEncodeDecodeRoundTrip), CorrelationIdSurvivesEncodeDecodeRoundTrip);
        yield return new TestCase(nameof(CorrelationIdKeepsPayloadSizeWhenBodyIsLongEnough), CorrelationIdKeepsPayloadSizeWhenBodyIsLongEnough);
        yield return new TestCase(nameof(CorrelationIdExpandsBodyThatIsTooShort), CorrelationIdExpandsBodyThatIsTooShort);
        yield return new TestCase(nameof(CorrelationIdReadFailsOnShortBody), CorrelationIdReadFailsOnShortBody);
    }

    /// <summary>서버가 본문을 그대로 되돌려주므로 상관 ID가 왕복해야 응답을 요청에 짝지을 수 있다.</summary>
    private static void CorrelationIdSurvivesEncodeDecodeRoundTrip()
    {
        const long correlationId = 1234567890123L;
        var body = BinaryPacket.WithCorrelationId(new byte[32], correlationId);
        var encoded = BinaryPacket.Encode(101, 0, body);

        AssertEx.True(BinaryPacket.TryDecode(encoded, out var packet, out _), "Packet should decode.");
        AssertEx.True(BinaryPacket.TryReadCorrelationId(packet!.Body, out var actual), "Correlation id should be readable.");
        AssertEx.Equal(correlationId, actual);
    }

    /// <summary>페이로드 크기가 부하 특성이므로 본문이 충분히 길면 크기가 그대로여야 한다.</summary>
    private static void CorrelationIdKeepsPayloadSizeWhenBodyIsLongEnough()
    {
        var body = BinaryPacket.WithCorrelationId(new byte[4096], 42);

        AssertEx.Equal(4096, body.Length);
        AssertEx.True(BinaryPacket.TryReadCorrelationId(body, out var actual), "Correlation id should be readable.");
        AssertEx.Equal(42L, actual);
    }

    /// <summary>하트비트처럼 본문이 빈 패킷도 상관 ID를 실을 자리를 얻어야 한다.</summary>
    private static void CorrelationIdExpandsBodyThatIsTooShort()
    {
        var body = BinaryPacket.WithCorrelationId([], 7);

        AssertEx.Equal(BinaryPacket.CorrelationSize, body.Length);
        AssertEx.True(BinaryPacket.TryReadCorrelationId(body, out var actual), "Correlation id should be readable.");
        AssertEx.Equal(7L, actual);
    }

    private static void CorrelationIdReadFailsOnShortBody()
    {
        AssertEx.False(BinaryPacket.TryReadCorrelationId(new byte[3], out _), "A body shorter than the correlation field should fail.");
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
        var filter = new ReceiveFilter(AllocationMode.Pooled);
        var method = typeof(ReceiveFilter).GetMethod("GetBodyLengthFromHeader", BindingFlags.Instance | BindingFlags.NonPublic);

        AssertEx.True(method is not null, "ReceiveFilter should expose protected GetBodyLengthFromHeader.");
        AssertEx.Throws<TargetInvocationException>(() => method!.Invoke(filter, [new System.Buffers.ReadOnlySequence<byte>(header)]));
    }
}
