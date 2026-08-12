using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>
/// Shared helpers for <see cref="ISequenceReceiveFilter{TRequestInfo}"/> implementations that still
/// hand the matched request to a byte[] based <c>ProcessMatchedRequest</c>.
/// </summary>
internal static class SequenceFilterHelper
{
    /// <summary>
    /// Exposes <paramref name="sequence"/> as a contiguous array segment, without copying when the
    /// sequence already sits in one array-backed segment (the common case for a receive pipe).
    /// </summary>
    public static ArraySegment<byte> AsArraySegment(in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment && MemoryMarshal.TryGetArray(sequence.First, out var segment) && segment.Array != null)
            return segment;

        var data = sequence.ToArray();
        return new ArraySegment<byte>(data, 0, data.Length);
    }

    /// <summary>
    /// Compares the first <paramref name="length"/> bytes of <paramref name="sequence"/> with the
    /// beginning of <paramref name="prefix"/>.
    /// </summary>
    public static bool StartsWith(in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> prefix, int length)
    {
        if (length <= 0)
            return true;

        if (sequence.Length < length)
            return false;

        var reader = new SequenceReader<byte>(sequence);

        for (var i = 0; i < length; i++)
        {
            if (!reader.TryRead(out var value) || value != prefix[i])
                return false;
        }

        return true;
    }
}
