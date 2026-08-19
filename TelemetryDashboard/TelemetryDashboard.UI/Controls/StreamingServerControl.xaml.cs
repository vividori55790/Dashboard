using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.UI.Controls;

public partial class StreamingServerControl : UserControl
{
    private TelemetryStreamingServer? _server;
    private string _htmlClientPath = string.Empty;

    public StreamingServerControl()
    {
        InitializeComponent();
    }

    public void AttachServer(TelemetryStreamingServer server, string htmlClientPath)
    {
        _server = server;
        _htmlClientPath = htmlClientPath;
        TxtHtmlPath.Text = htmlClientPath;
        UpdateServerUI();
    }

    public void UpdateMetrics()
    {
        if (_server == null) return;
        Dispatcher.Invoke(() =>
        {
            TxtClientCount.Text = $"{_server.ConnectedClientCount} Client(s) Connected";
            TxtBroadcastStats.Text = $"Total Broadcasted: {_server.TotalPacketsBroadcasted:N0} pkts (60 Hz)";
            ServerLed.Fill = _server.IsRunning ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(255, 85, 85));
            BtnToggleServer.Content = _server.IsRunning ? "⏹️ 서버 정지" : "▶️ 서버 시작";
        });
    }

    private void UpdateServerUI()
    {
        UpdateMetrics();
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"웹 브라우저 실행 중 오류가 발생했습니다:\n{ex.Message}", "Browser Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;

        if (_server.IsRunning)
        {
            _server.Stop();
        }
        else
        {
            _server.Start(_htmlClientPath);
        }
        UpdateServerUI();
    }

    private void BtnCopyWsUrl_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText("ws://localhost:8080/ws");
        MessageBox.Show("웹소켓 URL이 클립보드에 복사되었습니다:\nws://localhost:8080/ws", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnOpenHtmlLocation_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_htmlClientPath))
        {
            Process.Start("explorer.exe", $"/select,\"{_htmlClientPath}\"");
        }
        else
        {
            MessageBox.Show($"파일을 찾을 수 없습니다:\n{_htmlClientPath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
