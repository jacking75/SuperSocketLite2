# Load Test DuckDB Analysis

This directory contains DuckDB SQL for analyzing CSV files produced by the SuperSocketLite load test server and client.

## Expected CSV Location

Run the load test tools from the repository root and write output under:

```text
logs/loadtest/<run-id>/
```

The analysis SQL expects these files when available:

```text
logs/loadtest/<run-id>/server_samples.csv
logs/loadtest/<run-id>/server_events.csv
logs/loadtest/<run-id>/client_samples.csv
logs/loadtest/<run-id>/client_operations.csv
```

The `read_csv_auto` views use `union_by_name = true`, so runs with schema additions can still be analyzed together when column names match.

## Start DuckDB

From the repository root:

```powershell
duckdb loadtest.duckdb -init Test\LoadTest\analysis\duckdb_loadtest.sql
```

The init script creates raw CSV views:

```text
server_samples
server_events
client_samples
client_operations
```

It also creates analysis views:

```text
analysis_throughput
analysis_latency
analysis_client_machine_summary
analysis_distributed_client_throughput
analysis_server_handler_latency
analysis_memory_trend
analysis_error_summary
analysis_server_event_summary
analysis_session_leak_check
analysis_smoke_verdict
```

## Common Queries

```sql
SELECT * FROM analysis_throughput;
SELECT * FROM analysis_latency;
SELECT * FROM analysis_client_machine_summary;
SELECT * FROM analysis_distributed_client_throughput;
SELECT * FROM analysis_memory_trend;
SELECT * FROM analysis_error_summary;
SELECT * FROM analysis_session_leak_check;
SELECT * FROM analysis_smoke_verdict;
```

For one run:

```sql
SELECT *
FROM analysis_latency
WHERE run_id = 'baseline-1000';
```

To inspect the raw samples:

```sql
SELECT timestamp_utc, active_sessions, requests_per_sec, working_set_bytes
FROM server_samples
ORDER BY run_id, elapsed_ms
LIMIT 100;
```

If DuckDB reports that a glob has no matching files, generate the corresponding CSV first or temporarily remove that view from the init script.

## Distributed Client Runs

For multi-machine client runs, copy each client machine's output directory under `logs/loadtest` without merging CSV files.
The analysis SQL groups client samples by `run_id`, `machine_id`, and a one-second `elapsed_bucket_ms` so samples from independent processes can be compared on the same timeline.

Useful distributed queries:

```sql
SELECT *
FROM analysis_client_machine_summary
WHERE run_id = 'dist-20260602-001';

SELECT *
FROM analysis_distributed_client_throughput
WHERE run_id = 'dist-20260602-001'
ORDER BY elapsed_bucket_ms;
```

## Smoke Verdict

`analysis_smoke_verdict` provides a simple pass/fail summary for smoke runs. It checks client/server errors, dropped operation rows, session leak indicators, and p99 RTT under 50 ms.

```sql
SELECT *
FROM analysis_smoke_verdict
WHERE run_id = 'smoke-run-id';
```
