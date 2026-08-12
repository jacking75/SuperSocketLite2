using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

var tests = new (string Name, Action Test)[]
{
    ("ReuseLockBaseBuffer.Copy appends data and Commit preserves remaining bytes", ReuseLockBaseBufferCopyAppendsData),
    ("FixedHeaderReceiveFilter reports accumulated body length and rejects negative body length", FixedHeaderReceiveFilterTracksBodyAndRejectsNegativeLength),
    ("SendingQueue.InternalTrim trims across remaining segments after a partial trim", SendingQueueInternalTrimTrimsAcrossRemainingSegments),
    ("UDP receive packet exposes pooled payload without cloning and snapshots endpoint", UdpReceivePacketExposesPooledPayloadAndEndpoint),
    ("Channel send queue drains batches in FIFO order with bounded capacity", ChannelSendQueueDrainsBatchesInFifoOrder),
    ("Channel send queue counts a multi-segment send as one slot and copies the caller's list", ChannelSendQueueCountsAMultiSegmentSendAsOneSlot),
    ("Channel send queue keeps every item under concurrent lock-free enqueue", ChannelSendQueueKeepsEveryItemUnderConcurrentEnqueue),
    ("FixedHeaderSequenceReceiveFilter parses multi-segment requests without carry buffer copies", FixedHeaderSequenceReceiveFilterParsesMultiSegmentRequest),
    ("FixedHeaderSequenceReceiveFilter preserves fragmented byte-array requests", FixedHeaderSequenceReceiveFilterPreservesFragmentedByteArrayRequest),
    ("AppSession pipe parser exposes consumed and examined positions", AppSessionPipeParserExposesConsumedAndExaminedPositions),
    ("Legacy fixed-size and fixed-header filters opt in to sequence receive path", LegacyFiltersOptInToSequenceReceivePath),
    ("SocketSession stores receive processing task for lifecycle observation", SocketSessionStoresReceiveProcessingTask),
    ("TCP keep-alive options are applied to accepted sockets", LiveServerTests.KeepAliveOptionsAreAppliedToAcceptedSockets),
    ("UDP listener starts without the Windows-only SIO_UDP_CONNRESET ioctl", LiveServerTests.UdpListenerStartsOnEveryPlatform),
    ("SendSync releases InSending when the socket was dropped by another thread", LiveServerTests.SendSyncClearsInSendingWhenSocketIsAlreadyGone),
    ("Loopback echo survives a synchronous-completion burst without recursing", LiveServerTests.LoopbackEchoSurvivesSynchronousCompletionBurst),
    ("Echo still works with IOCP-thread receive inlining disabled", LiveServerTests.EchoWorksWithIocpInliningDisabled),
    ("Idle sessions are closed by the tick-based ClearIdleSession timer", LiveServerTests.IdleSessionsAreClosedByTheClearIdleSessionTimer),
    ("LastActiveTime round-trips through the monotonic tick stamp", LiveServerTests.LastActiveTimeRoundTripsThroughTheTickStamp)
};

var failures = 0;

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex.ToString());
    }
}

if (failures > 0)
    Environment.Exit(1);

static void ReuseLockBaseBufferCopyAppendsData()
{
    var buffer = new ReuseLockBaseBuffer(16);

    AssertTrue(buffer.Copy(new byte[] { 1, 2, 3 }, 0, 3), "first copy should fit");
    AssertEqual(3, buffer.GetData().Count, "first copy should advance write position");

    AssertTrue(buffer.Copy(new byte[] { 4, 5 }, 0, 2), "second copy should fit");
    var data = buffer.GetData();
    AssertEqual(5, data.Count, "second copy should append after first copy");
    AssertSequence(new byte[] { 1, 2, 3, 4, 5 }, data);

    buffer.Commit(3);
    AssertSequence(new byte[] { 4, 5 }, buffer.GetData());
}

static void FixedHeaderReceiveFilterTracksBodyAndRejectsNegativeLength()
{
    var filter = new OneByteLengthFilter();

    var header = new byte[] { 5 };
    var request = filter.Filter(header, 0, 1, true, out var rest);

    AssertNull(request, "body is incomplete after header");
    AssertEqual(0, rest, "header read should leave no rest");
    AssertEqual(1, filter.LeftBufferSize, "left buffer should include parsed header while waiting for body");

    request = filter.Filter(new byte[] { 10, 11 }, 0, 2, true, out rest);
    AssertNull(request, "body is still incomplete");
    AssertEqual(0, rest, "partial body should leave no rest");
    AssertEqual(3, filter.LeftBufferSize, "left buffer should include header and accumulated body bytes");

    var invalid = new OneByteLengthFilter();
    request = invalid.Filter(new byte[] { 0xFF }, 0, 1, true, out rest);

    AssertNull(request, "negative body length must not produce a request");
    AssertEqual(FilterState.Error, invalid.State, "negative body length should put filter into error state");
}

static void SendingQueueInternalTrimTrimsAcrossRemainingSegments()
{
    var globalQueue = new ArraySegment<byte>[4];
    var queue = new SendingQueue(globalQueue, 0, 4);
    var trackID = queue.TrackID;

    AssertTrue(queue.Enqueue(new ArraySegment<byte>(new byte[] { 1, 2 }), trackID), "first enqueue should succeed");
    AssertTrue(queue.Enqueue(new ArraySegment<byte>(new byte[] { 3, 4 }), trackID), "second enqueue should succeed");
    AssertTrue(queue.Enqueue(new ArraySegment<byte>(new byte[] { 5, 6 }), trackID), "third enqueue should succeed");

    queue.InternalTrim(3);
    AssertEqual(2, queue.Count, "first trim should leave two logical segments");
    AssertSequence(new byte[] { 4 }, queue[0]);
    AssertSequence(new byte[] { 5, 6 }, queue[1]);

    queue.InternalTrim(2);
    AssertEqual(1, queue.Count, "second trim should leave one logical segment");
    AssertSequence(new byte[] { 6 }, queue[0]);
}

static void UdpReceivePacketExposesPooledPayloadAndEndpoint()
{
    var packetType = Type.GetType("SuperSocketLite.SocketEngine.UdpReceivePacket, SuperSocketLite", throwOnError: true)!;
    var buffer = ArrayPool<byte>.Shared.Rent(5);
    buffer[0] = 0;
    buffer[1] = 10;
    buffer[2] = 11;
    buffer[3] = 12;
    buffer[4] = 0;
    var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
    var packet = Activator.CreateInstance(packetType, nonPublic: true)!;

    packetType.GetMethod("Initialize")!.Invoke(packet, new object[] { buffer, 1, 3, endpoint });

    AssertSame(buffer, (byte[])packetType.GetProperty("Buffer")!.GetValue(packet)!, "packet should keep the pooled buffer owner");
    AssertEqual(1, (int)packetType.GetProperty("Offset")!.GetValue(packet)!, "packet offset should match receive offset");
    AssertEqual(3, (int)packetType.GetProperty("Count")!.GetValue(packet)!, "packet count should match received bytes");

    var remoteEndPoint = (IPEndPoint)packetType.GetProperty("RemoteEndPoint")!.GetValue(packet)!;
    AssertTrue(!ReferenceEquals(endpoint, remoteEndPoint), "packet should snapshot the endpoint instance");
    AssertEqual(endpoint.ToString(), remoteEndPoint.ToString(), "packet endpoint should keep address and port");

    ((IDisposable)packet).Dispose();
}

static void ChannelSendQueueDrainsBatchesInFifoOrder()
{
    var queue = SendingQueueAccessor.Create(2);

    var first = new ArraySegment<byte>(new byte[] { 1 });
    var second = new ArraySegment<byte>(new byte[] { 2 });
    var third = new ArraySegment<byte>(new byte[] { 3 });

    AssertTrue(queue.TryEnqueue(first), "first enqueue should fit");
    AssertTrue(queue.TryEnqueue(second), "second enqueue should fit");
    AssertTrue(!queue.TryEnqueue(third), "bounded queue should reject when full");

    var batch = new List<ArraySegment<byte>>();
    queue.DrainAvailable(batch);
    AssertEqual(2, batch.Count, "drain should return all available queued segments");
    AssertSequence(new byte[] { 1 }, batch[0]);
    AssertSequence(new byte[] { 2 }, batch[1]);
    AssertEqual(0, queue.Count, "drain should empty the queue");

    AssertTrue(queue.TryEnqueue(new[] { third }), "enqueue list should fit after drain");
    queue.DrainAvailable(batch);
    AssertEqual(1, batch.Count, "second drain should return newly queued segment and clear the previous batch");
    AssertSequence(new byte[] { 3 }, batch[0]);

    queue.Complete();
    AssertTrue(!queue.TryEnqueue(new[] { first, second }), "completed queue should reject list enqueue");
    queue.DrainAvailable(batch);
    AssertEqual(0, batch.Count, "completed queue should not publish rejected list items");
}

static void ChannelSendQueueCountsAMultiSegmentSendAsOneSlot()
{
    var queue = SendingQueueAccessor.Create(2);

    var listA = new List<ArraySegment<byte>>
    {
        new ArraySegment<byte>(new byte[] { 1 }),
        new ArraySegment<byte>(new byte[] { 2 }),
        new ArraySegment<byte>(new byte[] { 3 })
    };

    AssertTrue(queue.TryEnqueue(listA), "a multi-segment send should occupy a single slot");

    // The queue must not keep a reference to the caller's list: reusing it right after the enqueue
    // is allowed and must not change what gets drained.
    listA.Clear();
    listA.Add(new ArraySegment<byte>(new byte[] { 9 }));

    AssertTrue(queue.TryEnqueue(listA), "second multi-segment send should fit into the second slot");
    AssertTrue(!queue.TryEnqueue(new ArraySegment<byte>(new byte[] { 4 })), "a third send must be rejected when both slots are taken");

    var batch = new List<ArraySegment<byte>>();
    queue.DrainAvailable(batch);

    AssertEqual(4, batch.Count, "drain should flatten every queued segment");
    AssertSequence(new byte[] { 1 }, batch[0]);
    AssertSequence(new byte[] { 2 }, batch[1]);
    AssertSequence(new byte[] { 3 }, batch[2]);
    AssertSequence(new byte[] { 9 }, batch[3]);
}

static void ChannelSendQueueKeepsEveryItemUnderConcurrentEnqueue()
{
    const int threadCount = 8;
    const int itemsPerThread = 500;
    const int total = threadCount * itemsPerThread;

    var queue = SendingQueueAccessor.Create(total);
    var start = new ManualResetEventSlim(false);
    var accepted = 0;
    var threads = new Thread[threadCount];

    for (var t = 0; t < threadCount; t++)
    {
        var threadIndex = t;

        threads[t] = new Thread(() =>
        {
            start.Wait();

            for (var i = 0; i < itemsPerThread; i++)
            {
                var payload = new byte[] { (byte)threadIndex, (byte)(i & 0xFF) };

                if (queue.TryEnqueue(new ArraySegment<byte>(payload)))
                    Interlocked.Increment(ref accepted);
            }
        });

        threads[t].Start();
    }

    start.Set();

    foreach (var thread in threads)
        thread.Join();

    AssertEqual(total, accepted, "a queue sized for every item must accept them all");
    AssertEqual(total, queue.Count, "the advisory count should settle on the enqueued item count");

    var batch = new List<ArraySegment<byte>>();
    queue.DrainAvailable(batch);

    AssertEqual(total, batch.Count, "drain must not lose items enqueued concurrently");
    AssertEqual(0, queue.Count, "the count should be back to zero after a full drain");
}

static void FixedHeaderSequenceReceiveFilterParsesMultiSegmentRequest()
{
    var filter = new OneByteSequenceLengthFilter();
    var sequence = CreateSequence(new byte[][] { new byte[] { 2 }, new byte[] { 10 }, new byte[] { 11 }, new byte[] { 99 } });

    var request = filter.Filter(sequence, out var consumed, out var examined);

    AssertSequence(new byte[] { 10, 11 }, new ArraySegment<byte>(request!.Body));
    AssertEqual(3L, sequence.Slice(0, consumed).Length, "sequence filter should consume exactly header and body");
    AssertEqual(3L, sequence.Slice(0, examined).Length, "sequence filter should examine the parsed request");
    AssertEqual(1L, sequence.Slice(consumed).Length, "sequence filter should leave rest for the next parse");

    var incomplete = CreateSequence(new byte[][] { new byte[] { 2 }, new byte[] { 10 } });
    request = filter.Filter(incomplete, out consumed, out examined);

    AssertNull(request, "incomplete sequence should not produce a request");
    AssertEqual(0L, incomplete.Slice(0, consumed).Length, "incomplete sequence should not consume buffered bytes");
    AssertEqual(2L, incomplete.Slice(0, examined).Length, "incomplete sequence should examine available bytes");
}

static void FixedHeaderSequenceReceiveFilterPreservesFragmentedByteArrayRequest()
{
    var filter = new OneByteSequenceLengthFilter();

    var request = filter.Filter(new byte[] { 2, 10 }, 0, 2, true, out var rest);

    AssertNull(request, "fragmented byte-array request should wait for the remaining body");
    AssertEqual(0, rest, "incomplete byte-array request should not report rest");
    AssertEqual(2, filter.LeftBufferSize, "filter should retain the incomplete header/body bytes");

    request = filter.Filter(new byte[] { 11, 99 }, 0, 2, true, out rest);

    AssertSequence(new byte[] { 10, 11 }, new ArraySegment<byte>(request!.Body));
    AssertEqual(1, rest, "complete byte-array request should leave trailing bytes as rest");
    AssertEqual(0, filter.LeftBufferSize, "complete byte-array request should clear retained bytes");
}

static void AppSessionPipeParserExposesConsumedAndExaminedPositions()
{
    var method = typeof(IAppSession).GetMethod(
        "ProcessRequest",
        new[] { typeof(ReadOnlySequence<byte>) });

    AssertTrue(method != null, "IAppSession should expose a pipe ProcessRequest overload");
    AssertEqual("ProcessReceiveResult", method!.ReturnType.Name, "pipe ProcessRequest should return both consumed and examined positions");

    var consumedProperty = method.ReturnType.GetProperty("Consumed");
    var examinedProperty = method.ReturnType.GetProperty("Examined");

    AssertTrue(consumedProperty != null, "pipe ProcessRequest result should expose Consumed");
    AssertTrue(examinedProperty != null, "pipe ProcessRequest result should expose Examined");
}

static void LegacyFiltersOptInToSequenceReceivePath()
{
    var fixedSize = new ThreeByteFixedSizeFilter();
    var fixedHeader = new OneByteLengthFilter();

    var fixedSizeSequenceFilter = (object)fixedSize as ISequenceReceiveFilter<TestRequestInfo>;
    var fixedHeaderSequenceFilter = (object)fixedHeader as ISequenceReceiveFilter<TestRequestInfo>;

    AssertTrue(fixedSizeSequenceFilter != null, "FixedSizeReceiveFilter should be usable by the sequence pipe path");
    AssertTrue(fixedHeaderSequenceFilter != null, "FixedHeaderReceiveFilter should be usable by the sequence pipe path");

    var fixedSizeSequence = CreateSequence(new byte[][] { new byte[] { 1 }, new byte[] { 2, 3 }, new byte[] { 4 } });
    var fixedSizeRequest = fixedSizeSequenceFilter!.Filter(fixedSizeSequence, out var consumed, out var examined);
    AssertSequence(new byte[] { 1, 2, 3 }, new ArraySegment<byte>(fixedSizeRequest!.Body));
    AssertEqual(3L, fixedSizeSequence.Slice(0, consumed).Length, "fixed-size sequence filter should consume the matched size");
    AssertEqual(3L, fixedSizeSequence.Slice(0, examined).Length, "fixed-size sequence filter should examine the matched size");

    var fixedHeaderSequence = CreateSequence(new byte[][] { new byte[] { 2 }, new byte[] { 10 }, new byte[] { 11 }, new byte[] { 12 } });
    var fixedHeaderRequest = fixedHeaderSequenceFilter!.Filter(fixedHeaderSequence, out consumed, out examined);
    AssertSequence(new byte[] { 10, 11 }, new ArraySegment<byte>(fixedHeaderRequest!.Body));
    AssertEqual(3L, fixedHeaderSequence.Slice(0, consumed).Length, "fixed-header sequence filter should consume header and body");
    AssertEqual(3L, fixedHeaderSequence.Slice(0, examined).Length, "fixed-header sequence filter should examine header and body");
}

static void SocketSessionStoresReceiveProcessingTask()
{
    var socketSessionType = Type.GetType("SuperSocketLite.SocketEngine.SocketSession, SuperSocketLite", throwOnError: true)!;
    var field = socketSessionType.GetField("m_ReceiveProcessingTask", BindingFlags.Instance | BindingFlags.NonPublic);
    AssertTrue(field != null, "SocketSession should store the receive processing task for close-time observation");
}

static void AssertTrue(bool actual, string message)
{
    if (!actual)
        throw new InvalidOperationException(message);
}

static void AssertSame(object expected, object actual, string message)
{
    if (!ReferenceEquals(expected, actual))
        throw new InvalidOperationException(message);
}

static void AssertNull(object? actual, string message)
{
    if (actual != null)
        throw new InvalidOperationException(message);
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new InvalidOperationException($"{message}. Expected {expected}, actual {actual}.");
}

static void AssertSequence(byte[] expected, ArraySegment<byte> actual)
{
    AssertEqual(expected.Length, actual.Count, "segment length mismatch");

    for (var i = 0; i < expected.Length; i++)
    {
        var value = actual.Array![actual.Offset + i];
        if (value != expected[i])
            throw new InvalidOperationException($"segment byte mismatch at {i}. Expected {expected[i]}, actual {value}.");
    }
}

static ReadOnlySequence<byte> CreateSequence(byte[][] segments)
{
    BufferSegment? first = null;
    BufferSegment? last = null;
    long runningIndex = 0;

    foreach (var segment in segments)
    {
        var current = new BufferSegment(segment);

        if (first == null)
        {
            first = current;
        }
        else
        {
            last!.SetNext(current, runningIndex);
        }

        last = current;
        runningIndex += segment.Length;
    }

    return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
}

/// <summary>
/// Reflection wrapper over the internal <c>ChannelSendingQueue</c>.
/// </summary>
sealed class SendingQueueAccessor
{
    private static readonly Type s_QueueType = Type.GetType("SuperSocketLite.Common.ChannelSendingQueue, SuperSocketLite", throwOnError: true)!;
    private static readonly MethodInfo s_TryEnqueueSegment = s_QueueType.GetMethod("TryEnqueue", new[] { typeof(ArraySegment<byte>) })!;
    private static readonly MethodInfo s_TryEnqueueList = s_QueueType.GetMethod("TryEnqueue", new[] { typeof(IList<ArraySegment<byte>>) })!;
    private static readonly MethodInfo s_DrainAvailable = s_QueueType.GetMethod("DrainAvailable", new[] { typeof(List<ArraySegment<byte>>) })!;
    private static readonly MethodInfo s_Complete = s_QueueType.GetMethod("Complete", Type.EmptyTypes)!;
    private static readonly PropertyInfo s_CountProperty = s_QueueType.GetProperty("Count")!;

    private readonly object m_Queue;

    private SendingQueueAccessor(object queue)
    {
        m_Queue = queue;
    }

    public static SendingQueueAccessor Create(int capacity)
    {
        var queue = Activator.CreateInstance(
            s_QueueType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { capacity },
            culture: null)!;

        return new SendingQueueAccessor(queue);
    }

    public int Count => (int)s_CountProperty.GetValue(m_Queue)!;

    public bool TryEnqueue(ArraySegment<byte> segment) => (bool)s_TryEnqueueSegment.Invoke(m_Queue, new object[] { segment })!;

    public bool TryEnqueue(IList<ArraySegment<byte>> segments) => (bool)s_TryEnqueueList.Invoke(m_Queue, new object[] { segments })!;

    public void DrainAvailable(List<ArraySegment<byte>> into) => s_DrainAvailable.Invoke(m_Queue, new object[] { into });

    public void Complete() => s_Complete.Invoke(m_Queue, Array.Empty<object>());
}

sealed class OneByteLengthFilter : FixedHeaderReceiveFilter<TestRequestInfo>
{
    public OneByteLengthFilter()
        : base(1)
    {
    }

    protected override int GetBodyLengthFromHeader(byte[] header, int offset, int length)
    {
        return unchecked((sbyte)header[offset]);
    }

    protected override TestRequestInfo? ResolveRequestInfo(ArraySegment<byte> header, byte[]? bodyBuffer, int offset, int length)
    {
        var body = bodyBuffer == null ? Array.Empty<byte>() : bodyBuffer.AsSpan(offset, length).ToArray();
        return new TestRequestInfo("test", body);
    }
}

sealed class ThreeByteFixedSizeFilter : FixedSizeReceiveFilter<TestRequestInfo>
{
    public ThreeByteFixedSizeFilter()
        : base(3)
    {
    }

    protected override TestRequestInfo? ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied)
    {
        return new TestRequestInfo("fixed-size", buffer.AsSpan(offset, length).ToArray());
    }
}

sealed class OneByteSequenceLengthFilter : FixedHeaderSequenceReceiveFilter<TestRequestInfo>
{
    public OneByteSequenceLengthFilter()
        : base(1)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        return header.First.Span[0];
    }

    protected override TestRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        return new TestRequestInfo("sequence", body.ToArray());
    }
}

sealed class BufferSegment : ReadOnlySequenceSegment<byte>
{
    public BufferSegment(byte[] memory)
    {
        Memory = memory;
    }

    public void SetNext(BufferSegment next, long runningIndex)
    {
        next.RunningIndex = runningIndex;
        Next = next;
    }
}

sealed class TestRequestInfo : IRequestInfo
{
    public TestRequestInfo(string key, byte[] body)
    {
        Key = key;
        Body = body;
    }

    public string Key { get; }

    public byte[] Body { get; }
}
