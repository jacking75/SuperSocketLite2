using SuperSocketLite.LoadTest.Shared.Csv;
using SuperSocketLite.LoadTest.Client.Metrics;

namespace SuperSocketLite.LoadTest.Tests;

internal static class CsvMetricWriterTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(WritesHeaderOnlyOnce), WritesHeaderOnlyOnce);
        yield return new TestCase(nameof(EscapesCommaQuoteAndNewline), EscapesCommaQuoteAndNewline);
        yield return new TestCase(nameof(FlushPersistsUtf8CsvValues), FlushPersistsUtf8CsvValues);
        yield return new TestCase(nameof(ClientCsvWritersIncludeMachineIdColumns), ClientCsvWritersIncludeMachineIdColumns);
        yield return new TestCase(nameof(ClientCsvWritersSerializeConcurrentOperations), ClientCsvWritersSerializeConcurrentOperations);
        yield return new TestCase(nameof(ClientCsvWritersTrackDroppedOperationRows), ClientCsvWritersTrackDroppedOperationRows);
        yield return new TestCase(nameof(AnalysisSqlContainsDistributedClientViews), AnalysisSqlContainsDistributedClientViews);
    }

    private static void WritesHeaderOnlyOnce()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "metrics.csv");
        var schema = new CsvSchema("a", "b");

        using (var writer = new CsvMetricWriter(path, schema))
        {
            writer.WriteRow("1", "2");
            writer.WriteRow("3", "4");
            writer.Flush();
        }

        var lines = File.ReadAllLines(path);
        AssertEx.Equal(3, lines.Length);
        AssertEx.Equal("a,b", lines[0]);
        AssertEx.Equal(1, lines.Count(line => line == "a,b"));
    }

    private static void EscapesCommaQuoteAndNewline()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "metrics.csv");

        using (var writer = new CsvMetricWriter(path, new CsvSchema("value")))
        {
            writer.WriteRow("a,b\"c\nd");
            writer.Flush();
        }

        var content = File.ReadAllText(path);
        AssertEx.True(content.Contains("\"a,b\"\"c\nd\""), "CSV field should be quoted and escaped.");
    }

    private static void FlushPersistsUtf8CsvValues()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "metrics.csv");

        using (var writer = new CsvMetricWriter(path, new CsvSchema("text")))
        {
            writer.WriteRow("한글");
            writer.Flush();
        }

        var content = File.ReadAllText(path, System.Text.Encoding.UTF8);
        AssertEx.True(content.Contains("한글"), "UTF-8 value should round-trip.");
    }

    private static void ClientCsvWritersSerializeConcurrentOperations()
    {
        using var temp = TempDirectory.Create();
        using (var writers = new ClientCsvWriters(temp.Path))
        {
            Parallel.For(0, 500, i =>
            {
                writers.WriteOperation("run", i, i % 10, i, "echo", 101, 32, i, i + 1, 100, true, string.Empty, string.Empty);
            });

            writers.Flush();
        }

        var lines = File.ReadAllLines(Path.Combine(temp.Path, "client_operations.csv"));
        AssertEx.Equal(501, lines.Length);
        foreach (var line in lines.Skip(1))
        {
            AssertEx.Equal(15, line.Split(',').Length, "Each operation row should have the full CSV column count.");
        }
    }

    private static void ClientCsvWritersIncludeMachineIdColumns()
    {
        using var temp = TempDirectory.Create();
        using (var writers = new ClientCsvWriters(temp.Path))
        {
            writers.WriteSummary("run", "created", true);
            writers.Flush();
        }

        var samplesHeader = File.ReadLines(Path.Combine(temp.Path, "client_samples.csv")).First();
        var operationsHeader = File.ReadLines(Path.Combine(temp.Path, "client_operations.csv")).First();
        var summaryHeader = File.ReadLines(Path.Combine(temp.Path, "client_summary.csv")).First();

        AssertEx.True(samplesHeader.Contains("machine_id"), "client_samples.csv should include machine_id.");
        AssertEx.True(operationsHeader.Contains("machine_id"), "client_operations.csv should include machine_id.");
        AssertEx.True(summaryHeader.Contains("machine_id"), "client_summary.csv should include machine_id.");
    }

    private static void AnalysisSqlContainsDistributedClientViews()
    {
        var sql = File.ReadAllText(Path.Combine("Test", "LoadTest", "analysis", "duckdb_loadtest.sql"));

        AssertEx.True(sql.Contains("analysis_client_machine_summary"), "DuckDB SQL should expose a per-client-machine summary view.");
        AssertEx.True(sql.Contains("analysis_distributed_client_throughput"), "DuckDB SQL should expose a distributed client throughput view.");
        AssertEx.True(sql.Contains("distributed_client_samples"), "DuckDB SQL should aggregate client samples before throughput analysis.");
        AssertEx.True(sql.Contains("elapsed_bucket_ms"), "DuckDB SQL should align distributed samples with time buckets.");
        AssertEx.True(sql.Contains("analysis_smoke_verdict"), "DuckDB SQL should expose an automated smoke verdict view.");
        AssertEx.True(sql.Contains("machine_id"), "DuckDB SQL should preserve machine_id in distributed analysis.");
    }

    private static void ClientCsvWritersTrackDroppedOperationRows()
    {
        using var temp = TempDirectory.Create();
        using var writers = new ClientCsvWriters(temp.Path, operationChannelCapacity: 1, autoStartOperationWriter: false);

        writers.WriteOperation("run", 0, 1, 1, "echo", 101, 32, 0, 1, 100, true, string.Empty, string.Empty);
        writers.WriteOperation("run", 0, 1, 2, "echo", 101, 32, 0, 1, 100, true, string.Empty, string.Empty);

        AssertEx.True(writers.DroppedOperationRows > 0, "Full operation queue should increment dropped operation rows.");
    }
}
