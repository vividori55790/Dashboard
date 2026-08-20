using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TelemetryDashboard.UI.Dialogs;

public partial class CustomWebPageDialog : Window
{
    public CustomWebPageDialog()
    {
        InitializeComponent();
    }

    private void BtnCopySdk_Click(object sender, RoutedEventArgs e)
    {
        string snippet = "<!-- 1. SDK 스크립트 추가 -->\n" +
                         "<script src=\"http://localhost:8080/telemetry-client.js\"></script>\n" +
                         "<script>\n" +
                         "  TelemetryClient.connect('ws://localhost:8080/ws');\n" +
                         "  TelemetryClient.onData(data => {\n" +
                         "    document.getElementById('temp').textContent = data.temp;\n" +
                         "  });\n" +
                         "</script>";
        Clipboard.SetText(snippet);
        MessageBox.Show(this,
            "연동 스크립트를 클립보드에 복사했습니다.\n\nHTML 파일의 <body> 끝부분에 붙여넣으세요.",
            "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnOpenTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            // Traverse up if running in bin/Release/
            string candidate = Path.Combine(rootDir, "..", "..", "..", "..");
            if (File.Exists(Path.Combine(candidate, "starter_minimal.html")))
            {
                rootDir = Path.GetFullPath(candidate);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{rootDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"폴더를 열지 못했습니다: {ex.Message}",
                "폴더 열기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchBrowserUrl(string relativeUrl)
    {
        try
        {
            string url = $"http://localhost:8080/{relativeUrl.TrimStart('/')}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // The launch can also "succeed" into a browser error page when the built-in web server
            // is not running; the caption on the dialog says so rather than this handler claiming it.
            MessageBox.Show(this, $"브라우저를 열지 못했습니다:\n{ex.Message}",
                "실행 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLaunchPowerUpsPsfb_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("power_ups_psfb_dashboard.html");
    }

    private void BtnLaunchMinimal_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("starter_minimal.html");
    }

    private void BtnLaunchChartGauge_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("starter_chart_gauge.html");
    }

    private void BtnLaunchGrid_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("starter_grid_dashboard.html");
    }

    private void BtnLaunchStreamClient_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("stream_client.html");
    }

    private void BtnLaunchDashboard_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowserUrl("custom_dashboard.html");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>Escape closes the dialog, as in every other dialog here.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
