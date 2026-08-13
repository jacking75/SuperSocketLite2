using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// Unit-level tests for the receive filters and the sending queue.
/// </summary>
static class FilterAndQueueTests
{
    /// <summary>
    /// C-1: a fixed-size request that arrives in three separate pipe segments is still one request,
    /// and the filter consumes exactly the matched size.
    /// </summary>
    public static void FixedSizeFilterParsesRequestSplitAcrossThreeSegments()
    {
        var filter = new ThreeByteFixedSizeFilter();

        var incomplete = TestSequence.Create(new byte[][] { new byte[] { 1 }, new byte[] { 2 } });
        var request = filter.Filter(incomplete, out var consumed, out var examined);

        Assert.True(request == null, "two of the three bytes must not produce a request");
        Assert.Equal(0L, incomplete.Slice(0, consumed).Length, "an incomplete request must stay in the pipe");
        Assert.Equal(incomplete.Length, incomplete.Slice(0, examined).Length, "everything available should be reported as examined");

        var sequence = TestSequence.Create(new byte[][] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3, 4 } });
        request = filter.Filter(sequence, out consumed, out examined);

        Assert.True(request != null, "the request should be parsed once all three bytes arrived");
        AssertBody(new byte[] { 1, 2, 3 }, request!.Body, "the body should be the first three bytes");
        Assert.Equal(3L, sequence.Slice(0, consumed).Length, "consumed should cover exactly the matched size");
        Assert.Equal(3L, sequence.Slice(0, examined).Length, "examined should match consumed for a complete request");
        Assert.Equal(1L, sequence.Slice(consumed).Length, "the trailing byte should be left for the next parse");
    }

    /// <summary>
    /// C-1: the fixed-header filter must cope with a header split across a segment boundary, with a
    /// zero-length body, and with several pipelined requests sitting in one buffer.
    /// </summary>
    public static void FixedHeaderFilterHandlesBoundaryZeroBodyAndPipelining()
    {
        var filter = new TwoByteLengthFilter();

        // The two header bytes straddle a segment boundary.
        var straddling = TestSequence.Create(new byte[][] { new byte[] { 0 }, new byte[] { 3, 10, 11, 12 } });
        var request = filter.Filter(straddling, out var consumed, out _);

        Assert.True(request != null, "a header split across a segment boundary should still be read");
        AssertBody(new byte[] { 10, 11, 12 }, request!.Body, "the body should follow the split header");
        Assert.Equal(5L, straddling.Slice(0, consumed).Length, "consumed should cover header and body");

        // A zero-length body is a complete request too.
        var emptyBody = TestSequence.Create(new byte[][] { new byte[] { 0, 0 } });
        request = filter.Filter(emptyBody, out consumed, out _);

        Assert.True(request != null, "a zero-length body should still produce a request");
        Assert.Equal(0, request!.Body.Length, "the body should be empty");
        Assert.Equal(2L, emptyBody.Slice(0, consumed).Length, "consumed should cover the header only");

        // Three pipelined requests, with the last one split across the segment boundary.
        var pipelined = TestSequence.Create(new byte[][]
        {
            new byte[] { 0, 1, 0xA0 },
            new byte[] { 0, 2, 0xB0, 0xB1, 0, 1 },
            new byte[] { 0xC0 },
        });

        var bodies = new List<byte[]>();
        var current = pipelined;

        while (true)
        {
            var parsed = filter.Filter(current, out var stepConsumed, out _);

            if (parsed == null)
                break;

            bodies.Add(parsed.Body);
            current = current.Slice(stepConsumed);
        }

        Assert.Equal(3, bodies.Count, "all three pipelined requests should be parsed");
        AssertBody(new byte[] { 0xA0 }, bodies[0], "first pipelined body");
        AssertBody(new byte[] { 0xB0, 0xB1 }, bodies[1], "second pipelined body");
        AssertBody(new byte[] { 0xC0 }, bodies[2], "third pipelined body");
        Assert.Equal(0L, current.Length, "the buffer should be fully consumed");
    }

    private static void AssertBody(byte[] expected, byte[] actual, string message)
    {
        Assert.Equal(expected.Length, actual.Length, message);

        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], $"{message} (byte {i})");
    }

    /// <summary>
    /// TODO-09: EnqueueAsync must park while the queue is full and resume once it drains.
    /// </summary>
    public static void SendQueueEnqueueAsyncWaitsForSpace()
    {
        var queue = SendingQueueAccessor.Create(1);

        Assert.True(queue.TryEnqueue(new ArraySegment<byte>(new byte[] { 1 })), "the first item should fit");
        Assert.True(!queue.TryEnqueue(new ArraySegment<byte>(new byte[] { 2 })), "the queue should now be full");

        var pending = queue.EnqueueAsync(new ArraySegment<byte>(new byte[] { 2 }), CancellationToken.None);

        Assert.True(!pending.IsCompleted, "EnqueueAsync should park while the queue is full");

        var batch = new List<ArraySegment<byte>>();
        queue.DrainAvailable(batch);

        Assert.Equal(1, batch.Count, "the drain should release the queued item");
        Assert.True(pending.Wait(5000), "EnqueueAsync should resume once space is available");
        Assert.True(pending.Result, "EnqueueAsync should report success");

        queue.DrainAvailable(batch);
        Assert.Equal(1, batch.Count, "the awaited item should have been queued");
        Assert.Equal((byte)2, batch[0].Array![batch[0].Offset], "the awaited item should keep its payload");
    }

    /// <summary>
    /// TODO-09: a completed queue (session shutting down) must release the waiter with false, and a
    /// cancelled token must surface as a cancellation.
    /// </summary>
    public static void SendQueueEnqueueAsyncUnblocksOnCompleteAndCancel()
    {
        var queue = SendingQueueAccessor.Create(1);
        Assert.True(queue.TryEnqueue(new ArraySegment<byte>(new byte[] { 1 })), "the first item should fit");

        var pending = queue.EnqueueAsync(new ArraySegment<byte>(new byte[] { 2 }), CancellationToken.None);
        Assert.True(!pending.IsCompleted, "EnqueueAsync should park while the queue is full");

        queue.Complete();

        Assert.True(pending.Wait(5000), "completing the queue should release the waiter");
        Assert.True(!pending.Result, "a completed queue should report failure rather than hang");

        var cancelQueue = SendingQueueAccessor.Create(1);
        Assert.True(cancelQueue.TryEnqueue(new ArraySegment<byte>(new byte[] { 1 })), "the first item should fit");

        using var cts = new CancellationTokenSource();
        var cancelPending = cancelQueue.EnqueueAsync(new ArraySegment<byte>(new byte[] { 2 }), cts.Token);
        cts.Cancel();

        try
        {
            cancelPending.GetAwaiter().GetResult();
            throw new InvalidOperationException("a cancelled EnqueueAsync should throw");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void AssertSegment(byte[] expected, ArraySegment<byte> actual, string message)
    {
        Assert.Equal(expected.Length, actual.Count, message);

        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual.Array![actual.Offset + i], $"{message} (byte {i})");
    }
}

/// <summary>
/// Builds a multi-segment <see cref="ReadOnlySequence{T}"/> so filters are exercised across
/// segment boundaries.
/// </summary>
static class TestSequence
{
    public static ReadOnlySequence<byte> Create(params string[] segments)
    {
        var byteSegments = new byte[segments.Length][];

        for (var i = 0; i < segments.Length; i++)
            byteSegments[i] = Encoding.ASCII.GetBytes(segments[i]);

        return Create(byteSegments);
    }

    public static ReadOnlySequence<byte> Create(byte[][] segments)
    {
        TestSequenceSegment? first = null;
        TestSequenceSegment? last = null;
        long runningIndex = 0;

        foreach (var segment in segments)
        {
            var current = new TestSequenceSegment(segment);

            if (first == null)
                first = current;
            else
                last!.SetNext(current, runningIndex);

            last = current;
            runningIndex += segment.Length;
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class TestSequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public TestSequenceSegment(byte[] memory)
        {
            Memory = memory;
        }

        public void SetNext(TestSequenceSegment next, long runningIndex)
        {
            next.RunningIndex = runningIndex;
            Next = next;
        }
    }
}
