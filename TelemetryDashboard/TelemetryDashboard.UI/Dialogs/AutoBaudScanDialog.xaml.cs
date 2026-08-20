using System;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Opens each serial port the system reports and asks <see cref="AutoBaudScanner"/> which baud rate,
/// if any, produces frames in a known format.
/// </summary>
/// <remarks>
/// This dialog previously ran no scan at all. It listed the port names, invented two virtual ports
/// when the machine had none, wrote "Baud: 115200 | Status: Active Telemetry Framing OK" against
/// every one of them, and animated a progress bar that started at 65%. The window reported a
/// successful hardware probe on a machine with nothing plugged in.
/// </remarks>
public partial class AutoBaudScanDialog : Window
{
    /// <summary>One line in the results list; a failed probe is a row too, and says so.</summary>
    private sealed class ScanRow
    {
        public string Text { get; init; } = string.Empty;
        public string Port { get; init; } = string.Empty;
        public int BaudRate { get; init; }
        public bool IsUsable { get; init; }
        public override string ToString() => Text;
    }

    private readonly AutoBaudScanner _scanner = new(new MultiPortSerialManager(new Win32HotPlugHook()));

    /// <summary>Port the operator chose from the results; empty until a probe succeeds.</summary>
    public string DiscoveredPort { get; private set; } = string.Empty;

    /// <summary>Baud rate the scanner confirmed for <see cref="DiscoveredPort"/>; 0 when none.</summary>
    public int DiscoveredBaudRate { get; private set; }

    public AutoBaudScanDialog()
    {
        InitializeComponent();
        _ = RunScannerAsync();
    }

    private async Task RunScannerAsync()
    {
        BtnRescan.IsEnabled = false;
        BtnApply.IsEnabled = false;
        LstDiscoveredPorts.Items.Clear();
        DiscoveredPort = string.Empty;
        DiscoveredBaudRate = 0;

        Report(0, "직렬 포트 목록을 읽는 중...");
        string[] ports = SerialPort.GetPortNames();

        if (ports.Length == 0)
        {
            // No invented fallback ports. An empty list is a finding, not a gap to fill.
            Report(100, "시스템이 보고한 직렬 포트가 없습니다.");
            LstDiscoveredPorts.Items.Add(new ScanRow { Text = "사용 가능한 포트 없음" });
            BtnRescan.IsEnabled = true;
            return;
        }

        bool anyUsable = false;

        for (int i = 0; i < ports.Length; i++)
        {
            string port = ports[i];
            Report(i * 100.0 / ports.Length, $"{port} 검색 중 ({i + 1}/{ports.Length})");

            ScanResult result = await _scanner.ScanAsync(port);
            anyUsable |= result.IsSuccess;

            LstDiscoveredPorts.Items.Add(result.IsSuccess
                ? new ScanRow
                {
                    Text = $"{port}  ·  {result.DetectedBaudRate} baud  ·  {result.DetectedFormat} 형식 확인",
                    Port = port,
                    BaudRate = result.DetectedBaudRate,
                    IsUsable = true
                }
                : new ScanRow { Text = $"{port}  ·  알려진 형식의 프레임을 확인하지 못했습니다" });
        }

        Report(100, anyUsable
            ? "검색 완료. 적용할 포트를 선택하세요."
            : "검색 완료. 프레임이 확인된 포트가 없습니다.");
    }

    private void Report(double percent, string status)
    {
        ProgScan.Value = percent;
        TxtProgressPercent.Text = $"{percent:F0}%";
        TxtStatus.Text = status;
    }

    /// <summary>Only a row the scanner actually confirmed can be applied.</summary>
    private void LstDiscoveredPorts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = LstDiscoveredPorts.SelectedItem as ScanRow;
        bool usable = row is { IsUsable: true };

        DiscoveredPort = usable ? row!.Port : string.Empty;
        DiscoveredBaudRate = usable ? row!.BaudRate : 0;
        BtnApply.IsEnabled = usable;
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e) => await RunScannerAsync();

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(DiscoveredPort)) return;

        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the dialog, as in every other dialog here.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
