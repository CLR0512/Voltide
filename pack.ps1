param(
    [string]$Target
)

# Builds Voltide and drops a clean, runnable folder inside the project:
#   dist\Voltide.exe
#   dist\Fonts\  (three TTFs, without them the card typography falls back to Segoe UI)
#
# Default target is dist\ next to this script, so everything stays in one place.
# Override with: powershell -File pack.ps1 -Target "E:\somewhere"

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot   # this script lives next to build.ps1

if (-not $Target) { $Target = Join-Path $root 'dist' }

# A running copy locks the exe, so close it before compiling — not after.
$running = Get-Process Voltide -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing running copy..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

Write-Host "Building..." -ForegroundColor Cyan
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$exe = Join-Path $root 'Voltide.exe'
if (-not (Test-Path $exe)) { throw "Voltide.exe not found" }

Write-Host "Packing to $Target" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $Target -Force | Out-Null

# One file, nothing else: the three typefaces are manifest resources inside the exe.
Copy-Item $exe (Join-Path $Target 'Voltide.exe') -Force

Write-Host ""
Write-Host "--- packed ---" -ForegroundColor Green
Get-ChildItem $Target -Recurse -File |
    Select-Object @{n='Path';e={$_.FullName.Substring($Target.Length + 1)}}, @{n='KB';e={[math]::Round($_.Length / 1KB, 1)}} |
    Format-Table -AutoSize

$size = (Get-ChildItem $Target -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("total: {0:N1} MB" -f ($size / 1MB)) -ForegroundColor Gray
Write-Host ""
Write-Host "Run it with: $Target\Voltide.exe" -ForegroundColor Gray
