using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

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
        MessageBox.Show("HTML 연동 기본 스크립트가 클립보드에 복사되었습니다.\n\n원하는 HTML 파일의 <body> 끝부분에 붙여넣어 사용하세요.", "클립보드 복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show($"폴더 열기 실패: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"브라우저 실행 실패:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
}
