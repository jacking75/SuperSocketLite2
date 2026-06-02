-- DuckDB analysis bootstrap for SuperSocketLite load test CSV output.
-- Run from the repository root:
--   duckdb loadtest.duckdb -init Test\LoadTest\analysis\duckdb_loadtest.sql
--
-- Expected CSV layout:
--   logs/loadtest/<run-id>/server_samples.csv
--   logs/loadtest/<run-id>/server_events.csv
--   logs/loadtest/<run-id>/client_samples.csv
--   logs/loadtest/<run-id>/client_operations.csv

CREATE OR REPLACE VIEW server_samples AS
SELECT *
FROM read_csv_auto('logs/loadtest/*/server_samples.csv', union_by_name = true);

CREATE OR REPLACE VIEW server_events AS
SELECT *
FROM read_csv_auto('logs/loadtest/*/server_events.csv', union_by_name = true);

CREATE OR REPLACE VIEW client_samples AS
SELECT *
FROM read_csv_auto('logs/loadtest/*/client_samples.csv', union_by_name = true);

CREATE OR REPLACE VIEW client_operations AS
SELECT *
FROM read_csv_auto('logs/loadtest/*/client_operations.csv', union_by_name = true);

CREATE OR REPLACE VIEW normalized_client_samples AS
SELECT
    *,
    COALESCE(json_extract_string(to_json(client_samples), '$.machine_id'), 'unknown') AS normalized_machine_id,
    COALESCE(TRY_CAST(json_extract_string(to_json(client_samples), '$.runtime_error_total') AS BIGINT), 0) AS normalized_runtime_error_total,
    COALESCE(TRY_CAST(json_extract_string(to_json(client_samples), '$.dropped_operation_rows') AS BIGINT), 0) AS normalized_dropped_operation_rows
FROM client_samples;

CREATE OR REPLACE VIEW normalized_client_operations AS
SELECT
    *,
    COALESCE(json_extract_string(to_json(client_operations), '$.machine_id'), 'unknown') AS normalized_machine_id
FROM client_operations;

-- One row per run, load-generator machine, and sample timestamp.
-- This prevents server samples from being duplicated when multiple client
-- machines share one run_id.
CREATE OR REPLACE VIEW distributed_client_samples AS
SELECT
    run_id,
    normalized_machine_id AS machine_id,
    floor(elapsed_ms / 1000) * 1000 AS elapsed_bucket_ms,
    elapsed_ms,
    max(active_clients) AS active_clients,
    max(connecting_clients) AS connecting_clients,
    max(connected_clients) AS connected_clients,
    max(send_per_sec) AS send_per_sec,
    max(receive_per_sec) AS receive_per_sec,
    max(bytes_sent_per_sec) AS bytes_sent_per_sec,
    max(bytes_received_per_sec) AS bytes_received_per_sec,
    max(rtt_p99_us) AS rtt_p99_us,
    max(normalized_runtime_error_total) AS runtime_error_total,
    max(normalized_dropped_operation_rows) AS dropped_operation_rows
FROM normalized_client_samples
GROUP BY run_id, normalized_machine_id, floor(elapsed_ms / 1000) * 1000, elapsed_ms;

CREATE OR REPLACE VIEW distributed_client_samples_by_elapsed AS
SELECT
    run_id,
    elapsed_bucket_ms,
    count(*) AS client_machine_count,
    sum(active_clients) AS total_active_clients,
    sum(connecting_clients) AS total_connecting_clients,
    sum(connected_clients) AS total_connected_clients,
    sum(send_per_sec) AS total_send_per_sec,
    sum(receive_per_sec) AS total_receive_per_sec,
    sum(bytes_sent_per_sec) AS total_bytes_sent_per_sec,
    sum(bytes_received_per_sec) AS total_bytes_received_per_sec,
    max(rtt_p99_us) / 1000.0 AS worst_machine_rtt_p99_ms,
    max(runtime_error_total) AS max_runtime_error_total,
    max(dropped_operation_rows) AS max_dropped_operation_rows
FROM distributed_client_samples
GROUP BY run_id, elapsed_bucket_ms;

-- Throughput by run. Compares server-side request rate with client-side receive rate.
CREATE OR REPLACE VIEW analysis_throughput AS
SELECT
    COALESCE(s.run_id, c.run_id) AS run_id,
    max(s.active_sessions) AS max_active_sessions,
    avg(s.requests_per_sec) AS avg_server_rps,
    max(s.requests_per_sec) AS max_server_rps,
    avg(c.total_send_per_sec) AS avg_client_send_per_sec,
    avg(c.total_receive_per_sec) AS avg_client_receive_per_sec,
    avg(s.bytes_in_per_sec + s.bytes_out_per_sec) AS avg_server_network_bytes_per_sec,
    avg(c.total_bytes_sent_per_sec + c.total_bytes_received_per_sec) AS avg_client_network_bytes_per_sec
FROM server_samples s
FULL OUTER JOIN distributed_client_samples_by_elapsed c
    ON s.run_id = c.run_id
    AND floor(s.elapsed_ms / 1000) * 1000 = c.elapsed_bucket_ms
GROUP BY COALESCE(s.run_id, c.run_id)
ORDER BY run_id;

-- Client RTT latency distribution from sampled operations.
CREATE OR REPLACE VIEW analysis_latency AS
SELECT
    run_id,
    normalized_machine_id AS machine_id,
    operation_type,
    count(*) AS sample_count,
    quantile_cont(rtt_us, 0.50) / 1000.0 AS p50_ms,
    quantile_cont(rtt_us, 0.95) / 1000.0 AS p95_ms,
    quantile_cont(rtt_us, 0.99) / 1000.0 AS p99_ms,
    max(rtt_us) / 1000.0 AS max_ms
FROM normalized_client_operations
WHERE success = true
  AND rtt_us IS NOT NULL
GROUP BY run_id, normalized_machine_id, operation_type
ORDER BY run_id, machine_id, operation_type;

-- Client summary by load-generator machine. This keeps distributed client runs
-- distinguishable when multiple machines share one run_id.
CREATE OR REPLACE VIEW analysis_client_machine_summary AS
SELECT
    run_id,
    normalized_machine_id AS machine_id,
    max(active_clients) AS max_active_clients,
    max(total_connect_success) AS total_connect_success,
    max(total_connect_fail) AS total_connect_fail,
    max(total_disconnect) AS total_disconnect,
    max(total_send_success) AS total_send_success,
    max(total_send_fail) AS total_send_fail,
    max(total_receive) AS total_receive,
    max(total_timeout) AS total_timeout,
    max(socket_error_total) AS socket_error_total,
    max(protocol_error_total) AS protocol_error_total,
    max(normalized_runtime_error_total) AS runtime_error_total,
    max(normalized_dropped_operation_rows) AS dropped_operation_rows,
    avg(send_per_sec) AS avg_send_per_sec,
    avg(receive_per_sec) AS avg_receive_per_sec,
    max(rtt_p99_us) / 1000.0 AS max_rtt_p99_ms
FROM normalized_client_samples
GROUP BY run_id, normalized_machine_id
ORDER BY run_id, machine_id;

-- Distributed client throughput by timestamp. For multi-machine runs, copy each
-- client machine's CSV directory under logs/loadtest before running this query.
CREATE OR REPLACE VIEW analysis_distributed_client_throughput AS
SELECT *
FROM distributed_client_samples_by_elapsed
ORDER BY run_id, elapsed_bucket_ms;

-- Server handler latency distribution from periodic sample histograms.
CREATE OR REPLACE VIEW analysis_server_handler_latency AS
SELECT
    run_id,
    max(handler_latency_p50_us) / 1000.0 AS max_handler_p50_ms,
    max(handler_latency_p95_us) / 1000.0 AS max_handler_p95_ms,
    max(handler_latency_p99_us) / 1000.0 AS max_handler_p99_ms,
    max(handler_latency_max_us) / 1000.0 AS max_handler_max_ms
FROM server_samples
GROUP BY run_id
ORDER BY run_id;

-- Memory trend by run. Positive growth may be expected during ramp-up; compare final rows
-- after client shutdown when checking for leaks.
CREATE OR REPLACE VIEW analysis_memory_trend AS
SELECT
    run_id,
    min(working_set_bytes) / 1024.0 / 1024.0 AS min_working_mb,
    max(working_set_bytes) / 1024.0 / 1024.0 AS max_working_mb,
    (max(working_set_bytes) - min(working_set_bytes)) / 1024.0 / 1024.0 AS working_growth_mb,
    arg_min(working_set_bytes, elapsed_ms) / 1024.0 / 1024.0 AS first_working_mb,
    arg_max(working_set_bytes, elapsed_ms) / 1024.0 / 1024.0 AS final_working_mb,
    min(gc_heap_bytes) / 1024.0 / 1024.0 AS min_heap_mb,
    max(gc_heap_bytes) / 1024.0 / 1024.0 AS max_heap_mb,
    arg_min(gc_heap_bytes, elapsed_ms) / 1024.0 / 1024.0 AS first_heap_mb,
    arg_max(gc_heap_bytes, elapsed_ms) / 1024.0 / 1024.0 AS final_heap_mb,
    max(gc_gen2_total) AS gc_gen2_total
FROM server_samples
GROUP BY run_id
ORDER BY run_id;

-- Client and server error summary.
CREATE OR REPLACE VIEW analysis_error_summary AS
SELECT
    COALESCE(c.run_id, s.run_id) AS run_id,
    max(c.total_connect_fail) AS client_connect_fail,
    max(c.total_send_fail) AS client_send_fail,
    max(c.total_timeout) AS client_timeout,
    max(c.socket_error_total) AS client_socket_errors,
    max(c.protocol_error_total) AS client_protocol_errors,
    max(c.normalized_runtime_error_total) AS client_runtime_errors,
    max(c.normalized_dropped_operation_rows) AS client_dropped_operation_rows,
    max(s.send_fail_total) AS server_send_fail,
    max(s.exception_total) AS server_exceptions,
    max(s.protocol_error_total) AS server_protocol_errors
FROM normalized_client_samples c
FULL OUTER JOIN server_samples s
    ON c.run_id = s.run_id
GROUP BY COALESCE(c.run_id, s.run_id)
ORDER BY run_id;

-- Server events grouped by run and type. Useful when server_events.csv exists.
CREATE OR REPLACE VIEW analysis_server_event_summary AS
SELECT
    run_id,
    event_type,
    count(*) AS event_count,
    count(DISTINCT session_id) AS distinct_sessions,
    min(timestamp_utc) AS first_seen_utc,
    max(timestamp_utc) AS last_seen_utc
FROM server_events
GROUP BY run_id, event_type
ORDER BY run_id, event_type;

-- Session leak check. After clients finish and the server drains, final_active_sessions
-- should return close to zero and total_connected should match total_closed.
CREATE OR REPLACE VIEW analysis_session_leak_check AS
SELECT
    run_id,
    arg_max(active_sessions, elapsed_ms) AS final_active_sessions,
    max(active_sessions) AS max_active_sessions,
    max(total_connected) AS total_connected,
    max(total_closed) AS total_closed,
    max(total_connected) - max(total_closed) AS connected_minus_closed
FROM server_samples
GROUP BY run_id
ORDER BY run_id;

-- Automated smoke-test verdict. A local smoke run should have no client/server
-- errors, no dropped operation rows, no leaked sessions, and p99 RTT under 50 ms.
CREATE OR REPLACE VIEW analysis_smoke_verdict AS
WITH latency AS (
    SELECT
        run_id,
        max(p99_ms) AS max_p99_ms
    FROM analysis_latency
    GROUP BY run_id
),
errors AS (
    SELECT *
    FROM analysis_error_summary
),
leaks AS (
    SELECT *
    FROM analysis_session_leak_check
)
SELECT
    COALESCE(errors.run_id, latency.run_id, leaks.run_id) AS run_id,
    COALESCE(errors.client_connect_fail, 0) AS client_connect_fail,
    COALESCE(errors.client_timeout, 0) AS client_timeout,
    COALESCE(errors.client_socket_errors, 0) AS client_socket_errors,
    COALESCE(errors.client_protocol_errors, 0) AS client_protocol_errors,
    COALESCE(errors.client_runtime_errors, 0) AS client_runtime_errors,
    COALESCE(errors.client_dropped_operation_rows, 0) AS client_dropped_operation_rows,
    COALESCE(errors.server_exceptions, 0) AS server_exceptions,
    COALESCE(errors.server_protocol_errors, 0) AS server_protocol_errors,
    COALESCE(leaks.final_active_sessions, 0) AS final_active_sessions,
    COALESCE(leaks.connected_minus_closed, 0) AS connected_minus_closed,
    latency.max_p99_ms,
    COALESCE(errors.client_connect_fail, 0) = 0
        AND COALESCE(errors.client_timeout, 0) = 0
        AND COALESCE(errors.client_socket_errors, 0) = 0
        AND COALESCE(errors.client_protocol_errors, 0) = 0
        AND COALESCE(errors.client_runtime_errors, 0) = 0
        AND COALESCE(errors.client_dropped_operation_rows, 0) = 0
        AND COALESCE(errors.server_exceptions, 0) = 0
        AND COALESCE(errors.server_protocol_errors, 0) = 0
        AND COALESCE(leaks.final_active_sessions, 0) = 0
        AND COALESCE(leaks.connected_minus_closed, 0) = 0
        AND COALESCE(latency.max_p99_ms, 0) < 50.0 AS passed
FROM errors
FULL OUTER JOIN latency ON errors.run_id = latency.run_id
FULL OUTER JOIN leaks ON COALESCE(errors.run_id, latency.run_id) = leaks.run_id
ORDER BY run_id;

-- Example ad-hoc queries:
--   SELECT * FROM analysis_throughput;
--   SELECT * FROM analysis_latency;
--   SELECT * FROM analysis_memory_trend;
--   SELECT * FROM analysis_error_summary;
--   SELECT * FROM analysis_session_leak_check;
--   SELECT * FROM analysis_smoke_verdict;
