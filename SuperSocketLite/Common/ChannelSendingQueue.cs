using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace SuperSocketLite.Common;

/// <summary>
/// One entry of the sending queue. A single-segment send is stored inline; a multi-segment send
/// keeps its segments in one entry so that it occupies exactly one channel slot and can therefore
/// be enqueued atomically without a lock.
/// </summary>
internal readonly struct SendItem
{
    private readonly ArraySegment<byte> m_Segment;
    private readonly ArraySegment<byte>[]? m_Segments;

    public SendItem(ArraySegment<byte> segment)
    {
        m_Segment = segment;
        m_Segments = null;
    }

    public SendItem(IList<ArraySegment<byte>> segments)
    {
        m_Segment = default;

        // The segments are copied out of the caller's list on purpose: the caller is free to reuse
        // its list as soon as the enqueue returns, exactly like before this queue stored whole
        // batches. Only the byte arrays themselves stay shared (see the Send zero-copy caution).
        var copy = new ArraySegment<byte>[segments.Count];

        for (var i = 0; i < copy.Length; i++)
            copy[i] = segments[i];

        m_Segments = copy;
    }

    public void AppendTo(List<ArraySegment<byte>> target)
    {
        var segments = m_Segments;

        if (segments == null)
        {
            target.Add(m_Segment);
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
    private readonly Channel<SendItem> m_Channel;
    private int m_Count;

    public ChannelSendingQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        m_Channel = Channel.CreateBounded<SendItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public int Count => Volatile.Read(ref m_Count);

    public bool TryEnqueue(ArraySegment<byte> item)
    {
        if (!m_Channel.Writer.TryWrite(new SendItem(item)))
            return false;

        Interlocked.Increment(ref m_Count);
        return true;
    }

    public bool TryEnqueue(IList<ArraySegment<byte>> items)
    {
        if (items.Count == 0)
            return true;

        if (!m_Channel.Writer.TryWrite(new SendItem(items)))
            return false;

        Interlocked.Increment(ref m_Count);
        return true;
    }

    /// <summary>
    /// Moves every currently queued segment into <paramref name="into"/>, which is cleared first.
    /// </summary>
    /// <remarks>
    /// Callers pass a per-session list that is reused across sends; sending is single-flight per
    /// session (the InSending state flag), so the previous batch is always detached from the socket
    /// before the next drain refills the list.
    /// </remarks>
    public void DrainAvailable(List<ArraySegment<byte>> into)
    {
        into.Clear();

        while (m_Channel.Reader.TryRead(out var item))
        {
            Interlocked.Decrement(ref m_Count);
            item.AppendTo(into);
        }
    }

    public void Complete()
    {
        m_Channel.Writer.TryComplete();
    }
}
