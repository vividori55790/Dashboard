# =====================================================================
#  Captures the desktop window for visual inspection.
# =====================================================================
#  Use this rather than an ad-hoc screenshot, because an ad-hoc
#  screenshot on this hardware lies.
#
#  The display runs at 125%. A process that has not declared itself
#  DPI-aware is handed virtualised coordinates by Windows: GetWindowRect
#  reports the window as 1550 wide when it is physically 1938, and
#  PrintWindow returns a bitmap cropped to that smaller figure. The
#  right-hand 390 pixels simply are not in the image.
#
#  That artefact cost real time. It was reported as a clipped panel,
#  investigated twice, and handed to someone who measured the layout,
#  found nothing wrong, and said so -- correctly. The runtime numbers
#  and the picture disagreed, and the picture was the one lying:
#  PointToScreen put the panel at x 1339..1906 on a 1920-pixel screen,
#  comfortably inside it, while the capture showed it running off the
#  edge. One SetProcessDPIAware call is the whole difference.
#
#  The lesson generalises past this script: when a measurement and an
#  observation disagree, the instrument is a suspect too.
# =====================================================================
param(
    [string]$Exe = "$PSScriptRoot\TelemetryDashboard\TelemetryDashboard.UI\bin\Debug\net8.0-windows\TelemetryDashboard.UI.exe",
    [string]$Out = "$PSScriptRoot\ui-capture.png",
    [int]$SettleSeconds = 22,
    [switch]$Maximize = $true
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public class UiCapture {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public struct RECT { public int L, T, R, B; }

  // PW_RENDERFULLCONTENT (2): renders a window that is not on top, and
  // captures only that window -- never whatever else is on the desktop.
  public static Bitmap Shot(IntPtr h) {
    RECT r; GetWindowRect(h, out r);
    var bmp = new Bitmap(r.R - r.L, r.B - r.T);
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2);
      g.ReleaseHdc(dc);
    }
    return bmp;
  }
}
"@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives

# Before any window handle is touched. Declaring awareness later has no effect.
[UiCapture]::SetProcessDPIAware() | Out-Null

if (-not (Test-Path $Exe)) { throw "not built: $Exe" }

$process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -PassThru
try {
    Start-Sleep -Seconds $SettleSeconds
    $process.Refresh()
    if ($process.HasExited) { throw "the application exited with code $($process.ExitCode) before it could be captured" }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw "no window appeared within $SettleSeconds seconds" }

    if ($Maximize) { [UiCapture]::ShowWindow($process.MainWindowHandle, 3) | Out-Null; Start-Sleep -Seconds 5 }

    $bitmap = [UiCapture]::Shot($process.MainWindowHandle)
    $bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    "captured $($bitmap.Width) x $($bitmap.Height) physical pixels -> $Out"
    $bitmap.Dispose()

    $crash = Join-Path (Split-Path $Exe) "crash_log.txt"
    if (Test-Path $crash) { "crash_log.txt was written:"; Get-Content $crash -TotalCount 6 }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
