param(
    [switch]$NoFrontend,
    [switch]$KillOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

if ($KillOnly) {
    Write-Host "=== Killing all dotnet processes ===" -ForegroundColor Yellow
    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process PLC.* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 2
    Write-Host "Done" -ForegroundColor Green
    exit 0
}

Write-Host "=== Killing old processes ===" -ForegroundColor Yellow
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process PLC.* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2

Write-Host "=== Rebuilding solution ===" -ForegroundColor Cyan
dotnet build src/PLC-Simulator.sln --nologo 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }
Write-Host "Build OK" -ForegroundColor Green

Write-Host "=== Starting Supervisor (port 5000) ===" -ForegroundColor Cyan
$job = Start-Job -ScriptBlock {
    Set-Location $using:root
    dotnet run --project src/PLC.Supervisor --urls "http://0.0.0.0:5000"
}
Start-Sleep 8

# Wait for supervisor to be ready
$maxRetries = 20
$ready = $false
for ($i = 0; $i -lt $maxRetries; $i++) {
    try {
        $r = Invoke-RestMethod -Uri "http://localhost:5000/api/plcs" -Method Get -ErrorAction Stop
        if ($r.Count -gt 0) { $ready = $true; break }
    } catch { }
    Start-Sleep 2
}
if (-not $ready) { Write-Host "Supervisor failed to start" -ForegroundColor Red; exit 1 }
Write-Host "Supervisor ready ($($r.Count) PLCs loaded)" -ForegroundColor Green

Write-Host "=== Starting all 6 PLC workers ===" -ForegroundColor Cyan
$ids = @()
foreach ($p in $r) { $ids += $p.id }
foreach ($id in $ids) {
    try {
        Invoke-RestMethod -Uri "http://localhost:5000/api/plcs/$id/start" -Method Post -ErrorAction Stop | Out-Null
        Write-Host "  Started $id" -ForegroundColor Gray
    } catch {
        Write-Host "  FAILED $id" -ForegroundColor Red
    }
}
Start-Sleep 5

Write-Host "=== Checking workers ===" -ForegroundColor Cyan
$ports = @{102="S7";502="Modbus";44818="Rockwell";3005="MELSEC";48898="ADS";4840="OPCUA"}
$listening = netstat -ano | Select-String "LISTENING"
foreach ($pair in $ports.GetEnumerator()) {
    $found = $listening | Select-String ":$($pair.Key)\s"
    if ($found) { Write-Host "  $($pair.Value) :$($pair.Key) [OK]" -ForegroundColor Green }
    else { Write-Host "  $($pair.Value) :$($pair.Key) [DOWN]" -ForegroundColor Red }
}

Write-Host "=== All running ===" -ForegroundColor Green
Write-Host "Supervisor API: http://localhost:5000/api/plcs" -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray

# Wait for supervisor to exit
Wait-Job $job | Out-Null
Remove-Job $job -ErrorAction SilentlyContinue
