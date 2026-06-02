namespace SuperSocketLite.LoadTest.Tests;

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}', actual '{actual}'.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(message ?? $"Expected {expected.Count} items, actual {actual.Count}.");

        for (var i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException(message ?? $"Item {i}: expected '{expected[i]}', actual '{actual[i]}'.");
        }
    }

    public static TException Throws<TException>(Action action, string? message = null)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(message ?? $"Expected {typeof(TException).Name}, actual {ex.GetType().Name}.");
        }

        throw new InvalidOperationException(message ?? $"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
