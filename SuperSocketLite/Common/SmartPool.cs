using System.Collections.Concurrent;

namespace SuperSocketLite.Common;

/// <summary>
/// A lock-free object pool that is pre-filled with <c>minPoolSize</c> items and doubles its
/// capacity on demand, up to <c>maxPoolSize</c>.
/// </summary>
/// <typeparam name="T">The pooled item type.</typeparam>
public sealed class SmartPool<T>
{
    private readonly ConcurrentStack<T> _stack = new();

    private readonly Func<T> _itemCreator;

    private readonly int _maxPoolSize;

    private int _totalItemsCount;

    // 0 = nobody is growing the pool, 1 = one thread is inside Grow().
    private int _isIncreasing;

    /// <summary>Initializes the pool and creates its initial <paramref name="minPoolSize"/> items.</summary>
    /// <param name="minPoolSize">How many items to create up front.</param>
    /// <param name="maxPoolSize">The upper bound on the number of items the pool will ever create.</param>
    /// <param name="itemCreator">Creates one new pooled item.</param>
    public SmartPool(int minPoolSize, int maxPoolSize, Func<T> itemCreator)
    {
        _itemCreator = itemCreator;
        _maxPoolSize = Math.Max(maxPoolSize, minPoolSize);
        Grow(minPoolSize);
    }

    /// <summary>Returns an item to the pool.</summary>
    public void Push(T item)
    {
        _stack.Push(item);
    }

    /// <summary>
    /// Tries to take one item from the pool, growing it when it is empty and has not reached
    /// <c>maxPoolSize</c> yet.
    /// </summary>
    /// <returns>false when the pool is exhausted at its maximum size.</returns>
    public bool TryGet(out T item)
    {
        if (_stack.TryPop(out item!))
            return true;

        if (Volatile.Read(ref _totalItemsCount) >= _maxPoolSize)
            return TryPopWithWait(out item);

        //Another thread is already growing the pool; wait for it rather than growing twice.
        if (Interlocked.CompareExchange(ref _isIncreasing, 1, 0) != 0)
            return TryPopWithWait(out item);

        try
        {
            Grow(Math.Min(_totalItemsCount, _maxPoolSize - _totalItemsCount));
        }
        finally
        {
            // Interlocked gives a full memory barrier: every item pushed by Grow() must be visible
            // to other threads before they observe _isIncreasing == 0 and try to pop it.
            Interlocked.Exchange(ref _isIncreasing, 0);
        }

        return _stack.TryPop(out item!);
    }

    private void Grow(int count)
    {
        if (count <= 0)
            return;

        for (var i = 0; i < count; i++)
        {
            _stack.Push(_itemCreator());
        }

        Interlocked.Add(ref _totalItemsCount, count);
    }

    private bool TryPopWithWait(out T item)
    {
        var spinWait = new SpinWait();

        while (true)
        {
            spinWait.SpinOnce();

            if (_stack.TryPop(out item!))
                return true;

            if (spinWait.Count >= 100)
                return false;
        }
    }
}
