<#
.SYNOPSIS
    부하 테스트를 서버 기동부터 리포트까지 한 번에 실행한다.

.DESCRIPTION
    서버를 띄우고, 리슨이 열릴 때까지 기다린 뒤 클라이언트를 돌리고, 서버를 정리한다.
    -Repeat 로 같은 조건을 여러 번 돌리면 실행마다 run_id에 번호가 붙는다.
    꼬리 지연은 실행마다 흔들리므로 비교용 실행은 3회 이상을 권장한다.

    서버가 준비되기를 기다리는 이유가 있다. 고정 시간 대기로는 기동이 느린 날 클라이언트가
    먼저 붙어 실패하고, 반대로 서버 실행 시간이 끝난 뒤 클라이언트가 도는 일도 생긴다.
    실제로 그 때문에 정상 동작을 결함으로 오인한 적이 있다.

.EXAMPLE
    .\run-loadtest.ps1 -RunId smoke -Clients 100 -Duration 00:00:30

.EXAMPLE
    .\run-loadtest.ps1 -RunId base -Repeat 3 -Clients 500 -Duration 00:02:00
    .\run-loadtest.ps1 -RunId cand -Repeat 3 -Clients 500 -Duration 00:02:00
    .\run-loadtest.ps1 -ReportOnly -Baseline base -Candidate cand -FailOnRegression
#>
[CmdletBinding()]
param(
    [string]   $RunId = "run",
    [int]      $Repeat = 1,
    [int]      $Clients = 100,
    [string]   $Duration = "00:00:30",
    [string]   $RampUp = "00:00:05",
    [double]   $SendRate = 5.0,
    [string]   $Scenario = "echo",
    [string]   $Payload = "small",
    [string]   $Pacing = "open",
    [string]   $Transport = "tcp",
    [string]   $Protocol = "echo-binary",
    [double]   $OperationSampling = 1.0,
    [int]      $Port = 2012,
    [int]      $TextPort = 0,
    [int]      $UdpPort = 0,
    [int]      $MaxConnections = 0,
    [string]   $LogRoot = "logs\loadtest",
    [string[]] $ExtraClientArgs = @(),

    # 리포트만 다시 만들 때 쓴다. 부하 실행은 건너뛴다.
    [switch]   $ReportOnly,
    [string]   $Baseline,
    [string]   $Candidate,
    [string]   $Thresholds,
    [string]   $ReportOutput,
    [switch]   $FailOnRegression,
    [switch]   $SkipReport
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$serverProject = "Test\LoadTest\SuperSocketLite.LoadTest.Server"
$clientProject = "Test\LoadTest\SuperSocketLite.LoadTest.Client"
$reportProject = "Test\LoadTest\SuperSocketLite.LoadTest.Report"

function Wait-ForListener {
    param([int] $ListenPort, [int] $TimeoutSeconds = 30)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $ok = Test-NetConnection -ComputerName '127.0.0.1' -Port $ListenPort -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($ok) { return $true }
    }

    return $false
}

function Invoke-SingleRun {
    param([string] $Id)

    $serverOutput = Join-Path $LogRoot "$Id-server"
    $clientOutput = Join-Path $LogRoot "$Id-client"

    # 서버는 클라이언트보다 넉넉히 오래 살아야 한다. 클라이언트가 끝나면 이 스크립트가 정리한다.
    $connections = if ($MaxConnections -gt 0) { $MaxConnections } else { [Math]::Max(100, $Clients * 2) }

    $serverArgs = @(
        'run', '--project', $serverProject, '-c', 'Release', '--no-build', '--',
        '--port', "$Port",
        '--max-connections', "$connections",
        '--output', $serverOutput,
        '--run-id', $Id
    )
    if ($TextPort -gt 0) { $serverArgs += @('--text-port', "$TextPort") }
    if ($UdpPort  -gt 0) { $serverArgs += @('--udp-port',  "$UdpPort") }

    Write-Host "[$Id] 서버 기동 (port $Port)"
    $server = Start-Process -FilePath 'dotnet' -PassThru -WindowStyle Hidden -ArgumentList $serverArgs

    try {
        $listenPort = if ($Transport -eq 'udp' -and $UdpPort -gt 0) { $UdpPort }
                      elseif ($Transport -eq 'text' -and $TextPort -gt 0) { $TextPort }
                      else { $Port }

        if ($Transport -eq 'udp') {
            # UDP는 연결이 없어 리슨 확인이 되지 않으므로 짧게 기다린다.
            Start-Sleep -Seconds 3
        }
        elseif (-not (Wait-ForListener -ListenPort $listenPort)) {
            throw "[$Id] 서버가 $listenPort 포트를 열지 못했다."
        }

        $clientArgs = @(
            'run', '--project', $clientProject, '-c', 'Release', '--no-build', '--',
            '--transport', $Transport,
            '--protocol', $Protocol,
            '--host', '127.0.0.1',
            '--port', "$listenPort",
            '--clients', "$Clients",
            '--ramp-up', $RampUp,
            '--duration', $Duration,
            '--send-rate-per-client', "$SendRate",
            '--scenario', $Scenario,
            '--payload', $Payload,
            '--pacing', $Pacing,
            '--operation-sampling', "$OperationSampling",
            '--output', $clientOutput,
            '--run-id', $Id
        ) + $ExtraClientArgs

        Write-Host "[$Id] 클라이언트 실행 ($Clients 클라이언트, $Duration)"
        & dotnet @clientArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "[$Id] 클라이언트가 코드 $LASTEXITCODE 로 끝났다." }
    }
    finally {
        # 서버는 --duration 없이 띄웠으므로 여기서 확실히 내린다.
        if (-not $server.HasExited) {
            $server | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        $server | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }

    Write-Host "[$Id] 완료"
}

if (-not $ReportOnly) {
    Write-Host "빌드 중..."
    & dotnet build Test\LoadTest\SuperSocketLite.LoadTest.sln -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "빌드에 실패했다." }

    for ($i = 1; $i -le $Repeat; $i++) {
        $id = if ($Repeat -eq 1) { $RunId } else { "{0}{1:D2}" -f $RunId, $i }
        Invoke-SingleRun -Id $id
    }
}

if ($SkipReport) { return }

$reportArgs = @('run', '--project', $reportProject, '-c', 'Release', '--no-build', '--', '--input', $LogRoot)

$candidatePrefix = if ($Candidate) { $Candidate } else { $RunId }
$reportArgs += @('--run', $candidatePrefix)

if ($Baseline)   { $reportArgs += @('--baseline', $Baseline) }
if ($Thresholds) { $reportArgs += @('--thresholds', $Thresholds) }

$output = if ($ReportOutput) { $ReportOutput } else { Join-Path $LogRoot "$candidatePrefix-report.html" }
$reportArgs += @('--output', $output)

if ($FailOnRegression) { $reportArgs += '--fail-on-regression' }

Write-Host ""
& dotnet @reportArgs
$reportExit = $LASTEXITCODE

if ($reportExit -ne 0) {
    Write-Host ""
    Write-Host "판정이 불합격이다. 종료 코드 $reportExit" -ForegroundColor Red
}

exit $reportExit
