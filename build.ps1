# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-22)
# - Target Environment: Windows PowerShell 5.1+
# - Integrity Check: Resolves dependencies, sets up .venv, and compiles PyInstaller SPEC
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.0.0 (2026-05-22) - Antigravity: Created one-click PowerShell packaging automation runner.
# ======================================================================

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "📦 Universal Embedded Telemetry Monitor - Packaging Runner" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Check Python installation
try {
    $pythonVer = & python --version 2>&1
    Write-Host "✔ Detected Python: $pythonVer" -ForegroundColor Green
} catch {
    Write-Error "CRITICAL: Python is not installed or not added to your system PATH."
    Exit
}

# 2. Setup Virtual Environment if not present
$venvPath = Join-Path $PSScriptRoot ".venv"
if (-not (Test-Path $venvPath)) {
    Write-Host "⚙ Creating a clean Python virtual environment (.venv)..." -ForegroundColor Yellow
    & python -m venv .venv
    Write-Host "✔ Virtual environment successfully created." -ForegroundColor Green
}

# 3. Resolve virtual environment paths for activation
$pipExe = Join-Path $venvPath "Scripts\pip.exe"
$pyExe = Join-Path $venvPath "Scripts\python.exe"
$pyinstallerExe = Join-Path $venvPath "Scripts\pyinstaller.exe"

# 4. Install production dependencies
Write-Host "⚙ Verifying and installing required python libraries..." -ForegroundColor Yellow
& $pipExe install --upgrade pip
& $pipExe install PyQt6 pyqtgraph pyserial pyinstaller

# 5. Clean up stale build folders
Write-Host "⚙ Clearing obsolete compilation artifacts..." -ForegroundColor Yellow
foreach ($folder in ("build", "dist")) {
    $target = Join-Path $PSScriptRoot $folder
    if (Test-Path $target) {
        Remove-Item -Path $target -Recurse -Force
        Write-Host "✔ Removed stale folder: $folder" -ForegroundColor DarkGray
    }
}

# 6. Trigger PyInstaller compiler
Write-Host "🚀 Triggering PyInstaller compiler utilizing embedded_monitor.spec..." -ForegroundColor Cyan
try {
    & $pyinstallerExe --clean embedded_monitor.spec
    
    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host "🎉 COMPILATION SUCCESSFULLY COMPLETED!" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    
    $outputDir = Join-Path $PSScriptRoot "dist\EmbeddedTelemetryMonitor"
    Write-Host "📂 Output Packaged Directory:" -ForegroundColor Gray
    Write-Host "   $outputDir" -ForegroundColor Yellow
    Write-Host "`n💡 To launch your standalone app:" -ForegroundColor Gray
    Write-Host "   Double-click: dist\EmbeddedTelemetryMonitor\EmbeddedTelemetryMonitor.exe" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
} catch {
    Write-Host "`n❌ COMPILATION FAILED!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Ensure no other processes are locking the 'dist/' folder." -ForegroundColor Yellow
}
