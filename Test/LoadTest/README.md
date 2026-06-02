# SuperSocketLite Load Test

This directory contains the planned load test solution for SuperSocketLite. The load test tools are intended to run a local instrumented server, drive TCP or UDP dummy clients, write CSV metrics, and analyze those CSVs with DuckDB.

For build commands and Korean usage instructions, see [BUILD_AND_USAGE.md](BUILD_AND_USAGE.md).

The current commands below document the intended workflow from the stability test plan. They require the load test server and client implementations to support the listed options.

## Smoke Test

Use the smoke test before longer runs. It should complete quickly and verify basic connectivity, CSV output, and low local latency.

Expected smoke criteria:

```text
clients: 100
duration: 5 minutes
expected: 0 unhandled server exceptions, 0% error rate, p99 RTT < 50 ms on a typical local machine
```

Start the server in one PowerShell window:

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Server -- `
  --port 2012 `
  --max-connections 1000 `
  --output logs\loadtest\smoke-server `
  --duration 00:06:00
```

Start the client in another PowerShell window:

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 100 `
  --ramp-up 00:00:10 `
  --duration 00:05:00 `
  --send-rate-per-client 1.0 `
  --operation-sampling 1.0 `
  --output logs\loadtest\smoke-client
```

After the client exits, stop the server cleanly and confirm CSV files were written under `logs\loadtest\smoke-server` and `logs\loadtest\smoke-client`.
When using `--duration`, the server exits gracefully after the specified interval and flushes final session close events.

## Recommended Runs

### Baseline

Use baseline runs to compare future changes against a stable reference.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 1000 `
  --ramp-up 00:02:00 `
  --duration 00:30:00 `
  --send-rate-per-client 1.0 `
  --payload mixed `
  --output logs\loadtest\baseline-1000
```

Recommended baseline criteria:

```text
connect fail < 0.1%
request timeout < 0.1%
server exception count = 0
```

### Stress

Use stress runs to increase load until the host approaches capacity. Raise `--clients` gradually and keep each run long enough to observe steady behavior.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 5000 `
  --ramp-up 00:05:00 `
  --duration 00:45:00 `
  --send-rate-per-client 1.0 `
  --payload mixed `
  --output logs\loadtest\stress-5000
```

Recommended stress criteria:

```text
server degrades gracefully near capacity
no process crash
no unbounded memory growth
no permanently stuck sessions after clients stop
```

### Soak

Use soak runs to detect memory growth, GC pressure, and slow session leaks. Pick a client count around 50-80% of the stable operating target.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 5000 `
  --ramp-up 00:10:00 `
  --duration 06:00:00 `
  --send-rate-per-client 0.5 `
  --scenario game-like `
  --output logs\loadtest\soak-5000
```

Recommended soak criteria:

```text
working set and GC heap stabilize after ramp-up
active sessions return close to zero after client shutdown
request timeout and p99 RTT do not trend upward without recovery
```

## CSV Files

All CSV files should be UTF-8, comma-delimited, and include a header row. Timestamps should include ISO-8601 UTC values and elapsed milliseconds for time-series analysis.

### `server_samples.csv`

Periodic server metrics, normally one row per second.

Important columns:

```text
timestamp_utc, elapsed_ms, run_id, server_name, process_id
active_sessions, total_connected, total_closed
total_requests, requests_per_sec
total_bytes_in, bytes_in_per_sec, total_bytes_out, bytes_out_per_sec
send_fail_total, exception_total, protocol_error_total
gc_gen0_total, gc_gen1_total, gc_gen2_total
gc_heap_bytes, working_set_bytes, private_memory_bytes
thread_count, threadpool_worker_available, threadpool_worker_max
threadpool_io_available, threadpool_io_max, cpu_percent
handler_latency_p50_us, handler_latency_p95_us, handler_latency_p99_us, handler_latency_max_us
```

### `server_events.csv`

Server event records for connect, close, and error events. Request events should be sampled if enabled because writing every request can become the bottleneck.

Enable request event sampling with:

```powershell
--server-event-request-sampling 0.001
```

Use `1.0` only for small debugging runs.

Important columns:

```text
timestamp_utc, elapsed_ms, run_id, event_type
session_id, remote_endpoint, packet_id
bytes_in, bytes_out, close_reason, error_type, message
```

### `client_samples.csv`

Periodic aggregate client metrics.

Important columns:

```text
timestamp_utc, elapsed_ms, run_id
active_clients, connecting_clients, connected_clients, closed_clients, reconnecting_clients
total_connect_success, total_connect_fail, total_disconnect
total_send_success, total_send_fail, total_receive, total_timeout
send_per_sec, receive_per_sec, bytes_sent_per_sec, bytes_received_per_sec
rtt_p50_us, rtt_p95_us, rtt_p99_us, rtt_max_us
socket_error_total, protocol_error_total
```

### `client_operations.csv`

Sampled per-operation latency records.

Use operation sampling to reduce CSV write pressure during high-load runs:

```powershell
--operation-sampling 0.01
```

Use `0.0` to keep aggregate `client_samples.csv` metrics while suppressing per-operation rows.
Use `--slow-receiver-delay-ms <milliseconds>` to delay response reads and exercise server send queues without changing client send cadence.

Important columns:

```text
timestamp_utc, elapsed_ms, run_id, client_id
operation_id, operation_type, packet_id, payload_bytes
send_start_ms, response_end_ms, rtt_us
success, error_type, socket_error
```

## DuckDB Analysis

From the repository root:

```powershell
duckdb loadtest.duckdb -init Test\LoadTest\analysis\duckdb_loadtest.sql
```

The script reads CSVs from `logs/loadtest/*/*.csv`, creates raw views, and creates analysis views for:

```text
throughput
latency
server handler latency
memory trend
error summary
server event summary
session leak check
```

Run the common reports:

```sql
SELECT * FROM analysis_throughput;
SELECT * FROM analysis_latency;
SELECT * FROM analysis_memory_trend;
SELECT * FROM analysis_error_summary;
SELECT * FROM analysis_session_leak_check;
```

Session leak checks are most useful after clients have stopped and the server has had time to drain closed sessions.

## Windows Notes

Ephemeral port exhaustion: high client counts from one Windows machine can consume local ephemeral ports or leave many sockets in `TIME_WAIT`. Increase clients gradually, reuse baselines, and consider spreading load across multiple client machines for very high connection counts.

Firewall: Windows Defender Firewall or third-party firewall tools can block listening ports or add overhead. Allow the test server port before running large tests.

Antivirus: real-time scanning can distort CSV write performance and process CPU readings, especially when writing many operation rows. Exclude the repository or `logs\loadtest` directory for controlled benchmark runs when policy allows it.

File locks: DuckDB, editors, antivirus scanners, or a still-running test process can hold CSV files open. Stop the relevant process or close the DuckDB session before deleting or overwriting a run directory.

Loopback versus NIC tests: `127.0.0.1` removes network hardware from the test and is useful for smoke and CPU-bound comparisons. Remote client machines are better for validating real network throughput and connection churn.
