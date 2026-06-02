# LoadTest 빌드 및 사용 방법

이 문서는 `Test\LoadTest` 부하 테스트 도구의 빌드, 실행, 결과 확인 방법을 정리합니다.
명령어는 저장소 루트(`F:\github\SuperSocketLite2`)에서 실행하는 것을 기준으로 합니다.

## 구성

```text
Test\LoadTest\
├── SuperSocketLite.LoadTest.sln
├── SuperSocketLite.LoadTest.Client\       부하 테스트 클라이언트
├── SuperSocketLite.LoadTest.Server\       계측용 TCP 바이너리 에코 서버
├── SuperSocketLite.LoadTest.ServerProbe\  서버 메트릭/CSV 계측 모듈
├── SuperSocketLite.LoadTest.Shared\       공용 패킷, CSV, 메트릭 유틸리티
├── SuperSocketLite.LoadTest.Tests\        테스트 실행 프로젝트
└── analysis\                              DuckDB 분석 SQL
```

## 사전 준비

- .NET 10 SDK
- PowerShell
- DuckDB CLI(선택): CSV 분석을 실행할 때만 필요합니다.

DuckDB CLI가 없으면 다음 명령으로 설치할 수 있습니다.

```powershell
winget install --id DuckDB.cli --exact
```

## 빌드

저장소 루트에서 LoadTest 솔루션을 빌드합니다.

```powershell
dotnet build Test\LoadTest\SuperSocketLite.LoadTest.sln -c Release
```

테스트까지 확인하려면 다음 명령을 실행합니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Tests\SuperSocketLite.LoadTest.Tests.csproj -c Release
```

## 빠른 실행 순서

1. 서버를 실행합니다.
2. 다른 PowerShell 창에서 클라이언트를 실행합니다.
3. 실행이 끝나면 `logs\loadtest` 아래 CSV 파일을 확인합니다.
4. 필요하면 DuckDB 분석 SQL을 실행합니다.

## 서버 실행

기본적인 100 클라이언트 스모크 테스트용 서버 실행 예입니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Server -- `
  --port 2012 `
  --max-connections 1000 `
  --output logs\loadtest\smoke-server `
  --duration 00:06:00
```

`--duration`을 지정하면 지정 시간이 지난 뒤 서버가 정상 종료하면서 마지막 세션 종료 이벤트까지 CSV에 기록합니다.
장시간 테스트에서 서버를 계속 켜 두려면 `--duration`을 생략합니다.

### 서버 옵션

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--port` | `2012` | 서버 리슨 포트입니다. |
| `--max-connections` | `1000` | 허용할 최대 연결 수입니다. |
| `--output` | `logs\loadtest\local-server` | 서버 CSV 출력 디렉토리입니다. |
| `--sample-interval-ms` | `1000` | 서버 샘플 메트릭 기록 주기입니다. |
| `--server-metrics-interval-ms` | `1000` | `--sample-interval-ms`와 같은 옵션입니다. |
| `--server-event-request-sampling` | `0` | 요청 이벤트 CSV 샘플링 비율입니다. `1.0`은 전체 기록, `0.001`은 0.1% 기록입니다. |
| `--duration` | 없음 | 서버 자동 종료까지의 실행 시간입니다. 예: `00:06:00`. |
| `--run-id` | UTC 타임스탬프 | CSV에 기록할 실행 식별자입니다. |

## 클라이언트 실행

TCP 바이너리 에코 스모크 테스트 예입니다.

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

### 클라이언트 공통 옵션

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--transport` | `tcp` | 전송 방식입니다. `tcp`, `text`, `udp`를 사용할 수 있습니다. |
| `--protocol` | `echo-binary` | 프로토콜 이름입니다. TCP 바이너리 서버 테스트는 `echo-binary`를 사용합니다. |
| `--host` | `127.0.0.1` | 접속 대상 호스트입니다. |
| `--port` | `2012` | 접속 대상 포트입니다. |
| `--clients` | `1` | 생성할 동시 클라이언트 수입니다. |
| `--ramp-up` | `00:00:00` | 클라이언트 수를 점진적으로 늘리는 시간입니다. |
| `--duration` | `00:01:00` | 클라이언트 실행 시간입니다. |
| `--send-rate-per-client` | `1.0` | 클라이언트 1개당 초당 송신 횟수입니다. |
| `--payload` | `small` | 페이로드 크기 패턴입니다. 예: `small`, `mixed`. |
| `--output` | `logs\loadtest\client` | 클라이언트 CSV 출력 디렉토리입니다. |
| `--scenario` | `echo` | 실행 시나리오입니다. 예: `echo`, `game-like`, `idle-heartbeat`, `reconnect-storm`. |
| `--receive-timeout` | `00:00:05` | 응답 수신 타임아웃입니다. |
| `--operation-sampling` | `1.0` | `client_operations.csv` 샘플링 비율입니다. |
| `--client-operation-sampling` | `1.0` | `--operation-sampling`과 같은 옵션입니다. |
| `--slow-receiver-delay-ms` | `0` | 응답 읽기를 지연시켜 서버 송신 큐 압력을 만드는 옵션입니다. |
| `--partial-packet` | 꺼짐 | TCP 바이너리 패킷을 부분 전송합니다. |
| `--coalesced-packet` | 꺼짐 | 여러 TCP 바이너리 패킷을 합쳐 전송합니다. |
| `--udp-loss-percent` | `0` | UDP 테스트에서 클라이언트 송신 손실을 흉내 내는 비율입니다. |
| `--run-id` | UTC 타임스탬프 | CSV에 기록할 실행 식별자입니다. |
| `--machine-id` | OS 머신명 | 클라이언트 CSV에 기록할 부하 발생 머신 식별자입니다. 복수 머신 실행 시 명시하는 것을 권장합니다. |

### 게임형 시나리오

모바일 게임 서버에 가까운 주기적 하트비트, 채팅, 룸 이동 패턴을 흉내 냅니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 1000 `
  --ramp-up 00:02:00 `
  --duration 00:30:00 `
  --scenario game-like `
  --payload mixed `
  --heartbeat-min-sec 5 `
  --heartbeat-max-sec 15 `
  --chat-min-sec 10 `
  --chat-max-sec 45 `
  --room-cycle-every 120 `
  --output logs\loadtest\game-like-1000
```

게임형 시나리오 옵션입니다.

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--heartbeat-min-sec` | `5` | 하트비트 최소 간격(초)입니다. |
| `--heartbeat-max-sec` | `15` | 하트비트 최대 간격(초)입니다. |
| `--chat-min-sec` | `10` | 채팅 패킷 최소 간격(초)입니다. |
| `--chat-max-sec` | `45` | 채팅 패킷 최대 간격(초)입니다. |
| `--room-cycle-every` | `120` | 룸 이동 패킷 주기(초)입니다. |

### 재접속 폭주 시나리오

일부 클라이언트가 동시에 끊긴 뒤 짧은 시간 안에 재접속하는 상황을 흉내 냅니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 1000 `
  --ramp-up 00:02:00 `
  --duration 00:20:00 `
  --scenario reconnect-storm `
  --storm-at 00:10:00 `
  --storm-percent 30 `
  --storm-window 00:00:30 `
  --reconnect-percent 2 `
  --output logs\loadtest\reconnect-storm-1000
```

재접속 시나리오 옵션입니다.

| 옵션 | 기본값 | 설명 |
| --- | --- | --- |
| `--reconnect-percent` | `2` | 일반 실행 중 재접속을 시도할 클라이언트 비율입니다. |
| `--storm-at` | `00:00:00` | 재접속 폭주를 시작할 시점입니다. |
| `--storm-percent` | `0` | 폭주 대상 클라이언트 비율입니다. |
| `--storm-window` | `00:00:20` | 폭주 재접속을 분산할 시간 창입니다. |

## 복수 머신 분산 클라이언트 실행

여러 머신에서 더미 클라이언트를 동시에 실행할 수 있습니다.
서버는 한 대에서 실행하고, 각 클라이언트 머신은 서버 머신의 LAN IP로 접속합니다.

분산 실행에서는 다음 값을 미리 정합니다.

```text
server_ip: 서버 머신의 LAN IP
run_id: 모든 서버/클라이언트가 공유할 실행 식별자
machine_id: 각 클라이언트 머신을 구분할 이름
```

서버 머신에서 실행합니다.

```powershell
$runId = "dist-20260602-001"

dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Server -- `
  --port 2012 `
  --max-connections 3500 `
  --run-id $runId `
  --output logs\loadtest\$runId-server `
  --duration 00:35:00
```

클라이언트 머신 A에서 실행합니다.

```powershell
$runId = "dist-20260602-001"

dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp `
  --protocol echo-binary `
  --host 192.168.0.10 `
  --port 2012 `
  --clients 1000 `
  --ramp-up 00:02:00 `
  --duration 00:30:00 `
  --send-rate-per-client 1.0 `
  --payload mixed `
  --run-id $runId `
  --machine-id client-a `
  --output logs\loadtest\$runId-client-a
```

클라이언트 머신 B와 C도 같은 `--run-id`를 사용하고 `--machine-id`, `--output`만 다르게 지정합니다.

```powershell
--machine-id client-b --output logs\loadtest\$runId-client-b
--machine-id client-c --output logs\loadtest\$runId-client-c
```

중요한 기준입니다.

- 서버의 `--max-connections`는 모든 클라이언트 머신의 `--clients` 합보다 크게 설정합니다.
- 각 클라이언트 머신은 서로 다른 `--machine-id`를 사용합니다.
- 모든 프로세스가 같은 `--run-id`를 사용해야 DuckDB에서 하나의 실행으로 묶입니다.
- 각 머신의 `--output` 디렉토리는 서로 달라야 합니다. 같은 디렉토리에 CSV를 동시에 쓰면 안 됩니다.
- 서버 머신 방화벽에서 테스트 포트, 기본 `2012`, 인바운드를 허용해야 합니다.

분석할 때는 각 머신의 출력 디렉토리를 한 머신의 `logs\loadtest` 아래로 복사합니다.
CSV 파일 이름을 합치지 말고 디렉토리 단위로 보관합니다.

권장 수집 구조입니다.

```text
logs\loadtest\
├── dist-20260602-001-server\
│   ├── server_samples.csv
│   └── server_events.csv
├── dist-20260602-001-client-a\
│   ├── client_samples.csv
│   ├── client_operations.csv
│   └── client_summary.csv
├── dist-20260602-001-client-b\
│   ├── client_samples.csv
│   ├── client_operations.csv
│   └── client_summary.csv
└── dist-20260602-001-client-c\
    ├── client_samples.csv
    ├── client_operations.csv
    └── client_summary.csv
```

각 클라이언트 CSV에는 `machine_id` 컬럼이 기록됩니다.
그래서 여러 머신에서 `client_id` 또는 `operation_id`가 같은 값으로 시작하더라도 `run_id + machine_id + client_id` 조합으로 구분할 수 있습니다.

## 다른 전송 방식

`SuperSocketLite.LoadTest.Server`는 TCP 바이너리 에코 프로토콜 검증용 서버입니다.
`text` 또는 `udp` 전송 방식은 해당 프로토콜을 처리할 수 있는 별도 서버나 호환 엔드포인트가 필요합니다.

텍스트 라인 기반 서버가 준비되어 있으면 다음처럼 실행합니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport text `
  --protocol text-line `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 100 `
  --duration 00:05:00 `
  --output logs\loadtest\text-line-client
```

UDP 에코 엔드포인트가 준비되어 있으면 다음처럼 실행합니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport udp `
  --protocol udp-echo `
  --host 127.0.0.1 `
  --port 2012 `
  --clients 100 `
  --duration 00:05:00 `
  --udp-loss-percent 0 `
  --output logs\loadtest\udp-client
```

## CSV 출력

실행 결과는 지정한 `--output` 디렉토리에 CSV로 기록됩니다.

| 파일 | 생성 주체 | 설명 |
| --- | --- | --- |
| `server_samples.csv` | 서버 | 활성 세션, 요청 수, 처리량, GC, 메모리, CPU, 핸들러 지연 시간 등 주기 샘플입니다. |
| `server_events.csv` | 서버 | 접속, 종료, 오류, 샘플링된 요청 이벤트입니다. |
| `client_samples.csv` | 클라이언트 | 활성 클라이언트, 접속/종료, 송수신, 타임아웃, RTT 분위수 등 주기 샘플입니다. |
| `client_operations.csv` | 클라이언트 | 샘플링된 개별 요청/응답 지연 시간입니다. |
| `client_summary.csv` | 클라이언트 | 실행 종료 시점의 요약 메트릭입니다. |

클라이언트 CSV에는 `machine_id` 컬럼이 포함됩니다.
복수 머신 실행 결과를 분석할 때 이 값으로 부하 발생 머신별 결과를 구분합니다.

고부하 테스트에서는 `client_operations.csv`가 병목이 될 수 있습니다.
이 경우 `--operation-sampling 0.01`처럼 샘플링 비율을 낮춥니다.
집계 샘플만 남기고 개별 작업 기록을 끄려면 `--operation-sampling 0.0`을 사용합니다.

## DuckDB 분석

CSV 파일을 `logs\loadtest` 아래에 둔 상태에서 다음 명령을 실행합니다.

```powershell
duckdb loadtest.duckdb -init Test\LoadTest\analysis\duckdb_loadtest.sql
```

DuckDB 콘솔에서 자주 쓰는 분석 뷰를 조회합니다.

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

복수 머신 실행 결과를 볼 때는 다음 순서로 확인합니다.

```sql
-- 클라이언트 머신별 최대 클라이언트 수, 오류, 평균 송수신량
SELECT *
FROM analysis_client_machine_summary
WHERE run_id = 'dist-20260602-001';

-- 모든 클라이언트 머신의 송수신량을 timestamp 단위로 합산
SELECT *
FROM analysis_distributed_client_throughput
WHERE run_id = 'dist-20260602-001'
ORDER BY elapsed_bucket_ms;

-- 서버 처리량과 분산 클라이언트 전체 처리량 비교
SELECT *
FROM analysis_throughput
WHERE run_id = 'dist-20260602-001';

-- 머신별/operation_type별 RTT 분포
SELECT *
FROM analysis_latency
WHERE run_id = 'dist-20260602-001'
ORDER BY machine_id, operation_type;

-- 스모크 테스트 자동 판정
SELECT *
FROM analysis_smoke_verdict
WHERE run_id = 'dist-20260602-001';
```

세션 누수 확인은 클라이언트가 종료된 뒤 서버가 닫힌 세션을 충분히 정리한 상태에서 보는 것이 좋습니다.

## 권장 실행 기준

스모크 테스트는 기능 검증용입니다.

```text
clients: 100
duration: 5 minutes
expected: 서버 예외 0건, 클라이언트 오류율 0%, 로컬 p99 RTT 50 ms 미만
```

기준 성능 측정은 변경 전후 비교용입니다.

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

장시간 안정성 측정은 메모리 증가, GC 압력, 세션 정리 문제를 확인하는 데 사용합니다.

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

## 문제 해결

- 포트 충돌: `--port` 값을 바꾸거나 기존 서버 프로세스를 종료합니다.
- 방화벽 차단: Windows Defender Firewall 또는 보안 제품에서 테스트 포트를 허용합니다.
- 높은 연결 수 실패: 단일 Windows 클라이언트 머신에서는 임시 포트와 `TIME_WAIT` 때문에 접속 수가 제한될 수 있습니다. 클라이언트 수를 단계적으로 올리거나 여러 머신에 부하를 분산합니다.
- CSV 쓰기 병목: `--operation-sampling` 값을 낮추고, 필요하면 백신의 실시간 검사 대상에서 `logs\loadtest`를 제외합니다.
- DuckDB에서 CSV를 못 읽음: 테스트 프로세스가 아직 파일을 잡고 있거나 CSV 경로가 `logs\loadtest` 밖에 있을 수 있습니다.
- 결과 재현성: 매번 고유한 `--output` 디렉토리와 `--run-id`를 사용하면 결과 비교가 쉬워집니다.
