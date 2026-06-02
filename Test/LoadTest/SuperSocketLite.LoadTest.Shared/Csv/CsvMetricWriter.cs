using System.Text;

namespace SuperSocketLite.LoadTest.Shared.Csv;

public sealed class CsvMetricWriter : IDisposable, IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly int _columnCount;
    private readonly StringBuilder _lineBuilder = new();
    private readonly object _syncRoot = new();
    private bool _disposed;

    public CsvMetricWriter(string path, CsvSchema schema)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _columnCount = schema.Columns.Count;
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteFields(schema.Columns);
    }

    public void WriteRow(params object?[] values)
    {
        WriteRow((IReadOnlyList<object?>)values);
    }

    public void WriteRow(IReadOnlyList<object?> values)
    {
        if (values.Count != _columnCount)
            throw new ArgumentException($"Expected {_columnCount} values, got {values.Count}.", nameof(values));

        lock (_syncRoot)
        {
            WriteFields(values);
        }
    }

    public void Flush()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.Flush();
        }
    }

    public Task FlushAsync()
    {
        Flush();
        return Task.CompletedTask;
    }

    private void WriteFields<T>(IReadOnlyList<T> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lineBuilder.Clear();
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
                _lineBuilder.Append(',');

            AppendEscaped(_lineBuilder, values[i]?.ToString() ?? string.Empty);
        }

        _writer.WriteLine(_lineBuilder.ToString());
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        var mustQuote = false;
        foreach (var c in value)
        {
            if (c is ',' or '"' or '\r' or '\n')
            {
                mustQuote = true;
                break;
            }
        }

        if (!mustQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        foreach (var c in value)
        {
            if (c == '"')
                builder.Append("\"\"");
            else
                builder.Append(c);
        }

        builder.Append('"');
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        StreamWriter? writer = null;
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            _disposed = true;
            writer = _writer;
        }

        await writer.DisposeAsync().ConfigureAwait(false);
    }
}
