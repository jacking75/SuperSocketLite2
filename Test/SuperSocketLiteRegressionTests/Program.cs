using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

var tests = new (string Name, Action Test)[]
{
    ("FixedHeaderReceiveFilter leaves an incomplete request in the pipe and rejects a negative body length", FixedHeaderReceiveFilterLeavesIncompleteRequestAndRejectsNegativeLength),
    ("UDP receive packet exposes pooled payload without cloning and snapshots endpoint", UdpReceivePacketExposesPooledPayloadAndEndpoint),
    ("Channel send queue drains batches in FIFO order with bounded capacity", ChannelSendQueueDrainsBatchesInFifoOrder),
    ("Channel send queue counts a multi-segment send as one slot and copies the caller's list", ChannelSendQueueCountsAMultiSegmentSendAsOneSlot),
    ("Channel send queue keeps every item under concurrent lock-free enqueue", ChannelSendQueueKeepsEveryItemUnderConcurrentEnqueue),
    ("FixedHeaderReceiveFilter parses multi-segment requests without carry buffer copies", FixedHeaderReceiveFilterParsesMultiSegmentRequest),
    ("AppSession pipe parser exposes consumed and examined positions", AppSessionPipeParserExposesConsumedAndExaminedPositions),
    ("SocketSession stores receive processing task for lifecycle observation", SocketSessionStoresReceiveProcessingTask),
    ("TCP keep-alive options are applied to accepted sockets", LiveServerTests.KeepAliveOptionsAreAppliedToAcceptedSockets),
    ("UDP listener starts without the Windows-only SIO_UDP_CONNRESET ioctl", LiveServerTests.UdpListenerStartsOnEveryPlatform),
    ("SendSync releases InSending when the socket was dropped by another thread", LiveServerTests.SendSyncClearsInSendingWhenSocketIsAlreadyGone),
    ("Loopback echo survives a synchronous-completion burst without recursing", LiveServerTests.LoopbackEchoSurvivesSynchronousCompletionBurst),
    ("Echo still works with IOCP-thread receive inlining disabled", LiveServerTests.EchoWorksWithIocpInliningDisabled),
    ("Idle sessions are closed by the tick-based ClearIdleSession timer", LiveServerTests.IdleSessionsAreClosedByTheClearIdleSessionTimer),
    ("LastActiveTime round-trips through the monotonic tick stamp", LiveServerTests.LastActiveTimeRoundTripsThroughTheTickStamp),
    ("Copy-on-send is unaffected by immediate caller buffer reuse", LiveServerTests.CopyOnSendIsUnaffectedByCallerBufferReuse),
    ("Awaitable SendAsync delivers every packet through a size-1 queue", LiveServerTests.AwaitableSendDeliversEveryPacket),
    ("StopAsync drains the queued sends before closing the sessions", LiveServerTests.StopAsyncDrainsQueuedSends),
    ("A single request larger than MaxRequestLength is still rejected", LiveServerTests.OversizedSingleRequestIsStillRejected),
    ("SyncSessionConnectedEvent orders the connected handler before the first request", LiveServerTests.SyncSessionConnectedEventOrdersBeforeFirstRequest),
    ("Connections refused by the connection limit are counted", LiveServerTests.RejectedSessionsAreCounted),
    ("FixedSizeReceiveFilter parses a request split across three segments", FilterAndQueueTests.FixedSizeFilterParsesRequestSplitAcrossThreeSegments),
    ("FixedHeaderReceiveFilter handles a straddling header, an empty body and pipelining", FilterAndQueueTests.FixedHeaderFilterHandlesBoundaryZeroBodyAndPipelining),
    ("Send queue EnqueueAsync waits for space and resumes after a drain", FilterAndQueueTests.SendQueueEnqueueAsyncWaitsForSpace),
    ("Send queue EnqueueAsync unblocks on Complete and on cancellation", FilterAndQueueTests.SendQueueEnqueueAsyncUnblocksOnCompleteAndCancel),
    ("A minimal ILog adapter still receives session-scoped entries", LoggingTests.MinimalAdapterStillReceivesSessionScopedEntries),
    ("Flattened log entries never span more than one line", LoggingTests.FlattenedEntriesAreSingleLine),
    ("Every log level accepts an exception", LoggingTests.EveryLevelAcceptsAnException),
    ("A structured ILog adapter receives the session identity separately", LoggingTests.StructuredAdapterReceivesSessionIdentitySeparately),
    ("Microsoft.Extensions.Logging bridge passes the exception and structured properties", LoggingTests.MicrosoftLoggingBridgePassesExceptionAndProperties),
    ("Microsoft.Extensions.Logging bridge honours level filtering", LoggingTests.MicrosoftLoggingBridgeHonoursLevelFiltering),
    ("Server byte and request totals stay exact under concurrent updates", HotPathTests.ServerTotalsStayExactUnderConcurrentUpdates),
    ("Closing a session while sends are draining fires the close exactly once", HotPathTests.ClosingWhileSendsAreDrainingFiresExactlyOnce),
    ("A closed session returns both of its pooled SocketAsyncEventArgs", HotPathTests.ClosedSessionsReturnBothPooledSocketEventArgs),
    ("Parallel accept loops register every connection of a burst", HotPathTests.ParallelAcceptLoopsRegisterEveryConnection),
    ("An out-of-range accept loop count is clamped into range", HotPathTests.AcceptLoopCountIsClampedIntoRange),
    ("Zero-byte receive echoes every packet", HotPathTests.ZeroByteReceiveEchoesEveryPacket),
    ("Zero-byte receive resumes after an idle period", HotPathTests.ZeroByteReceiveResumesAfterAnIdlePeriod),
    ("Zero-byte receive still detects a client close", HotPathTests.ZeroByteReceiveStillDetectsAClientClose),
    ("Zero-byte receive handles payloads larger than the receive buffer", HotPathTests.ZeroByteReceiveHandlesPayloadsLargerThanTheBuffer)
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

static void FixedHeaderReceiveFilterLeavesIncompleteRequestAndRejectsNegativeLength()
{
    var filter = new OneByteLengthFilter();

    // Header only: the request stays entirely in the pipe.
    var headerOnly = CreateSequence(new byte[][] { new byte[] { 5 } });
    var request = filter.Filter(headerOnly, out var consumed, out var examined);

    AssertNull(request, "body is incomplete after header");
    AssertEqual(0L, headerOnly.Slice(0, consumed).Length, "an incomplete request must not be consumed");
    AssertEqual(1L, headerOnly.Slice(0, examined).Length, "everything available should be examined");

    // Partial body: still nothing consumed.
    var partial = CreateSequence(new byte[][] { new byte[] { 5 }, new byte[] { 10, 11 } });
    request = filter.Filter(partial, out consumed, out examined);

    AssertNull(request, "body is still incomplete");
    AssertEqual(0L, partial.Slice(0, consumed).Length, "a partial body must not be consumed");
    AssertEqual(3L, partial.Slice(0, examined).Length, "everything available should be examined");

    var invalid = new OneByteLengthFilter();
    var negative = CreateSequence(new byte[][] { new byte[] { 0xFF } });
    request = invalid.Filter(negative, out _, out _);

    AssertNull(request, "negative body length must not produce a request");
    AssertEqual(FilterState.Error, invalid.State, "negative body length should put filter into error state");
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

static void FixedHeaderReceiveFilterParsesMultiSegmentRequest()
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

static void SocketSessionStoresReceiveProcessingTask()
{
    var socketSessionType = Type.GetType("SuperSocketLite.SocketEngine.SocketSession, SuperSocketLite", throwOnError: true)!;
    var field = socketSessionType.GetField("_receiveProcessingTask", BindingFlags.Instance | BindingFlags.NonPublic);
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
    private static readonly MethodInfo s_DrainAvailable = s_QueueType.GetMethod("DrainAvailable", new[] { typeof(List<ArraySegment<byte>>), typeof(List<byte[]>) })!;
    private static readonly MethodInfo s_Complete = s_QueueType.GetMethod("Complete", Type.EmptyTypes)!;
    private static readonly PropertyInfo s_CountProperty = s_QueueType.GetProperty("Count")!;

    private readonly object _queue;

    private SendingQueueAccessor(object queue)
    {
        _queue = queue;
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

    public int Count => (int)s_CountProperty.GetValue(_queue)!;

    public bool TryEnqueue(ArraySegment<byte> segment) => (bool)s_TryEnqueueSegment.Invoke(_queue, new object[] { segment })!;

    public bool TryEnqueue(IList<ArraySegment<byte>> segments) => (bool)s_TryEnqueueList.Invoke(_queue, new object[] { segments })!;

    /// <summary>Pooled backing arrays reported by the last drain.</summary>
    public List<byte[]> PooledBuffers { get; } = new List<byte[]>();

    public void DrainAvailable(List<ArraySegment<byte>> into) => s_DrainAvailable.Invoke(_queue, new object[] { into, PooledBuffers });

    public void Complete() => s_Complete.Invoke(_queue, Array.Empty<object>());

    /// <summary>Calls the internal <c>EnqueueAsync(SendItem, CancellationToken)</c>.</summary>
    public Task<bool> EnqueueAsync(ArraySegment<byte> segment, CancellationToken cancellationToken)
    {
        var sendItemType = Type.GetType("SuperSocketLite.Common.SendItem, SuperSocketLite", throwOnError: true)!;
        var item = Activator.CreateInstance(sendItemType, new object[] { segment })!;

        var method = s_QueueType.GetMethod("EnqueueAsync", new[] { sendItemType, typeof(CancellationToken) })!;
        var valueTask = method.Invoke(_queue, new object[] { item, cancellationToken })!;

        return (Task<bool>)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, Array.Empty<object>())!;
    }
}

/// <summary>A one byte, signed header length so a negative length can be exercised.</summary>
sealed class OneByteLengthFilter : FixedHeaderReceiveFilter<TestRequestInfo>
{
    public OneByteLengthFilter()
        : base(1)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> bytes = stackalloc byte[1];
        header.CopyTo(bytes);
        return unchecked((sbyte)bytes[0]);
    }

    protected override TestRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        return new TestRequestInfo("test", body.ToArray());
    }
}

sealed class ThreeByteFixedSizeFilter : FixedSizeReceiveFilter<TestRequestInfo>
{
    public ThreeByteFixedSizeFilter()
        : base(3)
    {
    }

    protected override TestRequestInfo? ProcessMatchedRequest(ReadOnlySequence<byte> buffer)
    {
        return new TestRequestInfo("fixed-size", buffer.ToArray());
    }
}

sealed class OneByteSequenceLengthFilter : FixedHeaderReceiveFilter<TestRequestInfo>
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

sealed class TwoByteLengthFilter : FixedHeaderReceiveFilter<TestRequestInfo>
{
    public TwoByteLengthFilter()
        : base(2)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> bytes = stackalloc byte[2];
        header.CopyTo(bytes);
        return (bytes[0] << 8) | bytes[1];
    }

    protected override TestRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        return new TestRequestInfo("two-byte", body.ToArray());
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
