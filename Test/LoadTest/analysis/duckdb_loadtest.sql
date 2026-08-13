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

CREATE OR REPLACE VIEW client_summary AS
SELECT *
FROM read_csv_auto('logs/loadtest/*/client_summary.csv', union_by_name = true);

-- The phase column marks rampup / steady / rampdown / idle. Runs recorded before the
-- column existed report 'unknown', and those rows are treated as load-bearing so that
-- older results stay comparable instead of disappearing from every aggregate.
CREATE OR REPLACE VIEW normalized_client_samples AS
SELECT
    *,
    COALESCE(json_extract_string(to_json(client_samples), '$.machine_id'), 'unknown') AS normalized_machine_id,
    COALESCE(TRY_CAST(json_extract_string(to_json(client_samples), '$.runtime_error_total') AS BIGINT), 0) AS normalized_runtime_error_total,
    COALESCE(TRY_CAST(json_extract_string(to_json(client_samples), '$.dropped_operation_rows') AS BIGINT), 0) AS normalized_dropped_operation_rows,
    COALESCE(json_extract_string(to_json(client_samples), '$.phase'), 'unknown') AS normalized_phase
FROM client_samples;

-- The runtime columns come from the SuperSocketLite meter. Runs recorded before those
-- gauges existed have no column at all, and runs with instrumentation turned off write -1,
-- so both cases normalize to -1 and are excluded from the aggregates below.
CREATE OR REPLACE VIEW normalized_server_samples AS
SELECT
    *,
    COALESCE(json_extract_string(to_json(server_samples), '$.phase'), 'unknown') AS normalized_phase,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.send_queue_depth_total') AS BIGINT), -1) AS normalized_send_queue_depth_total,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.send_queue_depth_max') AS BIGINT), -1) AS normalized_send_queue_depth_max,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.receive_saea_pool_available') AS BIGINT), -1) AS normalized_receive_saea_pool_available,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.receive_saea_pool_total') AS BIGINT), -1) AS normalized_receive_saea_pool_total,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.send_saea_pool_available') AS BIGINT), -1) AS normalized_send_saea_pool_available,
    COALESCE(TRY_CAST(json_extract_string(to_json(server_samples), '$.send_saea_pool_total') AS BIGINT), -1) AS normalized_send_saea_pool_total
FROM server_samples;

CREATE OR REPLACE VIEW normalized_client_operations AS
SELECT
    *,
    COALESCE(json_extract_string(to_json(client_operations), '$.machine_id'), 'unknown') AS normalized_machine_id
FROM client_operations;

-- Rows that carry real load. Excludes ramp-up, drain, and idle tails so that averages
-- are not diluted by the stretch where the server is running with no clients attached.
CREATE OR REPLACE VIEW steady_client_samples AS
SELECT *
FROM normalized_client_samples
WHERE normalized_phase IN ('steady', 'unknown');

CREATE OR REPLACE VIEW steady_server_samples AS
SELECT *
FROM normalized_server_samples
WHERE normalized_phase IN ('steady', 'unknown');

-- Elapsed range of the steady phase per run and machine. client_operations.csv has no
-- phase column, so per-operation rows are filtered by joining against this window.
CREATE OR REPLACE VIEW client_steady_window AS
SELECT
    run_id,
    normalized_machine_id AS machine_id,
    min(elapsed_ms) AS steady_start_ms,
    max(elapsed_ms) AS steady_end_ms
FROM normalized_client_samples
WHERE normalized_phase = 'steady'
GROUP BY run_id, normalized_machine_id;

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
    max(normalized_dropped_operation_rows) AS dropped_operation_rows,
    max(normalized_phase) AS phase
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
    max(dropped_operation_rows) AS max_dropped_operation_rows,
    max(phase) AS phase
FROM distributed_client_samples
GROUP BY run_id, elapsed_bucket_ms;

-- Throughput by run, restricted to the steady phase. Averaging every row would fold in
-- ramp-up and the idle tail after clients exit, which halves the reported rate.
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
FROM steady_server_samples s
FULL OUTER JOIN (
    SELECT *
    FROM distributed_client_samples_by_elapsed
    WHERE phase IN ('steady', 'unknown')
) c
    ON s.run_id = c.run_id
    AND floor(s.elapsed_ms / 1000) * 1000 = c.elapsed_bucket_ms
GROUP BY COALESCE(s.run_id, c.run_id)
ORDER BY run_id;

-- Same shape as analysis_throughput but over every sample, for comparison when you want
-- to see what ramp-up and drain did to the run.
CREATE OR REPLACE VIEW analysis_throughput_all_phases AS
SELECT
    COALESCE(s.run_id, c.run_id) AS run_id,
    max(s.active_sessions) AS max_active_sessions,
    avg(s.requests_per_sec) AS avg_server_rps,
    max(s.requests_per_sec) AS max_server_rps,
    avg(c.total_send_per_sec) AS avg_client_send_per_sec,
    avg(c.total_receive_per_sec) AS avg_client_receive_per_sec
FROM server_samples s
FULL OUTER JOIN distributed_client_samples_by_elapsed c
    ON s.run_id = c.run_id
    AND floor(s.elapsed_ms / 1000) * 1000 = c.elapsed_bucket_ms
GROUP BY COALESCE(s.run_id, c.run_id)
ORDER BY run_id;

-- How each run divided its time. A run whose steady_samples is 0 produced no comparable
-- measurement, which is worth seeing explicitly rather than inferring from empty averages.
CREATE OR REPLACE VIEW analysis_phase_breakdown AS
SELECT
    run_id,
    'client' AS source,
    normalized_phase AS phase,
    count(*) AS samples,
    min(elapsed_ms) AS first_elapsed_ms,
    max(elapsed_ms) AS last_elapsed_ms
FROM normalized_client_samples
GROUP BY run_id, normalized_phase
UNION ALL
SELECT
    run_id,
    'server' AS source,
    normalized_phase AS phase,
    count(*) AS samples,
    min(elapsed_ms) AS first_elapsed_ms,
    max(elapsed_ms) AS last_elapsed_ms
FROM normalized_server_samples
GROUP BY run_id, normalized_phase
ORDER BY run_id, source, phase;

-- Client RTT distribution from sampled operations, limited to the steady phase.
--
-- These percentiles are computed from client_operations.csv, which is sampled. Under
-- --operation-sampling below 1.0 they describe the sample, not the run. For the run-wide
-- figures use analysis_run_summary, whose percentiles come from the full histogram the
-- client keeps in memory and are unaffected by the sampling rate.
CREATE OR REPLACE VIEW analysis_latency AS
SELECT
    o.run_id,
    o.normalized_machine_id AS machine_id,
    o.operation_type,
    count(*) AS sample_count,
    quantile_cont(o.rtt_us, 0.50) / 1000.0 AS p50_ms,
    quantile_cont(o.rtt_us, 0.95) / 1000.0 AS p95_ms,
    quantile_cont(o.rtt_us, 0.99) / 1000.0 AS p99_ms,
    max(o.rtt_us) / 1000.0 AS max_ms
FROM normalized_client_operations o
LEFT JOIN client_steady_window w
    ON o.run_id = w.run_id
    AND o.normalized_machine_id = w.machine_id
WHERE o.success = true
  AND o.rtt_us IS NOT NULL
  AND (w.steady_start_ms IS NULL OR o.elapsed_ms BETWEEN w.steady_start_ms AND w.steady_end_ms)
GROUP BY o.run_id, o.normalized_machine_id, o.operation_type
ORDER BY run_id, machine_id, operation_type;

-- Run-wide summary pivoted from client_summary.csv. The rtt_total_* columns come from the
-- client's cumulative histogram, so they hold for the whole run at any sampling rate.
CREATE OR REPLACE VIEW analysis_run_summary AS
SELECT
    run_id,
    COALESCE(json_extract_string(to_json(client_summary), '$.machine_id'), 'unknown') AS machine_id,
    max(CASE WHEN key = 'clients' THEN TRY_CAST(value AS BIGINT) END) AS clients,
    max(CASE WHEN key = 'scenario' THEN value END) AS scenario,
    max(CASE WHEN key = 'transport' THEN value END) AS transport,
    max(CASE WHEN key = 'duration_ms' THEN TRY_CAST(value AS BIGINT) END) AS duration_ms,
    max(CASE WHEN key = 'steady_window_ms' THEN TRY_CAST(value AS BIGINT) END) AS steady_window_ms,
    max(CASE WHEN key = 'total_send_success' THEN TRY_CAST(value AS BIGINT) END) AS total_send_success,
    max(CASE WHEN key = 'total_receive' THEN TRY_CAST(value AS BIGINT) END) AS total_receive,
    max(CASE WHEN key = 'total_timeout' THEN TRY_CAST(value AS BIGINT) END) AS total_timeout,
    max(CASE WHEN key = 'total_connect_fail' THEN TRY_CAST(value AS BIGINT) END) AS total_connect_fail,
    max(CASE WHEN key = 'total_send_fail' THEN TRY_CAST(value AS BIGINT) END) AS total_send_fail,
    max(CASE WHEN key = 'socket_error_total' THEN TRY_CAST(value AS BIGINT) END) AS socket_error_total,
    max(CASE WHEN key = 'response_rate' THEN TRY_CAST(value AS DOUBLE) END) AS response_rate,
    max(CASE WHEN key = 'target_send_rate_per_sec' THEN TRY_CAST(value AS DOUBLE) END) AS target_send_rate_per_sec,
    max(CASE WHEN key = 'steady_send_rate_per_sec' THEN TRY_CAST(value AS DOUBLE) END) AS steady_send_rate_per_sec,
    max(CASE WHEN key = 'steady_rate_achievement' THEN TRY_CAST(value AS DOUBLE) END) AS steady_rate_achievement,
    max(CASE WHEN key = 'pacing' THEN value END) AS pacing,
    max(CASE WHEN key = 'max_in_flight' THEN TRY_CAST(value AS BIGINT) END) AS max_in_flight,
    -- When rate achievement is low these three say whether the load generator was the limit.
    max(CASE WHEN key = 'send_schedule_delay_p99_us' THEN TRY_CAST(value AS BIGINT) END) AS send_delay_p99_us,
    max(CASE WHEN key = 'send_skipped_in_flight' THEN TRY_CAST(value AS BIGINT) END) AS send_skipped_in_flight,
    max(CASE WHEN key = 'max_in_flight_observed' THEN TRY_CAST(value AS BIGINT) END) AS max_in_flight_observed,
    -- 서버 장애 주입에서 읽는 값. max_outage_ms 는 연결이 끊긴 뒤 응답을 다시 받기까지의 최대 시간이다.
    max(CASE WHEN key = 'outage_total' THEN TRY_CAST(value AS BIGINT) END) AS outage_total,
    max(CASE WHEN key = 'reconnect_total' THEN TRY_CAST(value AS BIGINT) END) AS reconnect_total,
    max(CASE WHEN key = 'max_outage_ms' THEN TRY_CAST(value AS BIGINT) END) AS max_outage_ms,
    max(CASE WHEN key = 'operation_sampling' THEN TRY_CAST(value AS DOUBLE) END) AS operation_sampling,
    max(CASE WHEN key = 'dropped_client_operation_rows' THEN TRY_CAST(value AS BIGINT) END) AS dropped_operation_rows,
    max(CASE WHEN key = 'rtt_total_count' THEN TRY_CAST(value AS BIGINT) END) AS rtt_total_count,
    max(CASE WHEN key = 'rtt_total_p50_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_p50_ms,
    max(CASE WHEN key = 'rtt_total_p90_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_p90_ms,
    max(CASE WHEN key = 'rtt_total_p95_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_p95_ms,
    max(CASE WHEN key = 'rtt_total_p99_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_p99_ms,
    max(CASE WHEN key = 'rtt_total_p999_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_p999_ms,
    max(CASE WHEN key = 'rtt_total_max_us' THEN TRY_CAST(value AS BIGINT) END) / 1000.0 AS rtt_max_ms
FROM client_summary
GROUP BY run_id, COALESCE(json_extract_string(to_json(client_summary), '$.machine_id'), 'unknown')
ORDER BY run_id, machine_id;

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
    avg(CASE WHEN normalized_phase IN ('steady', 'unknown') THEN send_per_sec END) AS avg_steady_send_per_sec,
    avg(CASE WHEN normalized_phase IN ('steady', 'unknown') THEN receive_per_sec END) AS avg_steady_receive_per_sec,
    max(CASE WHEN normalized_phase IN ('steady', 'unknown') THEN rtt_p99_us END) / 1000.0 AS max_steady_rtt_p99_ms,
    count(*) FILTER (WHERE normalized_phase = 'steady') AS steady_samples
FROM normalized_client_samples
GROUP BY run_id, normalized_machine_id
ORDER BY run_id, machine_id;

-- Distributed client throughput by timestamp. For multi-machine runs, copy each
-- client machine's CSV directory under logs/loadtest before running this query.
CREATE OR REPLACE VIEW analysis_distributed_client_throughput AS
SELECT *
FROM distributed_client_samples_by_elapsed
ORDER BY run_id, elapsed_bucket_ms;

-- Server handler latency distribution from periodic sample histograms, steady phase only.
CREATE OR REPLACE VIEW analysis_server_handler_latency AS
SELECT
    run_id,
    max(handler_latency_p50_us) / 1000.0 AS max_handler_p50_ms,
    max(handler_latency_p95_us) / 1000.0 AS max_handler_p95_ms,
    max(handler_latency_p99_us) / 1000.0 AS max_handler_p99_ms,
    max(handler_latency_max_us) / 1000.0 AS max_handler_max_ms,
    count(*) AS steady_samples
FROM steady_server_samples
GROUP BY run_id
ORDER BY run_id;

-- Send backpressure and pool headroom, steady phase only. A send queue that stays deep means
-- the server accepts sends faster than the socket drains them; a pool whose available count
-- reaches zero means new connections are about to be refused. instrumented_samples = 0 says the
-- run carries no runtime gauges, which is not the same as "the queues were empty".
CREATE OR REPLACE VIEW analysis_server_backpressure AS
SELECT
    run_id,
    max(normalized_send_queue_depth_total) AS max_send_queue_depth_total,
    max(normalized_send_queue_depth_max) AS max_send_queue_depth_session,
    avg(CASE WHEN normalized_send_queue_depth_total >= 0 THEN normalized_send_queue_depth_total END) AS avg_send_queue_depth_total,
    min(CASE WHEN normalized_receive_saea_pool_available >= 0 THEN normalized_receive_saea_pool_available END) AS min_receive_pool_available,
    max(normalized_receive_saea_pool_total) AS max_receive_pool_total,
    min(CASE WHEN normalized_send_saea_pool_available >= 0 THEN normalized_send_saea_pool_available END) AS min_send_pool_available,
    max(normalized_send_saea_pool_total) AS max_send_pool_total,
    count(*) FILTER (WHERE normalized_send_queue_depth_total >= 0) AS instrumented_samples
FROM steady_server_samples
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
        max(rtt_p99_ms) AS max_p99_ms,
        min(steady_rate_achievement) AS min_rate_achievement
    FROM analysis_run_summary
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
    latency.min_rate_achievement,
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
        AND COALESCE(latency.max_p99_ms, 0) < 50.0
        -- Runs recorded before the summary carried this figure report NULL and are not
        -- failed on it; a run that did record it must have driven at least 95% of the
        -- requested load, otherwise the latency numbers describe a lighter test than asked for.
        AND COALESCE(latency.min_rate_achievement, 1.0) >= 0.95 AS passed
FROM errors
FULL OUTER JOIN latency ON errors.run_id = latency.run_id
FULL OUTER JOIN leaks ON COALESCE(errors.run_id, latency.run_id) = leaks.run_id
ORDER BY run_id;

-- Example ad-hoc queries:
--   SELECT * FROM analysis_run_summary;        -- run-wide percentiles and rate achievement
--   SELECT * FROM analysis_throughput;         -- steady phase only
--   SELECT * FROM analysis_throughput_all_phases;
--   SELECT * FROM analysis_phase_breakdown;    -- how long each phase lasted
--   SELECT * FROM analysis_latency;
--   SELECT * FROM analysis_memory_trend;
--   SELECT * FROM analysis_error_summary;
--   SELECT * FROM analysis_session_leak_check;
--   SELECT * FROM analysis_smoke_verdict;
