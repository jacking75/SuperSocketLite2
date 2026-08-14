using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.LoadTest.Server;
using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Tests;

/// <summary>
/// <c>Docs/GC_Copy_Minimization.md</c>의 개선 1·3이 실제로 그렇게 동작하는지 확인합니다.
/// 할당량 자체는 여기서 재지 않습니다. 대신 할당을 없앨 수 있게 해 주는 성질
/// - 요청 인스턴스 재사용, 본문 무복사, 버퍼를 받는 인코딩 - 을 확인합니다.
/// </summary>
internal static class ZeroAllocationTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(EncodeIntoBufferMatchesAllocatingEncode), EncodeIntoBufferMatchesAllocatingEncode);
        yield return new TestCase(nameof(EncodeIntoBufferJoinsMultiSegmentBody), EncodeIntoBufferJoinsMultiSegmentBody);
        yield return new TestCase(nameof(EncodeIntoBufferAcceptsOversizedDestination), EncodeIntoBufferAcceptsOversizedDestination);
        yield return new TestCase(nameof(EncodeIntoBufferRejectsTooSmallDestination), EncodeIntoBufferRejectsTooSmallDestination);
        yield return new TestCase(nameof(SizeOfRejectsPacketLargerThanInt16), SizeOfRejectsPacketLargerThanInt16);
        yield return new TestCase(nameof(PooledFilterReusesTheRequestInstance), PooledFilterReusesTheRequestInstance);
        yield return new TestCase(nameof(PooledFilterBodyPointsAtTheReceiveBuffer), PooledFilterBodyPointsAtTheReceiveBuffer);
        yield return new TestCase(nameof(LegacyFilterAllocatesPerPacket), LegacyFilterAllocatesPerPacket);
        yield return new TestCase(nameof(LegacyFilterCopiesTheBody), LegacyFilterCopiesTheBody);
        yield return new TestCase(nameof(PooledFilterReadsBodySplitAcrossSegments), PooledFilterReadsBodySplitAcrossSegments);
    }

    /// <summary>버퍼를 받는 인코딩은 배열을 새로 만드는 인코딩과 바이트가 같아야 한다.</summary>
    private static void EncodeIntoBufferMatchesAllocatingEncode()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var expected = BinaryPacket.Encode(101, -7, body);

        var actual = new byte[expected.Length];
        var written = BinaryPacket.Encode(actual, 101, -7, new ReadOnlySequence<byte>(body));

        AssertEx.Equal(expected.Length, written);
        AssertEx.SequenceEqual(expected, actual);
    }

    /// <summary>수신 파이프의 본문은 조각으로 나뉘어 올 수 있다. 그대로 이어 붙여야 한다.</summary>
    private static void EncodeIntoBufferJoinsMultiSegmentBody()
    {
        var expected = BinaryPacket.Encode(205, 3, new byte[] { 10, 20, 30, 40 });

        var split = MultiSegment([10, 20], [30, 40]);
        AssertEx.False(split.IsSingleSegment, "The test body should span more than one segment.");

        var actual = new byte[expected.Length];
        var written = BinaryPacket.Encode(actual, 205, 3, split);

        AssertEx.Equal(expected.Length, written);
        AssertEx.SequenceEqual(expected, actual);
    }

    /// <summary>풀에서 빌린 배열은 요청한 크기보다 크다. 그래도 패킷 크기만큼만 써야 한다.</summary>
    private static void EncodeIntoBufferAcceptsOversizedDestination()
    {
        var body = new byte[] { 7, 7, 7 };
        var expected = BinaryPacket.Encode(101, 0, body);

        var oversized = new byte[expected.Length + 64];
        var written = BinaryPacket.Encode(oversized, 101, 0, new ReadOnlySequence<byte>(body));

        AssertEx.Equal(expected.Length, written);
        AssertEx.SequenceEqual(expected, oversized.AsSpan(0, written).ToArray());

        // 뒤쪽은 건드리지 않는다.
        foreach (var trailing in oversized.AsSpan(written).ToArray())
            AssertEx.Equal((byte)0, trailing);
    }

    private static void EncodeIntoBufferRejectsTooSmallDestination()
    {
        var body = new byte[] { 1, 2, 3 };
        var tooSmall = new byte[BinaryPacket.HeaderSize + body.Length - 1];

        AssertEx.Throws<ArgumentException>(
            () => BinaryPacket.Encode(tooSmall, 101, 0, new ReadOnlySequence<byte>(body)));
    }

    private static void SizeOfRejectsPacketLargerThanInt16()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BinaryPacket.SizeOf(short.MaxValue));
    }

    /// <summary>
    /// 개선 1의 핵심: 필터가 요청 인스턴스를 돌려 쓴다.
    /// 그래서 핸들러가 리턴한 뒤에는 이전 요청을 들고 있으면 안 된다.
    /// </summary>
    private static void PooledFilterReusesTheRequestInstance()
    {
        var filter = new ReceiveFilter(AllocationMode.Pooled);

        var first = filter.Filter(new ReadOnlySequence<byte>(Packet(101, 1, [9])), out _, out _);
        var second = filter.Filter(new ReadOnlySequence<byte>(Packet(205, 2, [8])), out _, out _);

        AssertEx.True(first is not null && second is not null, "Both packets should parse.");
        AssertEx.True(ReferenceEquals(first, second), "Pooled mode should hand back the same instance.");

        // 값은 마지막 패킷의 것이어야 한다.
        AssertEx.Equal((short)205, second!.PacketId);
        AssertEx.Equal((sbyte)2, second.Value1);
    }

    /// <summary>개선 1의 나머지 절반: 본문을 복사하지 않고 수신 버퍼를 그대로 가리킨다.</summary>
    private static void PooledFilterBodyPointsAtTheReceiveBuffer()
    {
        var filter = new ReceiveFilter(AllocationMode.Pooled);
        var buffer = Packet(101, 0, [42]);

        var request = filter.Filter(new ReadOnlySequence<byte>(buffer), out _, out _);
        AssertEx.True(request is not null, "The packet should parse.");
        AssertEx.Equal((byte)42, request!.Body.FirstSpan[0]);

        // 원본을 고치면 본문에도 그대로 비쳐야 복사가 없는 것이다.
        buffer[BinaryPacket.HeaderSize] = 99;
        AssertEx.Equal((byte)99, request.Body.FirstSpan[0]);
    }

    private static void LegacyFilterAllocatesPerPacket()
    {
        var filter = new ReceiveFilter(AllocationMode.Legacy);

        var first = filter.Filter(new ReadOnlySequence<byte>(Packet(101, 1, [9])), out _, out _);
        var second = filter.Filter(new ReadOnlySequence<byte>(Packet(205, 2, [8])), out _, out _);

        AssertEx.True(first is not null && second is not null, "Both packets should parse.");
        AssertEx.False(ReferenceEquals(first, second), "Legacy mode should allocate a new instance per packet.");
    }

    private static void LegacyFilterCopiesTheBody()
    {
        var filter = new ReceiveFilter(AllocationMode.Legacy);
        var buffer = Packet(101, 0, [42]);

        var request = filter.Filter(new ReadOnlySequence<byte>(buffer), out _, out _);
        AssertEx.True(request is not null, "The packet should parse.");

        buffer[BinaryPacket.HeaderSize] = 99;
        AssertEx.Equal((byte)42, request!.Body.FirstSpan[0]);
    }

    /// <summary>
    /// 본문이 조각에 걸쳐 있어도 파싱되어야 한다.
    /// 무복사 경로는 본문을 배열로 펴지 않으므로 이 경우가 실제로 핸들러까지 간다.
    /// </summary>
    private static void PooledFilterReadsBodySplitAcrossSegments()
    {
        var packet = Packet(101, 5, [1, 2, 3, 4]);
        var split = MultiSegment(packet[..4], packet[4..]);

        var filter = new ReceiveFilter(AllocationMode.Pooled);
        var request = filter.Filter(split, out var consumed, out _);

        AssertEx.True(request is not null, "The packet should parse.");
        AssertEx.Equal((short)101, request!.PacketId);
        AssertEx.Equal((sbyte)5, request.Value1);
        AssertEx.Equal(4L, request.Body.Length);
        AssertEx.SequenceEqual(new byte[] { 1, 2, 3, 4 }, request.Body.ToArray());
        AssertEx.Equal((long)packet.Length, split.Slice(split.Start, consumed).Length);
    }

    private static byte[] Packet(short packetId, sbyte value1, byte[] body)
    {
        var buffer = new byte[BinaryPacket.HeaderSize + body.Length];
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(0, 2), (short)buffer.Length);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(2, 2), packetId);
        buffer[4] = unchecked((byte)value1);
        body.CopyTo(buffer.AsSpan(BinaryPacket.HeaderSize));
        return buffer;
    }

    private static ReadOnlySequence<byte> MultiSegment(params byte[][] chunks)
    {
        var first = new Segment(chunks[0]);
        var last = first;

        for (var i = 1; i < chunks.Length; i++)
            last = last.Append(chunks[i]);

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
