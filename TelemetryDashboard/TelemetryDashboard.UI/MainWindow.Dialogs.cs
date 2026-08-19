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

/// <summary>Analytics, diagnosis, DVR, alerting and security dialog launchers.</summary>
public partial class MainWindow
{
    private void BtnOpenMlAnalytics_Click(object sender, RoutedEventArgs e)
    {
        OpenMlAnalyticsModal();
    }

    private void OpenMlAnalyticsModal()
    {
        MlAnalyticsDialog dlg = new MlAnalyticsDialog(_mlEngine) { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenLlmDiagnosis_Click(object sender, RoutedEventArgs e)
    {
        // Feed the dialog the anomalies this session actually recorded.
        var dlg = new LlmDiagnosisDialog(
            onCommandSend: async (cmd) =>
            {
                ControlPanel.LogMessage("EMERGENCY", $"MCU safety command requested: {cmd}");
                await TransmitCommandAsync(cmd);
            },
            recentAnomalies: () => _mlEngine.RecentAnomalies) { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenTimeTravelDvr_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TimeTravelDvrDialog(_streamingServer.DvrPlayer) { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenAlertForwarder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AlertForwarderDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnAdaptiveSamplingSettings_Click(object sender, RoutedEventArgs e)
    {
        // Bind the dialog to the controller the app is running, not a throwaway instance.
        var dlg = new AdaptiveSamplingDialog(_samplingController) { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnSecuritySettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ZeroTrustSecurityDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenProtocolBridge_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProtocolBridgeDialog { Owner = this };
        dlg.ShowDialog();
    }
}
