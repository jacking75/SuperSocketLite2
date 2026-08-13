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

    /// <summary>
    /// TODO-18: ReuseLockBaseBuffer boundary behaviour after the double-copy simplification.
    /// </summary>
    public static void ReuseLockBaseBufferHandlesCommitBoundaries()
    {
        var buffer = new ReuseLockBaseBuffer(16);

        Assert.True(buffer.Copy(new byte[] { 1, 2, 3, 4, 5, 6 }, 0, 6), "the first copy should fit");

        // Partial commit keeps the leftover bytes and slides them to the front.
        buffer.Commit(2);
        AssertSegment(new byte[] { 3, 4, 5, 6 }, buffer.GetData(), "a partial commit should keep the unread bytes");

        buffer.Commit(0);
        AssertSegment(new byte[] { 3, 4, 5, 6 }, buffer.GetData(), "committing zero bytes should be a no-op");

        buffer.Commit(-5);
        AssertSegment(new byte[] { 3, 4, 5, 6 }, buffer.GetData(), "a negative commit should be a no-op");

        // Committing everything resets to the front.
        buffer.Commit(4);
        Assert.Equal(0, buffer.GetData().Count, "committing everything should empty the buffer");

        Assert.True(buffer.Copy(new byte[] { 7, 8 }, 0, 2), "the buffer should be reusable after a full commit");
        AssertSegment(new byte[] { 7, 8 }, buffer.GetData(), "the reused buffer should start from the front");

        // Committing more than is available also resets instead of corrupting the positions.
        buffer.Commit(100);
        Assert.Equal(0, buffer.GetData().Count, "an over-commit should reset the buffer");

        // The buffer refuses a copy that would exactly fill it (deliberately conservative).
        var full = new ReuseLockBaseBuffer(8);
        Assert.True(!full.Copy(new byte[8], 0, 8), "an exactly-filling copy is rejected");
        Assert.True(full.Copy(new byte[7], 0, 7), "one byte below capacity should fit");
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
