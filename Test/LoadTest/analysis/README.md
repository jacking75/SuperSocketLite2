# Load Test DuckDB Analysis

This directory contains DuckDB SQL for analyzing CSV files produced by the SuperSocketLite load test server and client.

## Expected CSV Location

Run the load test tools from the repository root and write output under `logs/loadtest/`, one
directory per process. A run is not one directory: `run-loadtest.ps1` gives the server and the
client each their own, and a fault-injection run adds a third for the restarted server.

```text
logs/loadtest/<run-id>-server/
logs/loadtest/<run-id>-client/
logs/loadtest/<run-id>-server-restart/   (-KillServerAt runs only)
```

The split costs nothing here. The views glob `logs/loadtest/*/<file>.csv`, each CSV name appears
in only one kind of directory, and every row carries `run_id`, so the pieces join back into one
run. Output written by hand into a single `logs/loadtest/<run-id>/` directory works the same way.

The analysis SQL expects these files when available:

```text
<run directory>/server_samples.csv
<run directory>/server_events.csv
<run directory>/client_samples.csv
<run directory>/client_operations.csv
<run directory>/client_summary.csv
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
client_summary
```

It also creates analysis views:

```text
analysis_run_summary
analysis_throughput
analysis_throughput_all_phases
analysis_phase_breakdown
analysis_latency
analysis_client_machine_summary
analysis_distributed_client_throughput
analysis_server_handler_latency
analysis_server_backpressure
analysis_memory_trend
analysis_error_summary
analysis_server_event_summary
analysis_session_leak_check
analysis_smoke_verdict
```

## Steady-Phase Filtering

Sample rows carry a `phase` column: `rampup`, `steady`, `rampdown`, or `idle`. Aggregate views
average only `steady` rows.

This matters more than it sounds. A server left running past its clients records a long idle
tail, and averaging it in halves the reported throughput. On a local 100-client run the same
data reads as 483 requests/sec over the steady phase and 206 requests/sec over every row.

`analysis_throughput_all_phases` keeps the unfiltered figures when you want to see what ramp-up
and drain cost. `analysis_phase_breakdown` shows how many samples each phase produced; a run
with zero steady samples produced no comparable measurement.

Runs recorded before the column existed report `unknown` and are treated as load-bearing, so
older results still appear in the aggregates rather than silently vanishing.

## Runtime Gauges

`analysis_server_backpressure` reports the send-queue depth and SAEA pool headroom the server
observed during the steady phase. A queue that stays deep means the server accepts sends faster
than the socket drains them; a pool whose available count reaches zero means new connections are
about to be refused.

These columns come from the SuperSocketLite meter, so they are absent from runs recorded before
the gauges existed and are written as `-1` by runs started with `--metrics no-gauges` or
`--metrics off`. Both cases normalize to `-1` and are excluded from the aggregates. Check
`instrumented_samples` before reading the row: zero means the run carries no gauges, which is not
the same as "the queues were empty".

## Common Queries

```sql
SELECT * FROM analysis_run_summary;
SELECT * FROM analysis_phase_breakdown;
SELECT * FROM analysis_throughput;
SELECT * FROM analysis_latency;
SELECT * FROM analysis_server_backpressure;
SELECT * FROM analysis_client_machine_summary;
SELECT * FROM analysis_distributed_client_throughput;
SELECT * FROM analysis_memory_trend;
SELECT * FROM analysis_error_summary;
SELECT * FROM analysis_session_leak_check;
SELECT * FROM analysis_smoke_verdict;
```

Start with `analysis_run_summary`. Its `rtt_*_ms` columns come from the client's cumulative
histogram rather than from sampled rows, so they hold for the whole run at any
`--operation-sampling` value, and `steady_rate_achievement` tells you whether the run actually
applied the load it was asked to.

Two runs are only comparable if their `pacing` matches. Under `closed` pacing the client waits
for each response before starting the next delay, so a slower server receives less load and its
latency reads better than it is; `open` pacing sends on a fixed schedule instead.

When `steady_rate_achievement` is low, `send_delay_p99_us` and `send_skipped_in_flight` say
whether the load generator was the limit. Both near zero means the client held its schedule and
the shortfall came from elsewhere.

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
