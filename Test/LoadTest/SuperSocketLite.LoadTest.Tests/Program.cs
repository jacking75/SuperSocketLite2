namespace SuperSocketLite.LoadTest.Tests;

internal sealed record TestCase(string Name, Action Body);

internal static class Program
{
    private static int Main()
    {
        var tests = new List<TestCase>();
        tests.AddRange(BinaryPacketTests.Cases());
        tests.AddRange(CsvMetricWriterTests.Cases());
        tests.AddRange(LatencyHistogramTests.Cases());
        tests.AddRange(SharedPrimitiveTests.Cases());
        tests.AddRange(MetricsCollectorTests.Cases());
        tests.AddRange(ScenarioScheduleTests.Cases());
        tests.AddRange(LoadTestIntegrationTests.Cases());

        var failed = 0;

        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed");
        return failed == 0 ? 0 : 1;
    }
}
