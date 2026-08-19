[CmdletBinding()]
param()

$repoRoot = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $repoRoot ".recontool-runtime\web.pid"

$savedPid = if (Test-Path -LiteralPath $pidFile) { Get-Content -LiteralPath $pidFile -Raw } else { $null }
$process = if ($savedPid -match '^\d+$') { Get-Process -Id ([int]$savedPid) -ErrorAction SilentlyContinue } else { $null }

if ($null -eq $process) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort 5104 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) { $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue }
}

if ($null -eq $process) {
    if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile -Force }
    Write-Host "ReconTool was already stopped."
    exit 0
}

if ($process.ProcessName -notin @("dotnet", "Recon.Web")) {
    throw "Refusing to stop PID $($process.Id) because it is '$($process.ProcessName)', not ReconTool."
}

Stop-Process -Id $process.Id
$process.WaitForExit(10000) | Out-Null
if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile -Force }
Write-Host "ReconTool stopped (PID $($process.Id))."
