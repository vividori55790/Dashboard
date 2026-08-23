using System;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TelemetryDashboard.Core.Protocols;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Decodes one pasted industrial frame with the same bridge the ingest path uses.
/// </summary>
/// <remarks>
/// The combo box never selected a decoder. <see cref="IndustrialProtocolBridge"/> identifies a frame
/// by sniffing its bytes, so the selection only swapped the sample hex — yet the control was labelled
/// "프로토콜 선택" and sat next to the convert button, which reads as though it determined the result.
/// It is now labelled as the example loader it is, and the outcome line reports whether the sniff
/// matched anything, because an unmatched frame comes back as a packet carrying the raw bytes and
/// <c>recognized: false</c> — output that looks like a successful conversion until it is read.
/// </remarks>
public partial class ProtocolBridgeDialog : Window
{
    /// <summary>
    /// Sample frames, each one a payload the corresponding adapter genuinely decodes.
    /// </summary>
    /// <remarks>
    /// The previous samples did not. <c>01 03 0A DC</c> is four bytes and Modbus recognition needs
    /// five, and the ROS2 entry was the ASCII text "ROS2_TELEM", which carries no CDR encapsulation
    /// header — both fell through to the facade's unrecognised branch, so two of the three examples
    /// shipped with the dialog demonstrated nothing but the fall-through path. These are a
    /// CRC-valid Modbus read response (slave 1, function 0x03, one register = 2780) and a
    /// little-endian CDR message carrying topic "temp" = 25.5.
    /// </remarks>
    private static readonly string[] SampleFrames =
    {
        "08 00 23 01 00 00 00 00 48 42",
        "01 03 02 0A DC BF 7D",
        "00 01 00 00 05 00 00 00 74 65 6D 70 00 00 00 00 00 00 00 00 00 00 00 00 00 80 39 40"
    };

    private readonly IndustrialProtocolBridge _bridge = new();

    public ProtocolBridgeDialog()
    {
        InitializeComponent();

        TxtRegistryInfo.Text =
            $"등록된 어댑터 {_bridge.Registry.Count}개: {string.Join(", ", _bridge.Registry.ProtocolNames)}";

        RunConversion();
    }

    private void CboProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtRawHex is null) return;

        int index = CboProtocol.SelectedIndex;
        if (index < 0 || index >= SampleFrames.Length) return;

        TxtRawHex.Text = SampleFrames[index];
    }

    private void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        RunConversion();
    }

    /// <summary>
    /// Runs the bridge and reports both the packet and whether anything recognised the frame.
    /// </summary>
    private void RunConversion()
    {
        string hexClean = TxtRawHex.Text
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (hexClean.Length == 0)
        {
            SetOutcome("입력 없음", "TextTertiaryBrush");
            TxtConvertedJson.Text = "변환할 바이트가 없습니다.";
            return;
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Convert.FromHexString(hexClean);
        }
        catch (FormatException)
        {
            SetOutcome("16진수 형식 오류", "DangerBrush");
            TxtConvertedJson.Text =
                "16진수로 해석할 수 없습니다. 0-9와 A-F만 사용하고, 바이트 수는 짝수여야 합니다.";
            return;
        }

        try
        {
            byte[] jsonBytes = _bridge.ConvertToStandardPacket(rawBytes);
            string jsonText = Encoding.UTF8.GetString(jsonBytes);

            using JsonDocument doc = JsonDocument.Parse(jsonText);
            TxtConvertedJson.Text = JsonSerializer.Serialize(
                doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

            ReportMatch(doc.RootElement, rawBytes.Length);
        }
        catch (JsonException ex)
        {
            SetOutcome("변환 실패", "DangerBrush");
            TxtConvertedJson.Text = $"어댑터가 유효한 JSON을 생성하지 못했습니다: {ex.Message}";
        }
    }

    /// <summary>
    /// Names the adapter that claimed the frame, or states that none did.
    /// </summary>
    /// <remarks>
    /// The facade's fall-through packet carries <c>recognized: false</c> alongside the raw bytes. It
    /// is well-formed JSON and prints exactly like a decoded frame, so without this line an operator
    /// reading indented output has no signal that nothing understood their payload.
    /// </remarks>
    private void ReportMatch(JsonElement packet, int byteCount)
    {
        if (packet.TryGetProperty("recognized", out JsonElement recognized)
            && recognized.ValueKind == JsonValueKind.False)
        {
            SetOutcome($"{byteCount} bytes — 일치하는 어댑터 없음 (원본 바이트만 전달됨)", "WarningBrush");
            return;
        }

        string protocol = packet.TryGetProperty("protocol", out JsonElement name)
            ? name.GetString() ?? "이름 없음"
            : "이름 없음";

        SetOutcome($"{byteCount} bytes — {protocol} 어댑터가 처리", "SuccessBrush");
    }

    private void SetOutcome(string text, string brushKey)
    {
        TxtMatchState.Text = text;
        TxtMatchState.SetResourceReference(ForegroundProperty, brushKey);
    }

    private void BtnCopyJson_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtConvertedJson.Text);
        TxtMatchState.Text = "JSON을 클립보드에 복사했습니다.";
        TxtMatchState.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
