namespace SuperSocketLite.Common;

/// <summary>
/// A growable list of byte array segments, viewed as one logical byte range.
/// Receive filters use it to accumulate a request that arrived over several reads without
/// concatenating the buffers until the request is complete.
/// </summary>
public class ArraySegmentList
{
    /// <summary>
    /// One appended segment, tagged with the logical range [From, To] it occupies in the list.
    /// </summary>
    private sealed class Segment
    {
        public Segment(byte[] array, int offset, int count)
        {
            Array = array;
            Offset = offset;
            Count = count;
        }

        public byte[] Array { get; }

        public int Count { get; }

        public int Offset { get; }

        public int From { get; set; }

        public int To { get; set; }
    }

    private readonly List<Segment> _segments = [];

    private int _count;

    /// <summary>
    /// Gets the total number of bytes held by all segments.
    /// </summary>
    public int Count
    {
        get { return _count; }
    }

    /// <summary>
    /// Adds the segment to the list.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    public void AddSegment(byte[] array, int offset, int length)
    {
        AddSegment(array, offset, length, false);
    }

    /// <summary>
    /// Adds the segment to the list.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> the range is copied instead of referenced.</param>
    public void AddSegment(byte[] array, int offset, int length, bool toBeCopied)
    {
        if (length <= 0)
            return;

        var segment = toBeCopied
            ? new Segment(array.CloneRange(offset, length), 0, length)
            : new Segment(array, offset, length);

        segment.From = _count;
        _count += length;
        segment.To = _count - 1;

        _segments.Add(segment);
    }

    /// <summary>
    /// Clears all the segements.
    /// </summary>
    public void ClearSegements()
    {
        _segments.Clear();
        _count = 0;
    }

    /// <summary>
    /// Read all data in this list to the array data.
    /// </summary>
    /// <returns></returns>
    public byte[] ToArrayData()
    {
        return ToArrayData(0, _count);
    }

    /// <summary>
    /// Read the data in specific range to the array data.
    /// </summary>
    /// <param name="startIndex">The start index.</param>
    /// <param name="length">The length.</param>
    /// <returns></returns>
    public byte[] ToArrayData(int startIndex, int length)
    {
        var result = new byte[length];
        int from = 0, total = 0;

        var startSegmentIndex = 0;

        if (startIndex != 0)
        {
            var startSegment = QuickSearchSegment(startIndex, out startSegmentIndex);

            if (startSegment == null)
                throw new IndexOutOfRangeException();

            from = startIndex - startSegment.From;
        }

        for (var i = startSegmentIndex; i < _segments.Count; i++)
        {
            var currentSegment = _segments[i];
            var len = Math.Min(currentSegment.Count - from, length - total);
            Array.Copy(currentSegment.Array, currentSegment.Offset + from, result, total, len);
            total += len;

            if (total >= length)
                break;

            from = 0;
        }

        return result;
    }

    /// <summary>
    /// Copies a range of this list into the target array.
    /// </summary>
    /// <param name="to">The target array.</param>
    /// <param name="srcIndex">The start index in this list.</param>
    /// <param name="toIndex">The start index in the target array.</param>
    /// <param name="length">The number of bytes to copy.</param>
    /// <returns>The number of bytes copied.</returns>
    public int CopyTo(byte[] to, int srcIndex, int toIndex, int length)
    {
        int copied = 0;
        int offsetSegmentIndex;
        Segment? offsetSegment;

        if (srcIndex > 0)
        {
            offsetSegment = QuickSearchSegment(srcIndex, out offsetSegmentIndex);
        }
        else
        {
            offsetSegment = _segments[0];
            offsetSegmentIndex = 0;
        }

        int thisOffset = srcIndex - offsetSegment!.From + offsetSegment.Offset;
        int thisCopied = Math.Min(offsetSegment.Count - thisOffset + offsetSegment.Offset, length);

        Array.Copy(offsetSegment.Array, thisOffset, to, toIndex, thisCopied);

        copied += thisCopied;

        if (copied >= length)
            return copied;

        for (var i = offsetSegmentIndex + 1; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            thisCopied = Math.Min(segment.Count, length - copied);
            Array.Copy(segment.Array, segment.Offset, to, copied + toIndex, thisCopied);
            copied += thisCopied;

            if (copied >= length)
                break;
        }

        return copied;
    }

    /// <summary>
    /// Drops the last <paramref name="trimSize"/> bytes from the list.
    /// </summary>
    /// <param name="trimSize">Size of the trim.</param>
    public void TrimEnd(int trimSize)
    {
        if (trimSize <= 0)
            return;

        int expectedTo = _count - trimSize - 1;

        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            var s = _segments[i];

            if (s.From <= expectedTo && expectedTo < s.To)
            {
                s.To = expectedTo;
                _count -= trimSize;
                return;
            }

            RemoveSegmentAt(i);
        }
    }

    private void RemoveSegmentAt(int index)
    {
        var removedSegment = _segments[index];
        int removedLen = removedSegment.To - removedSegment.From + 1;

        _segments.RemoveAt(index);

        //the removed item is not the last item
        for (int i = index; i < _segments.Count; i++)
        {
            _segments[i].From -= removedLen;
            _segments[i].To -= removedLen;
        }

        _count -= removedLen;
    }

    /// <summary>
    /// Binary searches for the segment holding the given logical index.
    /// </summary>
    private Segment? QuickSearchSegment(int index, out int segmentIndex)
    {
        int from = 0;
        int to = _segments.Count - 1;

        while (from <= to)
        {
            int middle = from + (to - from) / 2;
            var segment = _segments[middle];

            if (index < segment.From)
                to = middle - 1;
            else if (index > segment.To)
                from = middle + 1;
            else
            {
                segmentIndex = middle;
                return segment;
            }
        }

        segmentIndex = -1;
        return null;
    }
}
