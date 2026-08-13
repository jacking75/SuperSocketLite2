# SuperSocketLite Load Test

This directory contains the planned load test solution for SuperSocketLite. The load test tools are intended to run a local instrumented server, drive TCP or UDP dummy clients, write CSV metrics, and analyze those CSVs with DuckDB.

For build commands and Korean usage instructions, see [BUILD_AND_USAGE.md](BUILD_AND_USAGE.md).

The current commands below document the intended workflow from the stability test plan. They require the load test server and client implementations to support the listed options.

## Running a Test

The scripts drive server startup, load, cleanup, and reporting in one step:

```powershell
Test\LoadTest\run-loadtest.ps1 -RunId smoke -Clients 100 -Duration 00:00:30
Test\LoadTest\run-matrix.ps1 -Prefix nightly -Clients 200 -Duration 00:01:00
```

The runner waits for the listener before starting clients. A fixed sleep either races a slow
startup or, worse, lets the client run after the server's duration already expired — which once
made working code look broken.

To compare against a baseline:

```powershell
Test\LoadTest\run-loadtest.ps1 -RunId base -Repeat 3 -Clients 500 -Duration 00:02:00
# apply the change
Test\LoadTest\run-loadtest.ps1 -RunId cand -Repeat 3 -Clients 500 -Duration 00:02:00

dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Report -c Release -- `
  --baseline base --run cand --fail-on-regression
```

`--baseline` and `--run` are prefixes, so `base01`, `base02`, `base03` form one group. Tail
latency swings between runs — p99 by 44% and p99.9 by 124% in local measurements — so the
comparison uses the **median per metric** across the group. Server exceptions and leaked
sessions keep the worst value instead: a fault that happened once must not be averaged away.

A failing verdict returns exit code 1 with `--fail-on-regression`, and the run writes a
self-contained HTML report with the verdict, metrics, and time series.

Thresholds live in `thresholds.json`; `--print-thresholds` prints the defaults.

Two runs are only comparable when their `pacing` matches — closed-loop applies less load and
therefore reports lower latency. The verdict marks that case inconclusive rather than guessing.

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
phase
send_queue_depth_total, send_queue_depth_max
receive_saea_pool_available, receive_saea_pool_total
send_saea_pool_available, send_saea_pool_total
```

The last six columns are read from the `SuperSocketLite` meter rather than from server code, so
they are `-1` — not `0` — when the run carries no gauges: runs recorded before the gauges
existed, runs started with `--metrics no-gauges` or `--metrics off`, and the final sample taken
after the server has stopped. `-1` means "not measured", which is not the same as "the queue was
empty".

The `phase` column marks whether a row carries real load. The server does not know the client's
schedule, so it infers the phase from how the active session count moves:

```text
idle      no sessions attached
rampup    session count climbing
steady    session count holding
rampdown  session count falling
```

Analysis views average only `steady` rows. Including the idle tail is what makes a run's
throughput look roughly half of what it actually sustained.

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
phase
```

The RTT percentiles on these rows cover only the preceding one-second window, and that window
holds too few samples for p99 to be stable. Use `client_summary.csv` for the run-wide figures.

The client sets `phase` from its own connect schedule: `rampup` until the target client count is
reached, `steady` afterwards, and `rampdown` once the run starts shutting down.

### `client_summary.csv`

Key/value rows written once when the run ends. This is the file to read first.

```text
timestamp_utc, run_id, machine_id, key, value
```

Important keys:

```text
clients, scenario, transport, duration_ms, operation_sampling
pacing, max_in_flight
total_connect_success, total_connect_fail, total_send_success, total_send_fail
total_receive, total_timeout, socket_error_total, protocol_error_total, runtime_error_total
send_success_rate, response_rate
rtt_total_count, rtt_total_p50_us, rtt_total_p90_us, rtt_total_p95_us
rtt_total_p99_us, rtt_total_p999_us, rtt_total_max_us
target_send_rate_per_sec, steady_window_ms, steady_send_rate_per_sec, steady_rate_achievement
send_schedule_delay_p50_us, send_schedule_delay_p99_us, send_schedule_delay_max_us
send_skipped_in_flight, max_in_flight_observed
```

The `rtt_total_*` values come from a histogram that counts every response for the whole run.
They are therefore unaffected by `--operation-sampling`: a run sampled at `0.01` writes 1% of
the rows to `client_operations.csv` but still reports percentiles over 100% of the responses.

`steady_rate_achievement` is the achieved send rate divided by the requested rate over the
steady phase. A value well below `1.0` means the run did not apply the load it was asked to,
so its latency figures describe a lighter test than intended.

When achievement is low, `send_schedule_delay_p99_us` and `send_skipped_in_flight` say whether
the load generator itself was the limit. Both near zero means the client kept its schedule and
the shortfall came from somewhere else.

## Send Pacing

`--pacing` selects how load is applied. The default is `open`.

```text
open      send on a fixed schedule; waiting for a response does not hold up the next send
closed    start the next delay only after the response arrives (the older behaviour)
```

Under closed-loop pacing a cycle costs `delay + round trip`, so a slower server produces less
load — the offered load drops exactly when the server is under stress, and latency reads better
than it is. Open-loop pacing fixes send times against the run's start, so a late send does not
push the following ones back. Requests and responses are matched by a correlation id carried in
the first 8 bytes of the body, so responses need not arrive in order.

Open-loop applies to the TCP binary protocol only. `--transport udp` and `--protocol text-line`
stay closed-loop regardless of the flag — neither protocol has room for a correlation id. The
`pacing` key in `client_summary.csv` records what was actually used — match it on both sides
when comparing runs.

## Protocols

The server can open three listeners at once:

```text
--port <n>        TCP binary echo (always on)
--text-port <n>   line-delimited text echo (0 disables)
--udp-port <n>    UDP echo (0 disables)
```

All three share one metrics collector. GC, memory, and CPU are process-wide, so measuring them
per listener would be meaningless, and session and request counts are easier to read summed.

UDP datagrams carry a 4-byte key plus a 36-byte session GUID ahead of the payload; that prefix
is how the library identifies UDP sessions.

## Anomaly Scenarios

```text
--scenario burst      periodic bursts layered on top of the base rate
--abort-percent <n>   share of clients that end with RST instead of FIN
--payload huge        ~32KB bodies, at the protocol's Int16 size limit
--payload mixed-huge  mostly small requests with occasional huge ones
```

Burst rides on the base rate rather than replacing it, and open-loop pacing is what lets the
burst actually go out instead of stalling behind pending responses. If the in-flight limit is
too low the burst is trimmed and the shortfall lands in `send_skipped_in_flight`.

For abort runs, the values that matter are on the server side: `exception_total` should stay 0
and `active_sessions` should return to 0.

### Server Fault Injection

The runner can kill the server mid-run and bring it back:

```powershell
Test\LoadTest\run-loadtest.ps1 -RunId fault -Clients 200 -Duration 00:01:30 `
  -KillServerAt 00:00:30 -ServerDowntime 00:00:05
```

It adds `--reconnect-on-drop` to the client for you; without it the clients simply leave when the
server disappears and there is no recovery to observe. The restarted server writes to
`<run>-server-restart`, and the report joins both directories by `run_id`.

Read the outcome from three keys in `client_summary.csv`: `outage_total`, `reconnect_total`, and
`max_outage_ms`. Recovery is measured to the next *response*, not the next successful connect —
a server can be listening again while still unable to answer.

### Declarative Scenarios

`--scenario-file <path.json>` describes the request mix in a file so a load composition can change
without a rebuild. `prologue` entries are sent once per connection (and again after a reconnect);
`operations` are then mixed by `weight`. See `scenarios/game-mix.json`.

`thinkTime` applies to closed-loop pacing only. Open loop sends on a fixed schedule derived from
`--send-rate-per-client`, and the client warns rather than silently ignoring the file's value.

### Instrumentation Cost

`--metrics <full|no-gauges|off>` sets how much the server measures itself. `off` writes no server
CSV but still echoes, so the client-side numbers stay comparable:

```powershell
Test\LoadTest\measure-metrics-overhead.ps1 -Prefix ovh -Clients 300 -Duration 00:01:00 -Repeat 3
```

That runs the same load at all three levels and produces two comparisons: `off → full` for the
cost of all server instrumentation, and `no-gauges → full` for the cost of the runtime gauges
alone. When a run has no server samples the verdict compares throughput from the client side and
leaves the server-side checks inconclusive rather than passing them on absent data.

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
run summary          run-wide percentiles and rate achievement
throughput           steady phase only
throughput (all)     every phase, for comparison
phase breakdown      how long each phase lasted
latency
server handler latency
server backpressure   send-queue depth and SAEA pool headroom
memory trend
error summary
server event summary
session leak check
smoke verdict
```

Run the common reports:

```sql
SELECT * FROM analysis_run_summary;
SELECT * FROM analysis_throughput;
SELECT * FROM analysis_phase_breakdown;
SELECT * FROM analysis_server_backpressure;
SELECT * FROM analysis_memory_trend;
SELECT * FROM analysis_error_summary;
SELECT * FROM analysis_session_leak_check;
```

Start with `analysis_run_summary`. It carries the run-wide percentiles and the rate achievement,
which together tell you whether the run is worth comparing at all.

Runs recorded before the `phase` column existed report `unknown` and are treated as
load-bearing, so older results still appear in the aggregates.

Session leak checks are most useful after clients have stopped and the server has had time to drain closed sessions.

## Windows Notes

Ephemeral port exhaustion: high client counts from one Windows machine can consume local ephemeral ports or leave many sockets in `TIME_WAIT`. Increase clients gradually, reuse baselines, and consider spreading load across multiple client machines for very high connection counts.

Firewall: Windows Defender Firewall or third-party firewall tools can block listening ports or add overhead. Allow the test server port before running large tests.

Antivirus: real-time scanning can distort CSV write performance and process CPU readings, especially when writing many operation rows. Exclude the repository or `logs\loadtest` directory for controlled benchmark runs when policy allows it.

File locks: DuckDB, editors, antivirus scanners, or a still-running test process can hold CSV files open. Stop the relevant process or close the DuckDB session before deleting or overwriting a run directory.

Loopback versus NIC tests: `127.0.0.1` removes network hardware from the test and is useful for smoke and CPU-bound comparisons. Remote client machines are better for validating real network throughput and connection churn.
