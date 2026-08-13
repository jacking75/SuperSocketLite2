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
