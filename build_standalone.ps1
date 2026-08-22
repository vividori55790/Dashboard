# =====================================================================
#  TelemetryDashboard - standalone build
# =====================================================================
#  Produces self-contained, single-file executables that run on a machine
#  with no .NET installed.
#
#  Two products, because they are genuinely different things:
#
#    Host  - the telemetry backbone. Headless, cross-platform. This is the
#            one that "runs on every computer": the operator reaches it
#            through a browser, so Windows, macOS and Linux all get the
#            full experience.
#    UI    - the WPF operator console. Windows only, by construction. One
#            client among several, not the product.
#
#  Trimming is deliberately OFF. Jint, IronPython and the collectible
#  AssemblyLoadContext that loads plugins all resolve types by reflection,
#  and the trimmer cannot see those references. A trimmed build would be
#  smaller, publish cleanly, and then fail at runtime the first time an
#  operator loaded a plugin - which is the exact class of silent failure
#  this project exists to avoid.
# =====================================================================
$ErrorActionPreference = "Stop"

$root      = $PSScriptRoot
$solution  = Join-Path $root "TelemetryDashboard"
$hostProj  = Join-Path $solution "TelemetryDashboard.Host\TelemetryDashboard.Host.csproj"
$uiProj    = Join-Path $solution "TelemetryDashboard.UI\TelemetryDashboard.UI.csproj"
$outRoot   = Join-Path $root "dist"

# Runtime identifiers for the headless host. Add one here and it ships.
$hostRids  = @("win-x64", "linux-x64", "osx-arm64")

$common = @(
    "-c", "Release",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishTrimmed=false",
    "-p:DebugType=embedded",
    "--nologo"
)

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " TelemetryDashboard - standalone build" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# A running instance holds its own executable open and the publish fails
# with a lock error that looks nothing like the real cause.
Stop-Process -Name TelemetryDashboard.UI,TelemetryDashboard.Host,VBCSCompiler -Force -ErrorAction SilentlyContinue

if (Test-Path $outRoot) { Remove-Item -Path $outRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

# --- Gate: never ship a build whose tests do not pass --------------------
Write-Host "`n[gate] running the test suite before publishing..." -ForegroundColor Yellow
dotnet test (Join-Path $solution "TelemetryDashboard.sln") --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed. Refusing to produce a standalone build from a red tree."
}

# --- Web console assets, shipped beside every host ----------------------
# For a non-Windows operator the browser console IS the interface, so these
# are part of the product rather than extras.
function Copy-WebAssets($destination) {
    $assets = Join-Path $destination "webroot"
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    Get-ChildItem -Path $root -Filter "*.html" -File | Copy-Item -Destination $assets -Force
    # verify_*.js are the development harnesses, not product. They were shipping into every
    # package because this line matched the same wildcard the pages do.
    Get-ChildItem -Path $root -Filter "*.js"   -File |
        Where-Object { $_.Name -notlike "verify_*" } | Copy-Item -Destination $assets -Force
    return $assets
}

# --- Headless host, one package per runtime -----------------------------
foreach ($rid in $hostRids) {
    $dest = Join-Path $outRoot "host-$rid"
    Write-Host "`n[host] publishing $rid ..." -ForegroundColor Yellow

    dotnet publish $hostProj @common -r $rid -o $dest
    if ($LASTEXITCODE -ne 0) { throw "Host publish failed for $rid" }

    Copy-WebAssets $dest | Out-Null
    Copy-Item -Path (Join-Path $root "PROJECT.md") -Destination $dest -Force -ErrorAction SilentlyContinue
}

# --- WPF console, Windows only ------------------------------------------
Write-Host "`n[ui] publishing win-x64 (WPF is Windows-only by construction) ..." -ForegroundColor Yellow
$uiDest = Join-Path $outRoot "desktop-win-x64"
dotnet publish $uiProj @common -r win-x64 -o $uiDest
if ($LASTEXITCODE -ne 0) { throw "UI publish failed" }
Copy-Item -Path (Join-Path $root "Logo_Gemini.ico") -Destination $uiDest -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $root "Logo_Gemini.png") -Destination $uiDest -Force -ErrorAction SilentlyContinue

# --- Report what actually exists on disk --------------------------------
Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host " Output" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$expected = @{
    "host-win-x64"      = "TelemetryDashboard.Host.exe"
    "host-linux-x64"    = "TelemetryDashboard.Host"
    "host-osx-arm64"    = "TelemetryDashboard.Host"
    "desktop-win-x64"   = "TelemetryDashboard.UI.exe"
}

$missing = @()
foreach ($package in $expected.Keys | Sort-Object) {
    $exe = Join-Path (Join-Path $outRoot $package) $expected[$package]
    if (Test-Path $exe) {
        $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        "{0,-20} {1,8} MB   {2}" -f $package, $mb, $expected[$package] | Write-Host
    } else {
        $missing += "$package/$($expected[$package])"
        Write-Host ("{0,-20} MISSING" -f $package) -ForegroundColor Red
    }
}

if ($missing.Count -gt 0) {
    throw "Publish reported success but these executables are absent: $($missing -join ', ')"
}

Write-Host "`nAll packages are in: $outRoot" -ForegroundColor Green
Write-Host "The host needs no .NET runtime installed. Run it, then open the" -ForegroundColor Green
Write-Host "printed URL in a browser - including from a phone on the same LAN," -ForegroundColor Green
Write-Host "which needs the host started with remote connections enabled." -ForegroundColor Green
