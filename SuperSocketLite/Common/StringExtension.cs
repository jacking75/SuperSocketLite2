namespace SuperSocketLite.Common;

/// <summary>
/// String extension class
/// </summary>
public static class StringExtension
{
    /// <summary>
    /// Converts string to int32.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>0 when the string is not a valid int32.</returns>
    public static int ToInt32(this string source)
    {
        return source.ToInt32(0);
    }

    /// <summary>
    /// Converts string to int32.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="defaultValue">The default value, returned when the string is not a valid int32.</param>
    /// <returns></returns>
    public static int ToInt32(this string source, int defaultValue)
    {
        return int.TryParse(source, out int value) ? value : defaultValue;
    }
}
