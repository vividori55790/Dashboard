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
    [switch]$Maximize = $true,

    # Toggle buttons to press, by their on-screen caption, before capturing.
    # Here rather than in an ad-hoc script on the side, because the retry logic
    # below is the whole reason this file exists: a one-off capture written
    # beside it silently returned a blank white image on the first try.
    [string[]]$Toggle = @()
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes

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

  // A hardware-accelerated WPF window sometimes hands PrintWindow a blank
  // redirection surface: the bitmap comes back the right size and entirely
  // one colour. That happened three times running to someone using this
  // script, who came within a step of reporting the window as empty.
  //
  // A blank frame is indistinguishable from a real one by size alone, so the
  // only defence is to look at the pixels. Sampling a sparse grid is enough --
  // any genuine screenful of a dark UI with text on it has more than one
  // colour in it, and sampling costs nothing next to launching the app.
  public static bool LooksBlank(Bitmap b) {
    if (b.Width < 64 || b.Height < 64) return true;

    // Inset well past the window chrome. A first version sampled to the very
    // edge, found the dark border pixels, concluded the image had more than
    // one colour, and passed a capture whose entire client area was white --
    // the exact failure it was written to catch. The title bar is excluded for
    // the same reason: it renders from a different surface and is almost
    // always present even when the content is not.
    int left = b.Width / 10, right = b.Width - b.Width / 10;
    int top = b.Height / 6, bottom = b.Height - b.Height / 10;

    Color first = b.GetPixel(left, top);
    for (int x = left; x < right; x += Math.Max(1, (right - left) / 24)) {
      for (int y = top; y < bottom; y += Math.Max(1, (bottom - top) / 24)) {
        if (b.GetPixel(x, y) != first) return false;
      }
    }
    return true;
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

    foreach ($caption in $Toggle) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $match = New-Object System.Windows.Automation.PropertyCondition (
            [System.Windows.Automation.AutomationElement]::NameProperty, $caption)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $match)
        if (-not $element) { throw "no control captioned '$caption' was found in the window" }

        # Through the automation pattern rather than a synthetic click, because that is the route a
        # keyboard or a screen reader takes. A control wired only to Click looks fine to a mouse and
        # does nothing here -- which is how one was found already.
        $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        "toggled '$caption'"
        Start-Sleep -Seconds 4
    }

    # Retry a blank frame rather than saving it. Saving one is worse than
    # failing: a picture is believed, and this one shows an empty application.
    $bitmap = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        if ($bitmap) { $bitmap.Dispose() }
        $bitmap = [UiCapture]::Shot($process.MainWindowHandle)
        if (-not [UiCapture]::LooksBlank($bitmap)) { break }
        "attempt ${attempt}: the window rendered blank, retrying"
        Start-Sleep -Seconds 2
    }

    if ([UiCapture]::LooksBlank($bitmap)) {
        $bitmap.Dispose()
        throw "the window captured blank five times running. This is a capture failure, not an empty application - do not read it as a finding about the UI."
    }

    $bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    "captured $($bitmap.Width) x $($bitmap.Height) physical pixels -> $Out"
    $bitmap.Dispose()

    $crash = Join-Path (Split-Path $Exe) "crash_log.txt"
    if (Test-Path $crash) { "crash_log.txt was written:"; Get-Content $crash -TotalCount 6 }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
