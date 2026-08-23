using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TelemetryDashboard.UI;

/// <summary>
/// Window chrome: the title bar, the window title, and the indicators that report state.
/// </summary>
/// <remarks>
/// Everything here exists because the shell was announcing things it had no business announcing.
/// The title was a sentence of marketing — "Enterprise Ingestion, AI Diagnosis, DVR &amp; Streaming
/// Hub" — sitting in light Windows chrome above a dark application, and the status bar claimed a
/// circuit-breaker state nobody had asked the breaker for. A title bar says what the product is and
/// whether it is connected; a status bar says what the machine reported.
/// </remarks>
public partial class MainWindow
{
    private const string ProductName = "Telemetry Dashboard";

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1 and later.</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>The same attribute's number on the 1809-1903 builds that introduced it.</summary>
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    /// <summary>
    /// Asks the desktop window manager for a dark title bar, and shrugs if it cannot have one.
    /// </summary>
    /// <remarks>
    /// The window frame is drawn by Windows, not by WPF, so no amount of styling inside the
    /// application reaches it; this call is the only way. It is also purely cosmetic, which is why
    /// every failure path here is silent: an older Windows, a stubbed dwmapi, a machine where the
    /// attribute number means something else — none of that is worth refusing to start over.
    /// </remarks>
    private void ApplyDarkTitleBar()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            int enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // No dwmapi.dll: the window keeps the system's own frame.
        }
        catch (EntryPointNotFoundException)
        {
            // Present but without this export. Same outcome, same lack of consequence.
        }
    }

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Reports a live hardware connection everywhere the shell mentions one.</summary>
    private void ShowConnected(string portName, int baudRate)
    {
        Title = $"{ProductName} — {portName} 연결됨";

        StatusConnectionText.Text = "연결됨";
        StatusConnectionText.SetResourceReference(ForegroundProperty, "SuccessBrush");
        StatusPortText.Text = $"{portName} · {baudRate} baud";

        ConnectButtonLabel.Text = "연결 해제";
        BtnToggleConnect.Style = (Style)FindResource("DangerButton");
    }

    /// <summary>Reports that nothing is connected. The title carries no state when there is none.</summary>
    private void ShowDisconnected()
    {
        Title = ProductName;

        StatusConnectionText.Text = "연결 안 됨";
        StatusConnectionText.SetResourceReference(ForegroundProperty, "DangerBrush");
        StatusPortText.Text = "포트 없음";

        ConnectButtonLabel.Text = "연결";
        BtnToggleConnect.Style = (Style)FindResource("PrimaryButton");
    }

    /// <summary>
    /// Reports the recorder's state on the button that controls it and in the status bar.
    /// </summary>
    /// <remarks>
    /// The recording state is carried by the button's style rather than by a background colour
    /// assigned in code, so "recording" looks the same as every other destructive-or-irreversible
    /// command in the application instead of being one more literal red somebody chose by hand.
    /// </remarks>
    private void ShowRecording(bool recording)
    {
        // A resource reference rather than a string: assigning the caption directly would freeze
        // this button in whichever language was active when the recording started.
        BtnToggleRecord.SetResourceReference(
            System.Windows.Controls.ContentControl.ContentProperty,
            recording ? "Ui_Cmd_ToggleRecord_Stop" : "Ui_Cmd_ToggleRecord");
        BtnToggleRecord.Style = (Style)FindResource(recording ? "RibbonDangerCommand" : "RibbonCommand");

        if (!recording) StatusRecordingText.Text = "녹화 중지";
    }
}
