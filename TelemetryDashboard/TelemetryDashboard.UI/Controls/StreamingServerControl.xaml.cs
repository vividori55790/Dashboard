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

    /// <summary>Glyph shown while the server is running; pressing the button stops it.</summary>
    private const string StopGlyph = "\uE71A";

    /// <summary>Glyph shown while the server is stopped; pressing the button starts it.</summary>
    private const string PlayGlyph = "\uE768";

    public void AttachServer(TelemetryStreamingServer server, string htmlClientPath)
    {
        _server = server;
        _htmlClientPath = htmlClientPath;
        TxtHtmlPath.Text = htmlClientPath;

        // Addresses come from the server's own port. They used to be written into the markup as
        // localhost:8080, so a server constructed on any other port advertised an address nothing
        // was listening on — and the copy button handed that address to the operator.
        TxtServerEndpoint.Text = $"http://localhost:{server.Port}  ·  ws://localhost:{server.Port}/ws";
        TxtWsUrl.Text = WebSocketUrl;
        TxtHttpUrl.Text = $"http://localhost:{server.Port}/api/status";

        UpdateServerUI();
    }

    /// <summary>The subscribe address for the attached server, or empty when none is attached.</summary>
    private string WebSocketUrl => _server is null ? string.Empty : $"ws://localhost:{_server.Port}/ws";

    public void UpdateMetrics()
    {
        if (_server == null) return;
        Dispatcher.Invoke(() =>
        {
            // Units belong to the caption beside each figure, so the figure stays a figure. The
            // broadcast readout also used to append a constant "(60 Hz)" that nothing measured.
            TxtClientCount.Text = $"{_server.ConnectedClientCount:N0}";
            TxtBroadcastStats.Text = $"{_server.TotalPacketsBroadcasted:N0}";

            // Stopped is a state, not a fault, so it reads as quiet rather than as an alarm.
            SetLed(_server.IsRunning ? "SuccessBrush" : "TextTertiaryBrush");
            ServerToggleGlyph.Text = _server.IsRunning ? StopGlyph : PlayGlyph;
            ServerToggleLabel.Text = _server.IsRunning ? "서버 정지" : "서버 시작";
        });
    }

    /// <summary>Colours the running indicator from a theme token.</summary>
    private void SetLed(string brushKey)
    {
        if (TryFindResource(brushKey) is Brush brush)
        {
            ServerLed.Fill = brush;
        }
    }

    private void UpdateServerUI()
    {
        UpdateMetrics();
    }

    private void BtnOpenWebConsole_Click(object sender, RoutedEventArgs e)
    {
        if (_server is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://localhost:{_server.Port}/",
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
        string url = WebSocketUrl;
        if (url.Length == 0) return;

        Clipboard.SetText(url);
        MessageBox.Show($"웹소켓 URL이 클립보드에 복사되었습니다:\n{url}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
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
