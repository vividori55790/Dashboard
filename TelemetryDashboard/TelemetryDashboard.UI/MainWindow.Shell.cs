using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.UI.Dialogs;
using TelemetryDashboard.UI.Docking;

namespace TelemetryDashboard.UI;

/// <summary>Shell chrome: recording, theme, language, palette, drag-and-drop and shortcuts.</summary>
public partial class MainWindow
{
    private void BtnCopyWsUrl_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText("ws://localhost:8080/ws");
        ControlPanel.LogMessage("SYSTEM", "WebSocket URL copied to clipboard.");
    }

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _themeService.ToggleTheme();
        ControlPanel.LogMessage("SYSTEM", "Theme toggled.");
    }

    private void BtnToggleRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_csvRecorder.IsRecording)
        {
            string path = _csvRecorder.StartRecording();
            BtnToggleRecord.Content = "⏹️ CSV 녹화 정지 (저장)";
            BtnToggleRecord.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x2E, 0x63));
            ControlPanel.LogMessage("DATA", $"[REC START] Writing real CSV disk file: {path}");
        }
        else
        {
            long count = _csvRecorder.RecordedPacketCount;
            long size = _csvRecorder.FileSizeBytes;
            string path = _csvRecorder.StopRecording();
            BtnToggleRecord.Content = "⏺️ 실제 CSV 디스크 녹화";
            BtnToggleRecord.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2C));
            ControlPanel.LogMessage("DATA", $"[REC STOP] Real file saved -> {path} ({count} rows, {size / 1024} KB)");

            var result = MessageBox.Show(this,
                $"CSV 텔레메트리 데이터가 실제 디스크 파일로 저장 완료되었습니다!\n\n" +
                $"• 저장 위치: {path}\n" +
                $"• 총 기록 행 수: {count:N0} 행\n" +
                $"• 파일 크기: {size / 1024:N0} KB\n\n" +
                $"저장된 로그 폴더를 지금 여시겠습니까?",
                "CSV 파일 영구 저장 완료", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                OpenLogsFolder(path);
            }
        }
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
        CHeaderExportDialog dlg = new CHeaderExportDialog { Owner = this };
        dlg.ShowDialog();
        ControlPanel.LogMessage("TOOLS", "Opened STM32 C Header & Driver Code Generator.");
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

    private void BtnToggleLanguage_Click(object sender, RoutedEventArgs e)
    {
        string nextLang = _languageService.CurrentCultureName.StartsWith("ko") ? "en-US" : "ko-KR";
        _languageService.SetLanguage(nextLang);
        ControlPanel.LogMessage("SYSTEM", $"Language switched to {nextLang}.");
    }

    private void BtnOpenCommandPalette_Click(object sender, RoutedEventArgs e)
    {
        _commandPaletteService.ToggleVisibility();
        CommandPalette.Visibility = _commandPaletteService.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new QuickStartGuideDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            _commandPaletteService.ToggleVisibility();
            CommandPalette.Visibility = _commandPaletteService.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }
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

        string port = _serialManager.ActivePorts.Keys.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrEmpty(port))
        {
            ControlPanel.LogMessage("WARN", $"'{command}' not sent: no active port.");
            return;
        }

        try
        {
            await _serialManager.WriteLineAsync(port, command);
            ControlPanel.LogMessage("TX", $"'{command}' transmitted on {port}.");
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"'{command}' failed on {port}: {ex.Message}");
        }
    }
}
