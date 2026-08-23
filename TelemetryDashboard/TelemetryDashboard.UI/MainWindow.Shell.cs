using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.UI.Dialogs;
using TelemetryDashboard.UI.Docking;

using TelemetryDashboard.UI.Themes;

namespace TelemetryDashboard.UI;

/// <summary>Shell chrome: recording, theme, language, palette, drag-and-drop and shortcuts.</summary>
public partial class MainWindow
{
    /// <summary>Puts the dock's tab strips on the application's own tab template.</summary>
    /// <remarks>
    /// This cannot be done from XAML, and the reason is worth writing down.
    ///
    /// AvalonDock's pane controls derive from TabControl, so their tabs are ordinary TabItems — but
    /// each pane hands its containers a style of its own, and an explicit container style beats the
    /// dictionary's implicit TabItem style, so the dock never saw the design system at all. Its tab
    /// template takes the unselected tab's fill straight from the stock Windows theme (a pale grey
    /// gradient) and, worse, hardcodes <c>#FFFFFFFF</c> for the selected tab inside a template
    /// trigger. A Setter cannot outrank a template trigger, so no amount of styling from outside
    /// reaches it: the template itself has to be replaced. That is why the second document rendered
    /// as a pale empty box beside a white selected tab, with both captions drawn in a foreground
    /// meant for a dark surface.
    ///
    /// Replacing the pane's whole style is not the answer either — that was tried, and it discards
    /// the pane template and the tab's header template with it, leaving a stock TabControl showing
    /// "AvalonDock.Layout.LayoutDocument" where the captions belong. So the container style is
    /// derived from AvalonDock's rather than substituted for it: everything the dock needs is
    /// inherited, and the visual setters — including the template — are copied from the very same
    /// implicit TabItem style the ribbon uses, so there is one tab appearance in the application
    /// and no second copy of it to keep in step.
    /// </remarks>
    private bool _workspaceRestored;

    private void DockManager_Loaded(object sender, RoutedEventArgs e)
    {
        // Before the tab styling below, and only once: Loaded fires again whenever the element is
        // re-added to the tree, and deserialising a second time would throw away an arrangement
        // the operator has since changed.
        if (!_workspaceRestored)
        {
            _workspaceRestored = true;
            RestoreWorkspace();
        }

        // The manager is loaded before it has built its pane controls, so this first pass normally
        // finds nothing to style. LayoutUpdated is the hook that fires once they exist; it detaches
        // itself as soon as every pane in the tree has been dealt with, so it costs one tree walk
        // rather than one per layout pass.
        DockManager.LayoutUpdated += DockManager_LayoutUpdated;
        ApplyDockTabTheme();
    }

    private void DockManager_LayoutUpdated(object? sender, EventArgs e)
    {
        if (ApplyDockTabTheme())
        {
            DockManager.LayoutUpdated -= DockManager_LayoutUpdated;
        }
    }

    /// <summary>Styles already derived here, so a second pass does not re-wrap its own work.</summary>
    private readonly HashSet<Style> _themedDockTabStyles = new();

    /// <summary>Returns true once every pane in the tree has been dealt with.</summary>
    private bool ApplyDockTabTheme()
    {
        if (TryFindResource(typeof(TabItem)) is not Style appTabStyle) return true;

        bool foundAPane = false;
        bool allStyled = true;

        foreach (TabControl pane in DockTabControls(DockManager))
        {
            foundAPane = true;

            // The pane's own frame, for the same reason as the tabs: its template binds these two
            // to the control, and the style behind it hands them a white border, which drew a
            // bright 1px rectangle around each pane.
            pane.SetResourceReference(BackgroundProperty, "CanvasBrush");
            pane.SetResourceReference(BorderBrushProperty, "BorderSubtleBrush");

            if (pane.ItemContainerStyle is not Style dockStyle)
            {
                allStyled = false;
                continue;
            }

            if (_themedDockTabStyles.Contains(dockStyle)) continue;

            try
            {
                var themed = new Style(typeof(TabItem), dockStyle);

                // Base first, derived last: within one style the last setter for a property wins,
                // so walking the BasedOn chain outward-in leaves the most specific setter on top.
                foreach (SetterBase setter in AppTabSetters(appTabStyle))
                {
                    themed.Setters.Add(setter);
                }

                // The tab's accessible name. AvalonDock puts the LayoutDocument itself in Header,
                // and the automation peer falls back to ToString() on it -- so every document tab
                // reported as "AvalonDock.Layout.LayoutDocument" and a screen reader could not tell
                // two open documents apart. The caption renders correctly from the header template,
                // which is why it goes unnoticed by anyone reading the screen with their eyes.
                themed.Setters.Add(new Setter(
                    System.Windows.Automation.AutomationProperties.NameProperty,
                    new System.Windows.Data.Binding("Title")));

                themed.Seal();
                _themedDockTabStyles.Add(themed);
                pane.ItemContainerStyle = themed;
            }
            catch (Exception ex)
            {
                // Cosmetic only. A dock that keeps its own tab chrome is worse-looking, not broken,
                // and is not worth taking the window down for — but it is said out loud rather than
                // swallowed, because a silently unthemed dock looks like the styling was never
                // written.
                ControlPanel.LogMessage("SYSTEM",
                    $"Dock tab styling skipped on {pane.GetType().Name}: {ex.GetType().Name}");
                return true;
            }
        }

        return foundAPane && allStyled;
    }

    private static IEnumerable<SetterBase> AppTabSetters(Style style)
    {
        if (style.BasedOn is not null)
        {
            foreach (SetterBase inherited in AppTabSetters(style.BasedOn)) yield return inherited;
        }

        foreach (SetterBase own in style.Setters) yield return own;
    }

    /// <summary>The document and anchorable pane controls, which are the dock's only TabControls.</summary>
    private static IEnumerable<TabControl> DockTabControls(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TabControl pane) yield return pane;

            foreach (TabControl nested in DockTabControls(child)) yield return nested;
        }
    }

    private void BtnCopyWsUrl_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText("ws://localhost:8080/ws");
        ControlPanel.LogMessage("SYSTEM", "WebSocket URL copied to clipboard.");
    }

    /// <summary>
    /// Switches the palette, and says what actually changed rather than that a button was pressed.
    /// </summary>
    /// <remarks>
    /// The log line used to read "Theme toggled." unconditionally, while the service it called
    /// dropped the argument, swallowed the exception it raised, and addressed a library no XAML in
    /// this application uses. A message written whether or not the work happened is worse than no
    /// message: it is the evidence someone checks.
    /// </remarks>
    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e) => _themeService.ToggleTheme();

    /// <summary>
    /// Says what the window is now painted with, having gone and looked.
    /// </summary>
    /// <remarks>
    /// Counted off the realised visual tree rather than off the service's own opinion of its work,
    /// because the two previous versions of this feature both had a service that believed it had
    /// succeeded. What an operator wants to know is whether the screen in front of them changed,
    /// and that is a question only the screen can answer.
    /// </remarks>
    private void ReportThemeToLog()
    {
        ThemeProbeResult probe = ThemeProbe.Sample(
            this,
            ThemePalette.For(_themeService.EffectiveIsLight),
            ThemePalette.For(!_themeService.EffectiveIsLight));

        string chosen = _themeService.FollowsSystem
            ? $"Following the Windows theme, which is {(_themeService.EffectiveIsLight ? "light" : "dark")}"
            : $"{_themeService.CurrentTheme} theme applied";

        string detail = $"{chosen}. {_themeService.ChangedColours} colours changed. {probe.Describe()}";

        if (_themeService.UnknownKeys.Count > 0)
        {
            detail += " Not found in the resource dictionary: "
                    + string.Join(", ", _themeService.UnknownKeys) + ".";
        }

        ControlPanel.LogMessage("SYSTEM", detail);
    }

    /// <summary>Starts or stops the CSV recording, and says which of those actually happened.</summary>
    /// <remarks>
    /// Recording captures the readings that arrive through ingest, so with no source running there
    /// is nothing to capture. That used to be untrue in one direction only: the display tick wrote
    /// six fixed rows of the bundled demo whether or not anything was streaming, so a recording
    /// always filled up and always described the same converter.
    /// </remarks>
    private void BtnToggleRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_csvRecorder.IsRecording)
        {
            string path = _csvRecorder.StartRecording();
            ShowRecording(true);
            ControlPanel.LogMessage("DATA", $"[REC START] Writing real CSV disk file: {path}");

            // Said at the moment it can still be acted on. Finding out at the end that an hour of
            // recording holds nothing is the same information delivered uselessly late.
            if (!_isSimulating && !_isConnected)
            {
                ControlPanel.LogMessage("WARN",
                    "수신 중인 소스가 없어 녹화 파일에 기록될 데이터가 없습니다. " +
                    "하드웨어를 연결하거나 가상 MCU 스트림을 시작하세요.");
            }

            return;
        }

        long count = _csvRecorder.RecordedPacketCount;
        long size = _csvRecorder.FileSizeBytes;
        string saved = _csvRecorder.StopRecording();
        ShowRecording(false);
        ControlPanel.LogMessage("DATA", $"[REC STOP] Real file saved -> {saved} ({count} rows, {size / 1024} KB)");
        ReportRecordingResult(saved, count, size);
    }

    /// <summary>Reports a finished recording, distinguishing a full file from an empty one.</summary>
    /// <remarks>
    /// An empty recording used to be announced with the same congratulatory dialog as a full one —
    /// "저장 완료되었습니다!" over a row count of zero. The file is real either way, so the fact
    /// worth telling the operator is whether anything is in it.
    /// </remarks>
    private void ReportRecordingResult(string path, long count, long size)
    {
        if (count == 0)
        {
            MessageBox.Show(this,
                $"녹화를 중지했습니다. 기록된 행이 없어 파일에는 헤더만 있습니다.\n\n"
                + $"• 저장 위치: {path}\n\n"
                + "녹화 중에 수신된 텔레메트리가 없었습니다. 하드웨어 연결 또는 "
                + "가상 MCU 스트림을 시작한 뒤 다시 녹화하세요.",
                "녹화 종료 — 데이터 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show(this,
            $"CSV 텔레메트리 데이터를 디스크에 저장했습니다.\n\n"
            + $"• 저장 위치: {path}\n"
            + $"• 총 기록 행 수: {count:N0} 행\n"
            + $"• 파일 크기: {size / 1024:N0} KB\n\n"
            + "저장된 로그 폴더를 지금 여시겠습니까?",
            "CSV 저장 완료", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes) OpenLogsFolder(path);
    }

    private void BtnOpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
        OpenLogsFolder(logsDir);
    }

    private void OpenLogsFolder(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = targetPath, UseShellExecute = true, Verb = "open" });
            }
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"Failed to open folder: {ex.Message}");
        }
    }

    private void BtnGenerateCHeader_Click(object sender, RoutedEventArgs e)
    {
        SensorNodeConfig? config = FirmwareConfig();
        CHeaderExportDialog dlg = new CHeaderExportDialog(config) { Owner = this };
        dlg.ShowDialog();

        ControlPanel.LogMessage("TOOLS", config is null
            ? "Opened the firmware generator with no profile loaded, so it shows the worked example."
            : $"Generated firmware for {config.NodeId}: {config.Variables.Count} channel(s) at {config.BaudRate} baud.");
    }

    private void BtnFormulaCalc_Click(object sender, RoutedEventArgs e)
    {
        FormulaEvaluatorDialog dlg = new FormulaEvaluatorDialog { Owner = this };
        dlg.ShowDialog();
        ControlPanel.LogMessage("TOOLS", "Opened Dynamic Signal Formula Evaluator.");
    }

    private void BtnLockScreen_Click(object sender, RoutedEventArgs e)
    {
        _passwordLockService.Lock();
        LockOverlay.Visibility = Visibility.Visible;
        ControlPanel.LogMessage("SECURITY", "Application screen locked.");
    }

    /// <summary>Switches between the two shipped languages.</summary>
    /// <remarks>
    /// The message says how far the switch reaches, because it does not reach everywhere yet: the
    /// ribbon and the command palette follow, and the dialogs and panels behind them are still
    /// written in Korean literals. Reporting "Language switched" alone is what this button did
    /// before it changed anything at all.
    /// </remarks>
    private void BtnToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        string nextLang = _languageService.CurrentCultureName.StartsWith("ko") ? "en-US" : "ko-KR";
        _languageService.SetLanguage(nextLang);

        ControlPanel.LogMessage("SYSTEM",
            $"Language switched to {nextLang}: the ribbon and the command palette. "
            + "Dialogs and panels are not translated yet.");
    }

    private void BtnOpenCommandPalette_Click(object sender, RoutedEventArgs e) => TogglePalette();

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new QuickStartGuideDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            TogglePalette();
            e.Handled = true;
        }
    }

    /// <summary>
    /// One path to the palette, from the hotkey and from the settings button alike.
    /// </summary>
    /// <remarks>
    /// There were two, and both did the same three lines by hand: toggle a flag on the service,
    /// then set Visibility from it. The overlay's own Escape and Enter handlers hid the control
    /// without telling the service, so the flag stayed true and the next press toggled it to false
    /// -- the palette needed two presses to reopen after any use of it.
    /// <para>
    /// Refused while the screen is locked. The palette can start the simulator and open dialogs,
    /// and a lock that leaves a command launcher reachable is not locking the thing that matters.
    /// </para>
    /// </remarks>
    private void TogglePalette()
    {
        if (LockOverlay.Visibility == Visibility.Visible) return;
        CommandPalette.Toggle();
    }

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files.Any(f => _dragDropHandler.CanAcceptFile(f)) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            string? primaryFile = _dragDropHandler.SelectPrimaryFile(files);
            if (primaryFile != null)
            {
                _dragDropHandler.ProcessDroppedFile(primaryFile);
                ControlPanel.LogMessage("FILE", $"Loaded file: {primaryFile}");
            }
        }
    }

    /// <summary>
    /// Sends an operator command to the connected hardware, reporting the true outcome.
    /// </summary>
    private async Task TransmitCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        if (!_isConnected)
        {
            ControlPanel.LogMessage("WARN", $"'{command}' not sent: no hardware port is connected.");
            return;
        }

        string port = Serial.ActivePorts.Keys.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrEmpty(port))
        {
            ControlPanel.LogMessage("WARN", $"'{command}' not sent: no active port.");
            return;
        }

        try
        {
            await Serial.WriteLineAsync(port, command);
            ControlPanel.LogMessage("TX", $"'{command}' transmitted on {port}.");
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"'{command}' failed on {port}: {ex.Message}");
        }
    }
}
