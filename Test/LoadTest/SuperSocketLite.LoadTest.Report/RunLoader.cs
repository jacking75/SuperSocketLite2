namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 로그 디렉토리 아래의 CSV를 실행(run_id) 단위로 모읍니다.
/// </summary>
/// <remarks>
/// 서버와 클라이언트는 보통 서로 다른 디렉토리에 쓰고, 분산 실행에서는 머신마다 디렉토리가 또 나뉩니다.
/// 디렉토리 이름이 아니라 CSV 안의 run_id로 묶어야 이 조각들이 하나의 실행으로 합쳐집니다.
/// </remarks>
public static class RunLoader
{
    public static IReadOnlyList<RunData> LoadAll(string root)
    {
        if (!Directory.Exists(root))
            return [];

        var summaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var clientSamples = new Dictionary<string, List<ClientSample>>(StringComparer.Ordinal);
        var serverSamples = new Dictionary<string, List<ServerSample>>(StringComparer.Ordinal);
        var directories = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            ReadClientSummary(directory, summaries, directories);
            ReadClientSamples(directory, clientSamples, directories);
            ReadServerSamples(directory, serverSamples, directories);
        }

        var runIds = new SortedSet<string>(StringComparer.Ordinal);
        runIds.UnionWith(summaries.Keys);
        runIds.UnionWith(clientSamples.Keys);
        runIds.UnionWith(serverSamples.Keys);

        var runs = new List<RunData>();
        foreach (var runId in runIds)
        {
            var client = clientSamples.TryGetValue(runId, out var cs) ? cs : [];
            var server = serverSamples.TryGetValue(runId, out var ss) ? ss : [];
            client.Sort((a, b) => a.ElapsedMs.CompareTo(b.ElapsedMs));
            server.Sort((a, b) => a.ElapsedMs.CompareTo(b.ElapsedMs));

            runs.Add(new RunData
            {
                RunId = runId,
                Directories = directories.TryGetValue(runId, out var dirs) ? [.. dirs] : [],
                Summary = summaries.TryGetValue(runId, out var summary) ? summary : new Dictionary<string, string>(),
                ClientSamples = client,
                ServerSamples = server
            });
        }

        return runs;
    }

    private static void ReadClientSummary(
        string directory,
        Dictionary<string, Dictionary<string, string>> summaries,
        Dictionary<string, SortedSet<string>> directories)
    {
        var table = CsvTable.TryLoad(Path.Combine(directory, "client_summary.csv"));
        if (table is null)
            return;

        foreach (var row in table.Rows)
        {
            var runId = table.GetString(row, "run_id");
            if (runId.Length == 0)
                continue;

            Track(directories, runId, directory);

            if (!summaries.TryGetValue(runId, out var summary))
            {
                summary = new Dictionary<string, string>(StringComparer.Ordinal);
                summaries[runId] = summary;
            }

            var key = table.GetString(row, "key");
            if (key.Length == 0)
                continue;

            summary[key] = table.GetString(row, "value");
        }
    }

    private static void ReadClientSamples(
        string directory,
        Dictionary<string, List<ClientSample>> samples,
        Dictionary<string, SortedSet<string>> directories)
    {
        var table = CsvTable.TryLoad(Path.Combine(directory, "client_samples.csv"));
        if (table is null)
            return;

        foreach (var row in table.Rows)
        {
            var runId = table.GetString(row, "run_id");
            if (runId.Length == 0)
                continue;

            Track(directories, runId, directory);

            if (!samples.TryGetValue(runId, out var list))
            {
                list = [];
                samples[runId] = list;
            }

            list.Add(new ClientSample(
                table.GetLong(row, "elapsed_ms"),
                // phase가 없던 시절의 실행은 unknown으로 두고 부하 구간으로 취급한다.
                table.GetString(row, "phase", "unknown"),
                table.GetLong(row, "active_clients"),
                table.GetDouble(row, "send_per_sec"),
                table.GetDouble(row, "receive_per_sec"),
                table.GetLong(row, "rtt_p99_us"),
                table.GetLong(row, "total_timeout"),
                table.GetLong(row, "total_send_fail")));
        }
    }

    private static void ReadServerSamples(
        string directory,
        Dictionary<string, List<ServerSample>> samples,
        Dictionary<string, SortedSet<string>> directories)
    {
        var table = CsvTable.TryLoad(Path.Combine(directory, "server_samples.csv"));
        if (table is null)
            return;

        foreach (var row in table.Rows)
        {
            var runId = table.GetString(row, "run_id");
            if (runId.Length == 0)
                continue;

            Track(directories, runId, directory);

            if (!samples.TryGetValue(runId, out var list))
            {
                list = [];
                samples[runId] = list;
            }

            list.Add(new ServerSample(
                table.GetLong(row, "elapsed_ms"),
                table.GetString(row, "phase", "unknown"),
                table.GetLong(row, "active_sessions"),
                table.GetDouble(row, "requests_per_sec"),
                table.GetLong(row, "working_set_bytes"),
                table.GetLong(row, "gc_heap_bytes"),
                table.GetLong(row, "exception_total"),
                table.GetLong(row, "handler_latency_p99_us"),
                table.GetDouble(row, "cpu_percent")));
        }
    }

    private static void Track(Dictionary<string, SortedSet<string>> directories, string runId, string directory)
    {
        if (!directories.TryGetValue(runId, out var set))
        {
            set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            directories[runId] = set;
        }

        set.Add(directory);
    }
}
