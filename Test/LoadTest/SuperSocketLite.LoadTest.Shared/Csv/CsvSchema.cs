namespace SuperSocketLite.LoadTest.Shared.Csv;

public sealed class CsvSchema
{
    public CsvSchema(params string[] columns)
    {
        if (columns.Length == 0)
            throw new ArgumentException("CSV schema must contain at least one column.", nameof(columns));

        Columns = columns.ToArray();
    }

    public IReadOnlyList<string> Columns { get; }
}
