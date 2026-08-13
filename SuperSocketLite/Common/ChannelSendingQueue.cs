using System.Threading.Channels;

namespace SuperSocketLite.Common;

/// <summary>
/// One entry of the sending queue. A single-segment send is stored inline; a multi-segment send
/// keeps its segments in one entry so that it occupies exactly one channel slot and can therefore
/// be enqueued atomically without a lock.
/// </summary>
internal readonly struct SendItem
{
    private readonly ArraySegment<byte> _segment;
    private readonly ArraySegment<byte>[]? _segments;
    private readonly byte[]? _pooledBuffer;

    public SendItem(ArraySegment<byte> segment)
        : this(segment, null)
    {
    }

    /// <param name="segment">The payload to send.</param>
    /// <param name="pooledBuffer">
    /// The <see cref="System.Buffers.ArrayPool{T}"/> array backing <paramref name="segment"/>, or
    /// null when the payload is owned by the caller. A pooled array is returned to the pool once
    /// the whole drained batch has finished sending.
    /// </param>
    public SendItem(ArraySegment<byte> segment, byte[]? pooledBuffer)
    {
        _segment = segment;
        _segments = null;
        _pooledBuffer = pooledBuffer;
    }

    public SendItem(IList<ArraySegment<byte>> segments)
    {
        _segment = default;
        _pooledBuffer = null;

        // The segments are copied out of the caller's list on purpose: the caller is free to reuse
        // its list as soon as the enqueue returns, exactly like before this queue stored whole
        // batches. Only the byte arrays themselves stay shared (see the Send zero-copy caution).
        var copy = new ArraySegment<byte>[segments.Count];

        for (var i = 0; i < copy.Length; i++)
            copy[i] = segments[i];

        _segments = copy;
    }

    public void AppendTo(List<ArraySegment<byte>> target, List<byte[]> pooledBuffers)
    {
        if (_pooledBuffer != null)
            pooledBuffers.Add(_pooledBuffer);

        var segments = _segments;

        if (segments == null)
        {
            target.Add(_segment);
            return;
        }

        for (var i = 0; i < segments.Length; i++)
            target.Add(segments[i]);
    }
}

/// <summary>
/// Bounded, lock-free sending queue for a single session.
/// </summary>
/// <remarks>
/// <para>
/// The bounded <see cref="Channel{T}"/> already rejects writes atomically once it is full or
/// completed, so no external lock is needed.
/// </para>
/// <para>
/// <see cref="Count"/> is advisory: it can briefly disagree with the channel because the counter
/// and the channel are updated in two steps. That is safe because
/// <c>SocketSession.OnSendingCompleted</c> re-checks the count after clearing the sending flag,
/// which absorbs a transient "queue looks empty" reading.
/// </para>
/// <para>
/// The capacity counts queued <em>send operations</em>, not individual segments: a multi-segment
/// send takes one slot.
/// </para>
/// </remarks>
internal sealed class ChannelSendingQueue
{
    private readonly Channel<SendItem> _channel;
    private int _count;

    public ChannelSendingQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _channel = Channel.CreateBounded<SendItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public int Count => Volatile.Read(ref _count);

    public bool TryEnqueue(ArraySegment<byte> item)
    {
        return TryEnqueue(new SendItem(item));
    }

    public bool TryEnqueue(IList<ArraySegment<byte>> items)
    {
        if (items.Count == 0)
            return true;

        return TryEnqueue(new SendItem(items));
    }

    public bool TryEnqueue(SendItem item)
    {
        if (!_channel.Writer.TryWrite(item))
            return false;

        Interlocked.Increment(ref _count);
        return true;
    }

    /// <summary>
    /// Waits asynchronously for room in the queue and then enqueues <paramref name="item"/>.
    /// </summary>
    /// <returns>
    /// false once the queue has been completed (the session is shutting down); otherwise true.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled while waiting.
    /// </exception>
    public async ValueTask<bool> EnqueueAsync(SendItem item, CancellationToken cancellationToken)
    {
        while (await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Writer.TryWrite(item))
            {
                Interlocked.Increment(ref _count);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves every currently queued segment into <paramref name="into"/> and every pooled backing
    /// array into <paramref name="pooledBuffers"/>. Both lists are cleared first.
    /// </summary>
    /// <remarks>
    /// Callers pass per-session lists that are reused across sends; sending is single-flight per
    /// session (the InSending state flag), so the previous batch is always detached from the socket
    /// before the next drain refills the lists.
    /// </remarks>
    public void DrainAvailable(List<ArraySegment<byte>> into, List<byte[]> pooledBuffers)
    {
        into.Clear();
        pooledBuffers.Clear();

        while (_channel.Reader.TryRead(out var item))
        {
            Interlocked.Decrement(ref _count);
            item.AppendTo(into, pooledBuffers);
        }
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}
