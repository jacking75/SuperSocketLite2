namespace SuperSocketLite.Common;

/// <summary>SearchMarkState</summary>
/// <typeparam name="T"></typeparam>
public class SearchMarkState<T>
    where T : IEquatable<T>
{
    public SearchMarkState(T[] mark)
    {
        Mark = mark;
    }

    /// <summary>Gets the mark.</summary>
    public T[] Mark { get; private set; }

    /// <summary>Gets or sets whether matched already.</summary>
    public int Matched { get; set; }
}
