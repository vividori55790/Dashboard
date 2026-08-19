using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>Incident report generation and export for the time-travel DVR dialog.</summary>
/// <remarks>
/// Separated from the transport controls so the report path — the one that produces a file an
/// operator keeps, mails and quotes — can be read on its own.
/// </remarks>
public partial class TimeTravelDvrDialog
{
    /// <summary>Title placed on every generated report.</summary>
    /// <remarks>
    /// It names the window that was summarised and claims nothing about it. The title was fixed at
    /// "UPS &amp; DC-DC Converter Power Stage Anomaly" for every report the dialog produced,
    /// including windows containing no anomaly at all, on whatever hardware happened to be attached.
    /// </remarks>
    private const string ReportTitle = "DVR 스냅샷 리뷰 (DVR Snapshot Review)";

    /// <summary>
    /// Regenerates the report covering the last <see cref="ReportWindowSec"/> seconds of timeline.
    /// </summary>
    /// <remarks>
    /// No diagnosis text is supplied. The dialog used to pass a fixed sentence prefixed
    /// "AI Diagnosis:" — thermal runaway accompanied by a vibration spike — whatever the buffer
    /// actually held, so every exported report attributed a specific conclusion to an analysis that
    /// had never run. The generator's own neutral summary stands in until a real diagnosis exists.
    /// </remarks>
    private void GenerateIncidentReport()
    {
        double now = DateTime.UtcNow.Ticks / 10_000_000.0;
        List<DvrFrame> snapshot = _dvrPlayer.ExtractSnapshot(now, ReportWindowSec);
        TxtIncidentReport.Text = _reportGen.GenerateMarkdownReport(ReportTitle, snapshot, string.Empty);
    }

    private void BtnRefreshReport_Click(object sender, RoutedEventArgs e)
    {
        GenerateIncidentReport();
    }

    private void BtnCopyReport_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtIncidentReport.Text);
        MessageBox.Show(this, "Incident Report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Markdown File (*.md)|*.md|All Files (*.*)|*.*",
            FileName = $"Incident_Report_{DateTime.Now:yyyyMMdd_HHmmss}.md"
        };
        if (sfd.ShowDialog() == true)
        {
            File.WriteAllText(sfd.FileName, TxtIncidentReport.Text, Encoding.UTF8);
            MessageBox.Show(this, $"Report saved successfully to:\n{sfd.FileName}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
