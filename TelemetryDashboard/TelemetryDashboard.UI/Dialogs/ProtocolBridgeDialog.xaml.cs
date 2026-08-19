using System;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Protocols;

namespace TelemetryDashboard.UI.Dialogs;

public partial class ProtocolBridgeDialog : Window
{
    private readonly IndustrialProtocolBridge _bridge = new();

    public ProtocolBridgeDialog()
    {
        InitializeComponent();
        RunConversion();
    }

    private void CboProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtRawHex == null) return;
        int idx = CboProtocol.SelectedIndex;
        if (idx == 0) // CAN
        {
            TxtRawHex.Text = "08 00 23 01 00 00 00 00 48 42";
        }
        else if (idx == 1) // Modbus RTU
        {
            TxtRawHex.Text = "01 03 0A DC";
        }
        else // ROS2
        {
            TxtRawHex.Text = "52 4F 53 32 5F 54 45 4C 45 4D";
        }
    }

    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        RunConversion();
    }

    private void RunConversion()
    {
        try
        {
            string hexClean = TxtRawHex.Text.Replace(" ", "").Replace("-", "").Replace("0x", "");
            byte[] rawBytes = Convert.FromHexString(hexClean);

            byte[] jsonBytes = _bridge.ConvertToStandardPacket(rawBytes);
            string jsonText = Encoding.UTF8.GetString(jsonBytes);

            // Pretty format JSON
            using var doc = JsonDocument.Parse(jsonText);
            TxtConvertedJson.Text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            TxtConvertedJson.Text = $"❌ 변환 오류: {ex.Message}";
        }
    }

    private void BtnCopyJson_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtConvertedJson.Text);
        MessageBox.Show(this, "JSON payload copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
