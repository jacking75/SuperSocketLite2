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
| `--port` | `2012` | TCP 바이너리 리슨 포트입니다. |
| `--text-port` | `0` | text-line 리슨 포트입니다. `0`이면 열지 않습니다. |
| `--udp-port` | `0` | UDP 에코 리슨 포트입니다. `0`이면 열지 않습니다. |
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
| `--payload` | `small` | 페이로드 크기 패턴입니다. `small`(32B), `medium`(256B), `large`(4KB), `huge`(약 32KB), `mixed`, `mixed-huge`를 씁니다. |
| `--abort-percent` | `0` | 실행 종료 시 정상 종료 대신 RST로 끊을 클라이언트 비율입니다. |
| `--burst-every` | `00:00:10` | 순간 폭주 간격입니다. `--scenario burst`에서만 씁니다. |
| `--burst-size` | `20` | 폭주 한 번에 몰아서 보낼 요청 수입니다. |
| `--output` | `logs\loadtest\client` | 클라이언트 CSV 출력 디렉토리입니다. |
| `--scenario` | `echo` | 실행 시나리오입니다. 예: `echo`, `game-like`, `idle-heartbeat`, `reconnect-storm`. |
| `--pacing` | `open` | 송신 페이싱 방식입니다. `open` 또는 `closed`를 지정합니다. 아래 설명을 참고합니다. |
| `--max-in-flight` | 자동 | 응답을 기다리는 동안 동시에 떠 있을 수 있는 요청 수입니다. 지정하지 않으면 `송신 레이트 × 수신 타임아웃`으로 잡습니다. |
| `--receive-timeout` | `00:00:05` | 응답 수신 타임아웃입니다. |
| `--operation-sampling` | `1.0` | `client_operations.csv` 샘플링 비율입니다. |
| `--client-operation-sampling` | `1.0` | `--operation-sampling`과 같은 옵션입니다. |
| `--slow-receiver-delay-ms` | `0` | 응답 읽기를 지연시켜 서버 송신 큐 압력을 만드는 옵션입니다. |
| `--partial-packet` | 꺼짐 | TCP 바이너리 패킷을 부분 전송합니다. |
| `--coalesced-packet` | 꺼짐 | 여러 TCP 바이너리 패킷을 합쳐 전송합니다. |
| `--udp-loss-percent` | `0` | UDP 테스트에서 클라이언트 송신 손실을 흉내 내는 비율입니다. |
| `--run-id` | UTC 타임스탬프 | CSV에 기록할 실행 식별자입니다. |
| `--machine-id` | OS 머신명 | 클라이언트 CSV에 기록할 부하 발생 머신 식별자입니다. 복수 머신 실행 시 명시하는 것을 권장합니다. |

### 송신 페이싱 (`--pacing`)

부하를 거는 방식이 두 가지입니다. 기본값은 `open`입니다.

| 값 | 동작 |
| --- | --- |
| `open` | 실행 시작 기준의 절대 일정대로 보냅니다. 응답을 기다리는 동안에도 다음 송신이 나갑니다. |
| `closed` | 응답을 받은 뒤에 다음 지연을 시작합니다. 예전 방식입니다. |

닫힌 루프에서는 한 번의 사이클이 `지연 + 왕복 시간`이 됩니다.
그래서 **서버가 느려지면 부하량도 함께 줄어듭니다.**
정작 서버가 힘들 때 부하가 약해지므로 지연 시간이 실제보다 좋게 측정되고, 성능 회귀를 놓치게 됩니다.

열린 루프는 송신 시각을 미리 정한 일정으로 고정합니다.
한 번 늦게 나가도 다음 송신은 원래 예정 시각을 따르므로 오차가 누적되지 않습니다.
요청과 응답은 본문 앞 8바이트에 실은 상관 ID로 짝지으므로 응답이 순서대로 오지 않아도 됩니다.

열린 루프는 **TCP 바이너리 프로토콜에만** 적용됩니다.
`--transport udp`와 `--protocol text-line`은 `--pacing open`을 지정해도 닫힌 루프로 동작합니다.

변경 전후를 비교할 때는 양쪽 실행의 페이싱을 반드시 맞춥니다.
`client_summary.csv`의 `pacing` 항목에 실제 적용된 값이 기록됩니다.

#### 부하를 내지 못할 때 원인 가리기

목표 레이트를 달성하지 못하면 원인이 서버인지 부하 발생기인지 구분해야 합니다.
`client_summary.csv`의 다음 항목을 봅니다.

| 키 | 의미 |
| --- | --- |
| `send_schedule_delay_p99_us` | 예정 시각보다 늦게 나간 송신의 지연입니다. 이 값이 크면 클라이언트가 일정을 따라가지 못하고 있습니다. |
| `send_skipped_in_flight` | 동시 요청 한도에 걸려 보내지 못한 송신 수입니다. |
| `max_in_flight_observed` | 실행 중 관측된 최대 동시 요청 수입니다. 한도에 붙어 있으면 한도를 올려야 합니다. |

`send_schedule_delay_p99_us`가 작고 `send_skipped_in_flight`가 0인데 달성률이 낮다면 부하 발생기는 정상이며 다른 원인을 봐야 합니다.
반대로 이 값들이 크면 클라이언트 머신이 한계에 닿은 것이므로 부하를 여러 머신에 나눠야 합니다.

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

### 이상 상황 시나리오

서버가 정상 트래픽이 아닌 상황에서 어떻게 버티는지 보는 실행입니다.

#### 순간 폭주 (`--scenario burst`)

기본 레이트 위에 주기마다 한 뭉치를 얹습니다.
열린 루프이므로 그 뭉치가 응답 대기에 막히지 않고 실제로 몰려 나갑니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp --protocol echo-binary --host 127.0.0.1 --port 2012 `
  --clients 500 --duration 00:10:00 --send-rate-per-client 1.0 `
  --scenario burst --burst-every 00:00:30 --burst-size 50 `
  --output logs\loadtest\burst-500
```

동시 요청 한도가 모자라면 보낼 수 있는 만큼만 나가고 부족분이
`send_skipped_in_flight`에 기록됩니다. 폭주를 온전히 재현하려면 `--max-in-flight`를 함께 올립니다.

#### 비정상 종료 (`--abort-percent`)

지정한 비율의 클라이언트가 실행이 끝날 때 FIN 대신 RST를 보냅니다.
모바일 환경에서 흔한 끊김이며, 서버가 이 경로에서 예외를 내거나 세션을 남기지 않아야 합니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport tcp --protocol echo-binary --host 127.0.0.1 --port 2012 `
  --clients 1000 --duration 00:05:00 --send-rate-per-client 1.0 `
  --abort-percent 30 `
  --output logs\loadtest\abort-1000
```

확인할 값은 서버의 `exception_total`이 0인지, 최종 `active_sessions`가 0으로 돌아오는지입니다.

#### 대용량 페이로드 (`--payload huge`)

패킷 헤더의 `totalSize`가 `Int16`이므로 본문은 최대 32,762바이트입니다.
`huge`는 그 한계에 붙여 보내 서버의 조립 경로를 흔듭니다.
`mixed-huge`는 대부분 작은 요청 사이에 가끔 큰 요청을 섞습니다.

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

`SuperSocketLite.LoadTest.Server`는 세 가지 리스너를 함께 띄울 수 있습니다.
`--text-port`와 `--udp-port`를 지정하면 TCP 바이너리와 나란히 열립니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Server -- `
  --port 2012 `
  --text-port 2013 `
  --udp-port 2014 `
  --max-connections 1000 `
  --output logs\loadtest\multiproto-server `
  --duration 00:06:00
```

세 리스너는 같은 계측기를 공유합니다.
GC·메모리·CPU는 프로세스 단위 값이라 리스너마다 따로 재는 것이 의미가 없고,
세션과 요청 수는 합산해서 보는 편이 서버 전체 부하를 읽기 쉽기 때문입니다.

텍스트 라인 서버에는 다음처럼 접속합니다.

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

UDP 에코 서버에는 다음처럼 접속합니다.

```powershell
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Client -- `
  --transport udp `
  --protocol udp-echo `
  --host 127.0.0.1 `
  --port 2014 `
  --clients 100 `
  --duration 00:05:00 `
  --udp-loss-percent 0 `
  --output logs\loadtest\udp-client
```

UDP 데이터그램은 4바이트 키 + 36바이트 세션 GUID + 페이로드로 구성됩니다.
앞의 40바이트는 라이브러리가 UDP 세션을 식별하는 규약입니다.
이 프로토콜에는 요청과 응답을 짝지을 상관 ID 자리가 없으므로
UDP와 text-line은 `--pacing open`을 지정해도 항상 닫힌 루프로 동작합니다.

## CSV 출력

실행 결과는 지정한 `--output` 디렉토리에 CSV로 기록됩니다.

| 파일 | 생성 주체 | 설명 |
| --- | --- | --- |
| `server_samples.csv` | 서버 | 활성 세션, 요청 수, 처리량, GC, 메모리, CPU, 핸들러 지연 시간 등 주기 샘플입니다. |
| `server_events.csv` | 서버 | 접속, 종료, 오류, 샘플링된 요청 이벤트입니다. |
| `client_samples.csv` | 클라이언트 | 활성 클라이언트, 접속/종료, 송수신, 타임아웃, RTT 분위수 등 주기 샘플입니다. |
| `client_operations.csv` | 클라이언트 | 샘플링된 개별 요청/응답 지연 시간입니다. |
| `client_summary.csv` | 클라이언트 | 실행 전체 요약입니다. **결과를 볼 때 이 파일을 먼저 봅니다.** |

클라이언트 CSV에는 `machine_id` 컬럼이 포함됩니다.
복수 머신 실행 결과를 분석할 때 이 값으로 부하 발생 머신별 결과를 구분합니다.

고부하 테스트에서는 `client_operations.csv`가 병목이 될 수 있습니다.
이 경우 `--operation-sampling 0.01`처럼 샘플링 비율을 낮춥니다.
집계 샘플만 남기고 개별 작업 기록을 끄려면 `--operation-sampling 0.0`을 사용합니다.

### 구간(phase) 컬럼

`client_samples.csv`와 `server_samples.csv`의 각 행에는 그 시점이 어떤 구간인지가 기록됩니다.

| 값 | 의미 |
| --- | --- |
| `rampup` | 목표 접속 수에 도달하는 중입니다. |
| `steady` | 목표 부하가 걸린 정상 구간입니다. |
| `rampdown` | 종료 절차에 들어갔습니다. |
| `idle` | 접속이 없습니다. 서버만 켜 둔 구간입니다. |

클라이언트는 자신의 접속 일정을 알고 있으므로 그것으로 판정합니다.
서버는 클라이언트의 계획을 모르므로 활성 세션 수의 변화로 추정합니다.

분석 뷰는 `steady` 구간만 평균합니다.
서버를 클라이언트보다 오래 켜 두면 무부하 구간이 평균을 끌어내려 실제 처리량의 절반 이하로 보이게 되기 때문입니다.

### 실행 전체 요약 (`client_summary.csv`)

`key`, `value` 형식이며 실행이 끝날 때 한 번 기록됩니다.

주요 항목입니다.

| 키 | 설명 |
| --- | --- |
| `rtt_total_p50_us` … `rtt_total_p999_us` | **실행 전체** RTT 분위수입니다. p50, p90, p95, p99, p99.9, max를 기록합니다. |
| `rtt_total_count` | 분위수 계산에 쓰인 응답 수입니다. |
| `target_send_rate_per_sec` | 요청한 목표 송신 레이트입니다. `--clients` × `--send-rate-per-client`입니다. |
| `steady_send_rate_per_sec` | 정상 구간에서 실제로 달성한 송신 레이트입니다. |
| `steady_rate_achievement` | 목표 대비 달성률입니다. `1.0`이 목표 달성입니다. |
| `response_rate` | 송신 대비 응답 수신 비율입니다. |
| `pacing` | 실제로 적용된 페이싱 방식입니다. `open` 또는 `closed`입니다. |
| `max_in_flight` | 적용된 동시 요청 한도입니다. |
| `send_schedule_delay_p99_us` | 예정 시각 대비 송신 지연입니다. 클라이언트 포화를 판단합니다. |
| `send_skipped_in_flight` | 동시 요청 한도에 걸려 건너뛴 송신 수입니다. |
| `max_in_flight_observed` | 관측된 최대 동시 요청 수입니다. |

`rtt_total_*` 값은 모든 응답을 세는 히스토그램에서 나옵니다.
따라서 `--operation-sampling` 값에 영향을 받지 않습니다.
샘플링을 `0.01`로 낮춰도 `client_operations.csv`에는 1%만 남지만 분위수는 응답 100%를 기준으로 계산됩니다.

`steady_rate_achievement`가 `1.0`보다 크게 낮으면 요청한 만큼 부하를 걸지 못한 실행입니다.
이때의 지연 시간 수치는 의도한 것보다 가벼운 테스트의 결과이므로 비교 대상으로 쓰면 안 됩니다.

## DuckDB 분석

CSV 파일을 `logs\loadtest` 아래에 둔 상태에서 다음 명령을 실행합니다.

```powershell
duckdb loadtest.duckdb -init Test\LoadTest\analysis\duckdb_loadtest.sql
```

DuckDB 콘솔에서 자주 쓰는 분석 뷰를 조회합니다.

```sql
-- 실행 전체 요약. 분위수와 목표 달성률이 함께 나오므로 여기서 시작합니다.
SELECT * FROM analysis_run_summary;

-- 구간별 샘플 수. steady 샘플이 0이면 비교할 수 있는 측정이 없다는 뜻입니다.
SELECT * FROM analysis_phase_breakdown;

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
