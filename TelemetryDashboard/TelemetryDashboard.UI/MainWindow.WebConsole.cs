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

/// <summary>Web console launchers: no-code builder, hosted dashboards and custom templates.</summary>
public partial class MainWindow
{
    private void BtnOpenNoCodeBuilder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "custom_dashboard.html");
            string p2 = Path.Combine(baseDir, "..", "..", "..", "..", "..", "custom_dashboard.html");
            string path = File.Exists(p1) ? p1 : (File.Exists(p2) ? p2 : p1);

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                UseShellExecute = true
            });
            ControlPanel.LogMessage("WEB", "Opened No-Code Web Dashboard Builder in browser.");
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"Failed to open No-Code builder: {ex.Message}");
        }
    }

    /// <summary>Opens the bundled power-converter example page.</summary>
    /// <remarks>
    /// One of several example pages shipped with the application, and named as such. The log line
    /// used to describe it as though it were the product's console rather than a sample dashboard
    /// for one worked example.
    /// </remarks>
    private void BtnOpenPowerUpsPsfbConsole_Click(object sender, RoutedEventArgs e)
    {
        const string page = "dab_psfb_console.html";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://localhost:8080/{page}",
                UseShellExecute = true
            });
            ControlPanel.LogMessage("WEB", $"Opened bundled example dashboard: {page}");
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"Failed to open browser: {ex.Message}");
        }
    }

    private void BtnOpenCustomWebTemplates_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomWebPageDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenWebConsole_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:8080/",
                UseShellExecute = true
            });
            ControlPanel.LogMessage("WEB", "Opened Web Client Console (http://localhost:8080) in browser.");
        }
        catch (Exception ex)
        {
            ControlPanel.LogMessage("ERROR", $"Failed to open browser: {ex.Message}");
        }
    }

    private void BtnOpenMeshCluster_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new MeshClusterDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenOtaFlasher_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OtaFlasherDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void BtnOpenPluginSandbox_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PluginSandboxDialog { Owner = this };
        dlg.ShowDialog();
    }
}
