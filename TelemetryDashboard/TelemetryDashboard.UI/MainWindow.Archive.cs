using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.UI;

/// <summary>Durable telemetry archive, and the MATLAB export taken from it.</summary>
/// <remarks>
/// The shell recorded to CSV and nowhere else, so anything an operator wanted to analyse later had
/// to be re-parsed from text and nothing could be queried by node, channel or time window. Samples
/// now also go through the bounded ring, a drain moves them into SQLite, and that archive is what
/// the export reads — so an export covers the whole recording, not just what is still on screen.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Packets the ring holds while waiting for the drain. Roughly 25 s at 120 packets/s.</summary>
    private const int ArchiveRingCapacity = 3_000;

    private ChannelDataLogger? _archiveRing;
    private SqliteDataLogger? _archive;
    private ChannelDataLoggerDrain? _archiveDrain;
    private bool _archiveFailed;

    /// <summary>Folder holding both the CSV recordings and the archive database.</summary>
    private static string LogsDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    /// <summary>Queues one tick of simulator output into the durable archive.</summary>
    /// <remarks>
    /// Measurements only. A score persisted beside the value it came from is a second copy that can
    /// disagree with the engine after any change to the detector.
    /// </remarks>
    private void ArchiveSimulationSamples(PowerPlantState state)
    {
        EnsureArchiveStarted();
        if (_archiveRing is null) return;

        DateTime now = DateTime.UtcNow;
        Enqueue("COM3", "Temperature", state.AmbientTemperature, "C", now);
        Enqueue("COM3", "Humidity", state.AmbientHumidity, "%", now);
        Enqueue("COM3", "Vibration", state.Vibration, "g", now);
        Enqueue("COM3", "RPM", state.Rpm, "rpm", now);
        Enqueue("DAB_CONVERTER", "BatteryCurrent", state.DabBatteryCurrent, "A", now);
        Enqueue("PSFB_CONVERTER", "ServerVoltage", state.PsfbOutputVoltage, "V", now);
    }

    private void Enqueue(string nodeId, string variable, double value, string unit, DateTime timestamp) =>
        _archiveRing?.TryEnqueue(new TelemetryPacket(nodeId, variable, value, unit, timestamp));

    /// <summary>Opens the archive on first use. A failure disables archiving without killing the tick.</summary>
    private void EnsureArchiveStarted()
    {
        if (_archiveDrain is not null || _archiveFailed) return;

        try
        {
            Directory.CreateDirectory(LogsDirectory);
            _archive = new SqliteDataLogger(Path.Combine(LogsDirectory, "telemetry_archive.db"));
            _archiveRing = new ChannelDataLogger(ArchiveRingCapacity);
            _archiveDrain = new ChannelDataLoggerDrain(_archiveRing, _archive);
            _archiveDrain.Start();
            ControlPanel.LogMessage("DATA", $"[ARCHIVE] Durable store open: {_archive.DatabasePath}");
        }
        catch (Exception ex)
        {
            // Retrying every 50 ms would flood the log with the same unwritable-path error.
            _archiveFailed = true;
            ControlPanel.LogMessage("ERROR", $"[ARCHIVE] Durable store unavailable: {ex.Message}");
        }
    }

    /// <summary>Exports the archived recording to a MATLAB MAT-file.</summary>
    /// <remarks>
    /// The drain is stopped before the query and restarted after it. Stopping flushes the packets
    /// still in the ring, so the export covers the recording up to the moment the operator asked
    /// for it rather than up to the last batch that happened to have been committed.
    /// </remarks>
    private async void BtnExportMatlab_Click(object sender, RoutedEventArgs e)
    {
        if (_archive is null)
        {
            MessageBox.Show(this,
                "아직 저장된 텔레메트리가 없습니다. 먼저 CSV 녹화를 시작해 아카이브를 채워주세요.",
                "MATLAB 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_archiveDrain is not null) await _archiveDrain.StopAsync();

            string target = Path.Combine(LogsDirectory, $"telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.mat");
            int exported = await new MatlabArchiveExporter(_archive)
                .ExportAsync(target, new QueryFilter(Limit: int.MaxValue));

            _archiveDrain?.Start();

            ControlPanel.LogMessage("DATA", $"[EXPORT] {exported:N0} packets -> {target}");
            ReportExport(exported, target);
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"[EXPORT] MATLAB export failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "MATLAB 내보내기 실패",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Reports the outcome, distinguishing an empty archive from a written file.</summary>
    private void ReportExport(int exported, string target)
    {
        if (exported == 0)
        {
            MessageBox.Show(this, "아카이브에서 내보낼 패킷을 찾지 못했습니다. 파일은 생성되지 않았습니다.",
                "MATLAB 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show(this,
            $"MATLAB / Octave / SciPy 에서 바로 열 수 있는 .mat 파일을 저장했습니다.\n\n"
            + $"• 저장 위치: {target}\n• 내보낸 패킷: {exported:N0} 개\n\n폴더를 여시겠습니까?",
            "MATLAB 내보내기 완료", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes) OpenLogsFolder(target);
    }

    /// <summary>Flushes and closes the archive during shutdown.</summary>
    /// <remarks>
    /// Blocking is deliberate: the drain holds the tail of the recording, and a window that closed
    /// while the last batch was in flight would lose it. Every await inside the drain is configured
    /// off the dispatcher, so waiting on it from the UI thread cannot deadlock.
    /// </remarks>
    private void ShutdownArchive()
    {
        try { _archiveDrain?.StopAsync().GetAwaiter().GetResult(); }
        catch (Exception) { /* Shutdown must not be blocked by a failing final flush. */ }

        _archive?.Dispose();
        _archiveDrain = null;
        _archive = null;
    }
}
