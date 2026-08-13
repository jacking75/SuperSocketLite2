using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SuperSocketLite.Common;

/// <summary>
/// A lock-free object pool that is pre-filled with <c>minPoolSize</c> items and doubles its
/// capacity on demand, up to <c>maxPoolSize</c>.
/// </summary>
/// <typeparam name="T">The pooled item type.</typeparam>
public sealed class SmartPool<T>
{
    private readonly ConcurrentStack<T> m_Stack = new ConcurrentStack<T>();

    private readonly Func<T> m_ItemCreator;

    private readonly int m_MaxPoolSize;

    private int m_TotalItemsCount;

    // 0 = nobody is growing the pool, 1 = one thread is inside Grow().
    private int m_IsIncreasing;

    /// <summary>
    /// Initializes the pool and creates its initial <paramref name="minPoolSize"/> items.
    /// </summary>
    /// <param name="minPoolSize">How many items to create up front.</param>
    /// <param name="maxPoolSize">The upper bound on the number of items the pool will ever create.</param>
    /// <param name="itemCreator">Creates one new pooled item.</param>
    public SmartPool(int minPoolSize, int maxPoolSize, Func<T> itemCreator)
    {
        m_ItemCreator = itemCreator;
        m_MaxPoolSize = Math.Max(maxPoolSize, minPoolSize);
        Grow(minPoolSize);
    }

    /// <summary>
    /// Returns an item to the pool.
    /// </summary>
    /// <param name="item">The item.</param>
    public void Push(T item)
    {
        m_Stack.Push(item);
    }

    /// <summary>
    /// Tries to take one item from the pool, growing it when it is empty and has not reached
    /// <c>maxPoolSize</c> yet.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>false when the pool is exhausted at its maximum size.</returns>
    public bool TryGet(out T item)
    {
        if (m_Stack.TryPop(out item!))
            return true;

        if (Volatile.Read(ref m_TotalItemsCount) >= m_MaxPoolSize)
            return TryPopWithWait(out item);

        //Another thread is already growing the pool; wait for it rather than growing twice.
        if (Interlocked.CompareExchange(ref m_IsIncreasing, 1, 0) != 0)
            return TryPopWithWait(out item);

        try
        {
            Grow(Math.Min(m_TotalItemsCount, m_MaxPoolSize - m_TotalItemsCount));
        }
        finally
        {
            // Interlocked gives a full memory barrier: every item pushed by Grow() must be visible
            // to other threads before they observe m_IsIncreasing == 0 and try to pop it.
            Interlocked.Exchange(ref m_IsIncreasing, 0);
        }

        return m_Stack.TryPop(out item!);
    }

    private void Grow(int count)
    {
        if (count <= 0)
            return;

        for (var i = 0; i < count; i++)
        {
            m_Stack.Push(m_ItemCreator());
        }

        Interlocked.Add(ref m_TotalItemsCount, count);
    }

    private bool TryPopWithWait(out T item)
    {
        var spinWait = new SpinWait();

        while (true)
        {
            spinWait.SpinOnce();

            if (m_Stack.TryPop(out item!))
                return true;

            if (spinWait.Count >= 100)
                return false;
        }
    }
}
