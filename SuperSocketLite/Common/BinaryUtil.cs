using System;
using System.Collections.Generic;


namespace SuperSocketLite.Common;

/// <summary>
/// Binary util class
/// </summary>
public static class BinaryUtil
{
    /// <summary>
    /// Searches the mark from source, carrying the partial-match count across calls in
    /// <paramref name="searchState"/> so a mark split over two reads is still found.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">The source.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="searchState">State of the search.</param>
    /// <param name="parsedLength">Length of the parsed.</param>
    /// <returns>The position of the mark, or -1 when it was not found.</returns>
    public static int SearchMark<T>(this IList<T> source, int offset, int length, SearchMarkState<T> searchState, out int parsedLength)
        where T : IEquatable<T>
    {
        int? result = SearchMark(source, offset, length, searchState.Mark, searchState.Matched, out parsedLength);

        if (!result.HasValue)
        {
            searchState.Matched = 0;
            return -1;
        }

        if (result.Value < 0)
        {
            searchState.Matched = 0 - result.Value;
            return -1;
        }

        searchState.Matched = 0;
        return result.Value;
    }

    /// <summary>
    /// Searches the mark from source.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">The source.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="searchState">State of the search.</param>
    /// <returns>The position of the mark, or -1 when it was not found.</returns>
    public static int SearchMark<T>(this IList<T> source, int offset, int length, SearchMarkState<T> searchState)
        where T : IEquatable<T>
    {
        return SearchMark(source, offset, length, searchState, out _);
    }

    /// <summary>
    /// Clones the elements in the specific range.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">The source.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns></returns>
    public static T[] CloneRange<T>(this IList<T> source, int offset, int length)
    {
        var target = new T[length];

        if (source is T[] array)
        {
            Array.Copy(array, offset, target, 0, length);
            return target;
        }

        for (int i = 0; i < length; i++)
        {
            target[i] = source[offset + i];
        }

        return target;
    }

    /// <summary>
    /// Returns the position of the mark, a negative matched count when only a prefix of the mark
    /// reached the end of the range, or null when the mark is absent.
    /// </summary>
    private static int? SearchMark<T>(IList<T> source, int offset, int length, T[] mark, int matched, out int parsedLength)
        where T : IEquatable<T>
    {
        int pos = offset;
        int endOffset = offset + length - 1;
        int matchCount = matched;
        parsedLength = 0;

        if (matched > 0)
        {
            for (int i = matchCount; i < mark.Length; i++)
            {
                if (!source[pos++].Equals(mark[i]))
                    break;

                matchCount++;

                if (pos > endOffset)
                {
                    if (matchCount == mark.Length)
                    {
                        parsedLength = mark.Length - matched;
                        return offset;
                    }
                    else
                    {
                        return (0 - matchCount);
                    }
                }
            }

            if (matchCount == mark.Length)
            {
                parsedLength = mark.Length - matched;
                return offset;
            }

            pos = offset;
            matchCount = 0;
        }

        while (true)
        {
            pos = IndexOf(source, mark[matchCount], pos, length - pos + offset);

            if (pos < 0)
                return null;

            matchCount += 1;

            for (int i = matchCount; i < mark.Length; i++)
            {
                int checkPos = pos + i;

                if (checkPos > endOffset)
                {
                    //found end, return matched chars count
                    return (0 - matchCount);
                }

                if (!source[checkPos].Equals(mark[i]))
                    break;

                matchCount++;
            }

            //found the full end mark
            if (matchCount == mark.Length)
            {
                parsedLength = pos - offset + mark.Length;
                return pos;
            }

            //Reset next round read pos
            pos += 1;
            //clear matched chars count
            matchCount = 0;
        }
    }

    private static int IndexOf<T>(IList<T> source, T target, int pos, int length)
        where T : IEquatable<T>
    {
        for (int i = pos; i < pos + length; i++)
        {
            if (source[i].Equals(target))
                return i;
        }

        return -1;
    }
}
