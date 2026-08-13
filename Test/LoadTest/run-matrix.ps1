<#
.SYNOPSIS
    여러 시나리오를 연속으로 돌리고 하나의 리포트로 모은다.

.DESCRIPTION
    시나리오마다 run_id를 <Prefix>-<이름> 으로 붙여 실행한다.
    포트는 조합마다 다르게 잡아 앞선 실행의 TIME_WAIT 소켓과 부딪히지 않게 한다.

    기본 매트릭스는 정상 부하와 이상 상황을 함께 훑는다.
    -Matrix 로 직접 정의하면 원하는 조합만 돌릴 수 있다.

.EXAMPLE
    .\run-matrix.ps1 -Prefix nightly -Clients 200 -Duration 00:01:00

.EXAMPLE
    .\run-matrix.ps1 -Prefix quick -Matrix @(
        @{ Name = 'echo';  Scenario = 'echo';  Payload = 'small' },
        @{ Name = 'burst'; Scenario = 'burst'; Payload = 'mixed' }
    )
#>
[CmdletBinding()]
param(
    [string] $Prefix = "matrix",
    [int]    $Clients = 100,
    [string] $Duration = "00:00:30",
    [string] $RampUp = "00:00:05",
    [double] $SendRate = 5.0,
    [int]    $BasePort = 2700,
    [string] $LogRoot = "logs\loadtest",
    [string] $Thresholds,
    [hashtable[]] $Matrix
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot "run-loadtest.ps1"

if (-not $Matrix) {
    # 정상 부하로 기준을 잡고, 이상 상황을 차례로 얹는다.
    $Matrix = @(
        @{ Name = 'echo';      Scenario = 'echo';            Payload = 'small' },
        @{ Name = 'game';      Scenario = 'game-like';       Payload = 'mixed' },
        @{ Name = 'burst';     Scenario = 'burst';           Payload = 'mixed';      Extra = @('--burst-every','00:00:05','--burst-size','30') },
        @{ Name = 'huge';      Scenario = 'echo';            Payload = 'mixed-huge' },
        @{ Name = 'abort';     Scenario = 'echo';            Payload = 'small';      Extra = @('--abort-percent','30') },
        @{ Name = 'reconnect'; Scenario = 'reconnect-storm'; Payload = 'small';      Extra = @('--reconnect-percent','100','--storm-at','00:00:10','--storm-percent','40','--storm-window','00:00:05') },
        # 부하 중 서버를 죽였다 살린다. 실행 시간의 절반쯤에서 내려야 회복까지 관측된다.
        @{ Name = 'fault';     Scenario = 'echo';            Payload = 'small';      KillServerAt = '00:00:15'; ServerDowntime = '00:00:05' }
    )
}

$port = $BasePort
$failed = @()

foreach ($case in $Matrix) {
    $name = $case.Name
    $runId = "$Prefix-$name"
    $extra = if ($case.ContainsKey('Extra')) { $case.Extra } else { @() }
    $scenario = if ($case.ContainsKey('Scenario')) { $case.Scenario } else { 'echo' }
    $payload = if ($case.ContainsKey('Payload')) { $case.Payload } else { 'small' }

    # 조합마다 서버 계측 수준과 장애 주입을 따로 정할 수 있다.
    $runnerArgs = @{}
    if ($case.ContainsKey('Metrics'))        { $runnerArgs['Metrics'] = $case.Metrics }
    if ($case.ContainsKey('KillServerAt'))   { $runnerArgs['KillServerAt'] = $case.KillServerAt }
    if ($case.ContainsKey('ServerDowntime')) { $runnerArgs['ServerDowntime'] = $case.ServerDowntime }

    Write-Host ""
    Write-Host "=== $runId ===" -ForegroundColor Cyan

    try {
        & $runner `
            -RunId $runId `
            -Clients $Clients `
            -Duration $Duration `
            -RampUp $RampUp `
            -SendRate $SendRate `
            -Scenario $scenario `
            -Payload $payload `
            -Port $port `
            -LogRoot $LogRoot `
            -ExtraClientArgs $extra `
            -SkipReport `
            @runnerArgs
    }
    catch {
        Write-Host "$runId 실패: $_" -ForegroundColor Red
        $failed += $runId
    }

    $port++
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "실패한 조합: $($failed -join ', ')" -ForegroundColor Red
}

# 매트릭스 전체를 한 리포트로 모은다. 접두사가 같으므로 하나의 묶음으로 읽힌다.
$reportArgs = @(
    'run', '--project', 'Test\LoadTest\SuperSocketLite.LoadTest.Report', '-c', 'Release', '--no-build', '--',
    '--input', $LogRoot,
    '--run', $Prefix,
    '--output', (Join-Path $LogRoot "$Prefix-report.html")
)
if ($Thresholds) { $reportArgs += @('--thresholds', $Thresholds) }

Write-Host ""
& dotnet @reportArgs
exit $LASTEXITCODE
