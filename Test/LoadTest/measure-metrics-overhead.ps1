<#
.SYNOPSIS
    계측이 측정 대상에 주는 영향을 수치로 남긴다.

.DESCRIPTION
    같은 부하를 서버 계측 수준만 바꿔 세 번 돌리고 비교한다.

        full       요청 계측 · 주기 표본 · 런타임 게이지를 모두 켠다 (기본 운영 상태)
        no-gauges  런타임 게이지(송신 큐 깊이 · SAEA 풀)만 끈다
        off        서버 계측을 전부 끈다

    off 기준으로 full을 비교하면 서버 계측 전체의 비용이 나오고,
    no-gauges 기준으로 full을 비교하면 런타임 게이지만의 비용이 나온다.

    계측을 끈 실행에는 서버 CSV가 없으므로 처리량은 클라이언트가 보낸 정상 구간 레이트로 견준다.
    판정 도구가 이 경우를 알아서 "처리량 (클라이언트 기준)"으로 바꿔 표시한다.

    꼬리 지연은 실행마다 흔들리므로 -Repeat 는 3회 이상을 권한다.

.EXAMPLE
    .\measure-metrics-overhead.ps1 -Prefix ovh -Clients 300 -Duration 00:01:00 -Repeat 3
#>
[CmdletBinding()]
param(
    [string] $Prefix = "overhead",
    [int]    $Repeat = 3,
    [int]    $Clients = 200,
    [string] $Duration = "00:00:30",
    [string] $RampUp = "00:00:05",
    [double] $SendRate = 5.0,
    [string] $Scenario = "echo",
    [string] $Payload = "small",
    [int]    $BasePort = 2800,
    [string] $LogRoot = "logs\loadtest",
    [string] $Thresholds
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot "run-loadtest.ps1"
$reportProject = "Test\LoadTest\SuperSocketLite.LoadTest.Report"

$modes = @('full', 'no-gauges', 'off')
$port = $BasePort

foreach ($mode in $modes) {
    $runId = "$Prefix-$mode"
    Write-Host ""
    Write-Host "=== $runId ($Repeat 회) ===" -ForegroundColor Cyan

    & $runner `
        -RunId $runId `
        -Repeat $Repeat `
        -Clients $Clients `
        -Duration $Duration `
        -RampUp $RampUp `
        -SendRate $SendRate `
        -Scenario $Scenario `
        -Payload $Payload `
        -Port $port `
        -LogRoot $LogRoot `
        -Metrics $mode `
        -SkipReport

    $port++
}

<#
.SYNOPSIS
    두 묶음을 견주는 리포트를 만들고 판정을 콘솔에 남긴다.
#>
function Invoke-Comparison {
    param([string] $Name, [string] $BaselinePrefix, [string] $CandidatePrefix)

    $output = Join-Path $LogRoot "$Prefix-$Name.html"
    $reportArgs = @(
        'run', '--project', $reportProject, '-c', 'Release', '--no-build', '--',
        '--input', $LogRoot,
        '--baseline', $BaselinePrefix,
        '--run', $CandidatePrefix,
        '--output', $output
    )
    if ($Thresholds) { $reportArgs += @('--thresholds', $Thresholds) }

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    Write-Host "기준 $BaselinePrefix · 대상 $CandidatePrefix"
    & dotnet @reportArgs
}

# 계측을 끈 실행이 기준이다. 대상(full)이 그보다 느려진 만큼이 계측의 비용이다.
Invoke-Comparison -Name 'all-instrumentation' -BaselinePrefix "$Prefix-off" -CandidatePrefix "$Prefix-full"

# 게이지만의 비용. 나머지 계측은 양쪽 다 켜져 있다.
Invoke-Comparison -Name 'runtime-gauges' -BaselinePrefix "$Prefix-no-gauges" -CandidatePrefix "$Prefix-full"

Write-Host ""
Write-Host "두 리포트의 'p99 지연'과 '처리량' 줄이 계측의 비용이다." -ForegroundColor Yellow
Write-Host "판정이 불합격으로 나와도 회귀가 아니라 계측 비용이 임계값을 넘었다는 뜻이다."
