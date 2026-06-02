using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace SuperSocketLite.Common;

internal sealed class ChannelSendingQueue
{
    private readonly Channel<ArraySegment<byte>> m_Channel;
    private readonly object m_SyncRoot = new object();
    private readonly int m_Capacity;
    private int m_Count;
    private bool m_Completed;

    public ChannelSendingQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        m_Capacity = capacity;
        m_Channel = Channel.CreateBounded<ArraySegment<byte>>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public int Count => Volatile.Read(ref m_Count);

    public bool TryEnqueue(ArraySegment<byte> item)
    {
        lock (m_SyncRoot)
        {
            if (m_Completed)
                return false;

            if (m_Count >= m_Capacity)
                return false;

            if (!m_Channel.Writer.TryWrite(item))
                return false;

            Interlocked.Increment(ref m_Count);
        }

        return true;
    }

    public bool TryEnqueue(IList<ArraySegment<byte>> items)
    {
        if (items.Count == 0)
            return true;

        lock (m_SyncRoot)
        {
            if (m_Completed)
                return false;

            if (m_Count + items.Count > m_Capacity)
                return false;

            for (var i = 0; i < items.Count; i++)
            {
                if (!m_Channel.Writer.TryWrite(items[i]))
                    return false;

                Interlocked.Increment(ref m_Count);
            }
        }

        return true;
    }

    public IList<ArraySegment<byte>> DrainAvailable()
    {
        var items = new List<ArraySegment<byte>>();

        lock (m_SyncRoot)
        {
            while (m_Channel.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref m_Count);
                items.Add(item);
            }
        }

        return items;
    }

    public void Complete()
    {
        lock (m_SyncRoot)
        {
            if (m_Completed)
                return;

            m_Completed = true;
            m_Channel.Writer.TryComplete();
        }
    }
}
