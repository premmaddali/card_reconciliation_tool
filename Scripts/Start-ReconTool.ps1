[CmdletBinding()]
param()

$repoRoot = Split-Path -Parent $PSScriptRoot
$stateDir = Join-Path $repoRoot ".recontool-runtime"
$pidFile = Join-Path $stateDir "web.pid"
$stdoutLog = Join-Path $stateDir "web.stdout.log"
$stderrLog = Join-Path $stateDir "web.stderr.log"

New-Item -ItemType Directory -Path $stateDir -Force | Out-Null

if (Test-Path -LiteralPath $pidFile) {
    $existingPid = Get-Content -LiteralPath $pidFile -Raw
    if ($existingPid -match '^\d+$' -and (Get-Process -Id ([int]$existingPid) -ErrorAction SilentlyContinue)) {
        Write-Host "ReconTool is already running (PID $existingPid)."
        exit 0
    }
    Remove-Item -LiteralPath $pidFile -Force
}

$listener = Get-NetTCPConnection -State Listen -LocalPort 5104 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    Write-Host "Port 5104 is already in use (PID $($listener.OwningProcess))."
    exit 0
}

$startArgs = @{
    FilePath = "dotnet"
    ArgumentList = @("run", "--project", "Recon.Web", "--launch-profile", "http")
    WorkingDirectory = $repoRoot
    RedirectStandardOutput = $stdoutLog
    RedirectStandardError = $stderrLog
    WindowStyle = "Hidden"
    PassThru = $true
}
$process = Start-Process @startArgs

$webPid = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Milliseconds 500
    $listener = Get-NetTCPConnection -State Listen -LocalPort 5104 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) { $webPid = $listener.OwningProcess; break }
    if ($process.HasExited) { break }
}

if ($null -eq $webPid) {
    throw "ReconTool did not start listening on port 5104. Check $stderrLog"
}

Set-Content -LiteralPath $pidFile -Value $webPid -NoNewline
Write-Host "ReconTool started (PID $webPid)."
Write-Host "Open http://localhost:5104 once startup completes."
Write-Host "Logs: $stdoutLog"
