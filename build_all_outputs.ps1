# ======================================================================
# TelemetryDashboard - Dual Build Script (Framework-Dependent vs Single-File)
# ======================================================================
$ErrorActionPreference = "Stop"
Stop-Process -Name TelemetryDashboard.UI,VBCSCompiler,dotnet -Force -ErrorAction SilentlyContinue

$projectPath = "TelemetryDashboard\TelemetryDashboard.UI\TelemetryDashboard.UI.csproj"
$frameworkDir = Join-Path $PSScriptRoot "dist_framework_dependent"
$singleFileDir = Join-Path $PSScriptRoot "dist_single_file"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "📦 Building Dual TelemetryDashboard Packages for Comparison" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Build Framework-Dependent Package
Write-Host "`n⚙ [1/2] Building Framework-Dependent Package..." -ForegroundColor Yellow
if (Test-Path $frameworkDir) { Remove-Item -Path $frameworkDir -Recurse -Force }
dotnet publish $projectPath -c Release -o $frameworkDir
Copy-Item -Path "Logo_Gemini.png" -Destination $frameworkDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path "Logo_Gemini.ico" -Destination $frameworkDir -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $PSScriptRoot -Filter "*.html" | Copy-Item -Destination $frameworkDir -Force
Get-ChildItem -Path $PSScriptRoot -Filter "*.js" | Copy-Item -Destination $frameworkDir -Force
Get-ChildItem -Path $PSScriptRoot -Filter "*.md" | Copy-Item -Destination $frameworkDir -Force

# 2. Build Self-Contained Single-File Package
Write-Host "`n⚙ [2/2] Building Self-Contained Single-File Package..." -ForegroundColor Yellow
if (Test-Path $singleFileDir) { Remove-Item -Path $singleFileDir -Recurse -Force }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o $singleFileDir
Copy-Item -Path "Logo_Gemini.png" -Destination $singleFileDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path "Logo_Gemini.ico" -Destination $singleFileDir -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $PSScriptRoot -Filter "*.html" | Copy-Item -Destination $singleFileDir -Force
Get-ChildItem -Path $PSScriptRoot -Filter "*.js" | Copy-Item -Destination $singleFileDir -Force
Get-ChildItem -Path $PSScriptRoot -Filter "*.md" | Copy-Item -Destination $singleFileDir -Force

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "🎉 DUAL BUILD COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green

# Print comparison summary
if (Test-Path "$frameworkDir\TelemetryDashboard.UI.exe") {
    $fwSize = (Get-ChildItem -Path $frameworkDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
    $fwFiles = (Get-ChildItem -Path $frameworkDir -Recurse -File).Count
    Write-Host "`n📁 1. Framework-Dependent Output (dist_framework_dependent\):" -ForegroundColor Cyan
    Write-Host "   - Exe Location: dist_framework_dependent\TelemetryDashboard.UI.exe" -ForegroundColor White
    Write-Host "   - Total Size  : $([math]::Round($fwSize, 2)) MB ($fwFiles files)" -ForegroundColor Gray
    Write-Host "   - Requirement : Target PC requires .NET 8 Desktop Runtime installed." -ForegroundColor DarkGray
}

if (Test-Path "$singleFileDir\TelemetryDashboard.UI.exe") {
    $sfExe = Get-Item "$singleFileDir\TelemetryDashboard.UI.exe"
    $sfSize = $sfExe.Length / 1MB
    Write-Host "`n📁 2. Self-Contained Single-File Output (dist_single_file\):" -ForegroundColor Cyan
    Write-Host "   - Exe Location: dist_single_file\TelemetryDashboard.UI.exe" -ForegroundColor White
    Write-Host "   - Single Exe   : $([math]::Round($sfSize, 2)) MB (1 Standalone File)" -ForegroundColor Gray
    Write-Host "   - Requirement : NO .NET Runtime required! Works everywhere." -ForegroundColor DarkGray
}
Write-Host "============================================================" -ForegroundColor Green
