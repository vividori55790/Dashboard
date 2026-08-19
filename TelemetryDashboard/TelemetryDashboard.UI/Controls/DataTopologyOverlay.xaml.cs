using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TelemetryDashboard.UI.Controls;

public partial class DataTopologyOverlay : UserControl
{
    public event Action<string>? OnNodeSelected;

    public DataTopologyOverlay()
    {
        InitializeComponent();
    }

    public void UpdateTopologyStatus(string sourceInfo, double packetRateHz, bool isSimulating)
    {
        Dispatcher.Invoke(() =>
        {
            SourceDetailsText.Text = sourceInfo;
            ThroughputText.Text = $"Data Stream: {(isSimulating ? "Simulating" : "Active")} ({packetRateHz:F0} Hz)";
            StatusLed.Fill = isSimulating ? new SolidColorBrush(Color.FromRgb(255, 179, 0)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));
        });
    }

    private void Node_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Name is string name)
        {
            OnNodeSelected?.Invoke(name);
            MessageBox.Show($"[유기적 데이터 파이프라인 노드 선택]\n노드: {name}\n이 노드에서 수신/파싱/연산되는 데이터 채널을 하이라이트합니다.", "Topology Node Selected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
