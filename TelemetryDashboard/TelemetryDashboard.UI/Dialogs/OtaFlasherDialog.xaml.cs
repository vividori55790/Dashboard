using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TelemetryDashboard.Core.Firmware;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Streams a firmware image to an edge MCU bootloader and reports what actually left the machine.
/// </summary>
/// <remarks>
/// The transfer path itself was corrected earlier: no firmware image is invented, and success means
/// "transmitted", not "flashed". What remained were the claims around it. The port list offered a
/// fabricated virtual port when the machine had no serial ports, so a bench with nothing
/// attached still looked ready; the firmware box was pre-filled with
/// <c>C:\Firmware\mcu_node_v2.1.bin</c>; and the footer promised a 3000 ms bootloader watchdog that
/// appears nowhere in <see cref="EdgeMcuOtaFlasher"/>. The figures shown are now read off the
/// flasher, and the start button stays disabled until a transport exists to carry the bytes.
/// </remarks>
public partial class OtaFlasherDialog : Window
{
    /// <summary>Segoe Fluent Icons checkmark.</summary>
    private const string CheckmarkGlyph = "\uE73E";

    /// <summary>Segoe Fluent Icons warning triangle.</summary>
    private const string WarningGlyph = "\uE7BA";

    private readonly EdgeMcuOtaFlasher _flasher = new();

    /// <summary>Injected chunk transport; set by the host once a bootloader link is open.</summary>
    private Func<byte[], Task<bool>>? _transport;

    public OtaFlasherDialog()
    {
        InitializeComponent();
        LoadPorts();
        ShowTransferParameters();
        ShowLinkState();
    }

    /// <summary>
    /// Lists the serial ports this machine reports.
    /// </summary>
    /// <remarks>
    /// An empty list is left empty and said out loud. The previous fallback added a made-up virtual
    /// port — not a device, and not even a name the serial stack would accept — which made a
    /// machine with no hardware attached present a selectable target.
    /// </remarks>
    private void LoadPorts()
    {
        CboPort.Items.Clear();

        string[] ports = SerialPort.GetPortNames();
        foreach (string port in ports)
        {
            CboPort.Items.Add(port);
        }

        if (ports.Length == 0)
        {
            CboPort.IsEnabled = false;
            CboPort.Items.Add("사용 가능한 직렬 포트 없음");
        }
        else
        {
            CboPort.IsEnabled = true;
        }

        CboPort.SelectedIndex = 0;
    }

    /// <summary>Reports the chunking the flasher will use, rather than an invented safety figure.</summary>
    private void ShowTransferParameters() =>
        TxtTransferParams.Text =
            $"청크 {_flasher.ChunkSize:N0} bytes · 청크당 최대 재시도 {_flasher.MaxRetriesPerChunk}회 · " +
            $"재시도 간격 {_flasher.RetryDelay.TotalMilliseconds:F0} ms · 청크 간격 {_flasher.ChunkPacing.TotalMilliseconds:F0} ms. " +
            "지원 형식: .bin, .hex (Intel HEX는 전송 전에 이진으로 디코딩됩니다).";

    /// <summary>
    /// States whether a transport is attached, and gates the start button on it.
    /// </summary>
    /// <remarks>
    /// Without <see cref="AttachTransport"/> every chunk send returns false, so a transfer can only
    /// end in "ACK 없음" after exhausting the retries. Offering an enabled button for that is an
    /// invitation to read the failure as a hardware fault.
    /// </remarks>
    private void ShowLinkState()
    {
        bool attached = _transport is not null;

        string accent = attached ? "SuccessBrush" : "WarningBrush";
        LinkBanner.SetResourceReference(BackgroundProperty,
            attached ? "SuccessSubtleBrush" : "WarningSubtleBrush");
        LinkBanner.SetResourceReference(BorderBrushProperty, accent);
        LinkIcon.SetResourceReference(ForegroundProperty, accent);
        LinkIcon.Text = attached ? CheckmarkGlyph : WarningGlyph;
        TxtLinkState.SetResourceReference(ForegroundProperty, accent);
        TxtLinkState.Text = attached
            ? "부트로더 링크가 연결되어 있습니다. 각 청크는 장치의 ACK를 기다립니다."
            : "부트로더 링크가 연결되지 않았습니다. 전송 경로가 없어 플래싱을 시작할 수 없습니다.";

        BtnStartFlash.IsEnabled = attached;
    }

    /// <summary>Attaches the serial or IP transport used to deliver firmware chunks.</summary>
    public void AttachTransport(Func<byte[], Task<bool>> transport)
    {
        _transport = transport;
        ShowLinkState();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "펌웨어 이미지 선택",
            Filter = "Firmware Binary/Hex (*.bin;*.hex;*.elf)|*.bin;*.hex;*.elf|All Files (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            TxtFirmwarePath.Text = ofd.FileName;
        }
    }

    private async void BtnStartFlash_Click(object sender, RoutedEventArgs e)
    {
        string targetPort = CboPort.IsEnabled ? CboPort.SelectedItem?.ToString() ?? string.Empty : string.Empty;
        string filePath = TxtFirmwarePath.Text;

        if (targetPort.Length == 0)
        {
            MessageBox.Show(this, "전송할 직렬 포트가 없습니다.",
                "포트 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            // Never invent a firmware image. Fabricating a blank 64 KB file made the dialog
            // report a successful flash for a file that did not exist.
            MessageBox.Show(this, "존재하는 펌웨어 파일(.bin 또는 .hex)을 선택하세요.",
                "펌웨어 파일 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FirmwareImage image;
        try
        {
            image = EdgeMcuOtaFlasher.LoadImage(filePath);
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            TxtProgressStatus.Text = "펌웨어 파싱 실패";
            TxtLog.Text += $"\n[ERROR] {ex.Message}";
            MessageBox.Show(this, ex.Message, "펌웨어 파싱 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        uint expectedCrc = EdgeMcuOtaFlasher.ComputeCrc32(image);

        BtnStartFlash.IsEnabled = false;
        PbFlashProgress.Value = 0;
        TxtProgressStatus.Text = $"{targetPort} 부트로더 진입 중";
        TxtLog.Text =
            $"[INIT] Target: {targetPort} | File: {Path.GetFileName(filePath)}\n" +
            $"[PARSE] Format={image.Format} Segments={image.Segments.Count} " +
            $"Bytes={image.TotalBytes:N0} Start=0x{image.StartAddress:X8}\n" +
            $"[CRC32] 0x{expectedCrc:X8}";

        _flasher.FlashProgressChanged += OnFlashProgress;
        try
        {
            OtaFlashResult result = await _flasher.FlashFirmwareAsync(
                targetPort, filePath, SendChunkAsync);

            if (result.Success)
            {
                TxtProgressStatus.Text = $"전송 완료 — {result.BytesSent:N0} bytes";
                // State only what happened: the image left this machine. Confirming it landed
                // correctly requires the device to echo back its own CRC.
                TxtLog.Text +=
                    $"\n[SENT] {result.BytesSent:N0}/{result.TotalBytes:N0} bytes transmitted." +
                    $"\n[VERIFY] Device must confirm CRC32 0x{result.ImageCrc32:X8} before reboot.";
                MessageBox.Show(this,
                    $"펌웨어 {result.BytesSent:N0} bytes 전송을 완료했습니다.\n\n" +
                    $"CRC32: 0x{result.ImageCrc32:X8}\n\n" +
                    "장치가 이 CRC를 확인한 뒤 재부팅해야 플래싱이 확정됩니다.",
                    "OTA 전송 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtProgressStatus.Text = "전송 실패";
                TxtLog.Text += $"\n[FAIL] {result.Message}";
                MessageBox.Show(this, result.Message, "OTA 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _flasher.FlashProgressChanged -= OnFlashProgress;
            ShowLinkState();
        }
    }

    /// <summary>
    /// Transmits one chunk to the device. No transport is wired into this dialog yet, so it
    /// reports the absence of a link rather than acknowledging a delivery that never happened.
    /// </summary>
    private Task<bool> SendChunkAsync(byte[] chunk)
    {
        if (_transport is null) return Task.FromResult(false);
        return _transport(chunk);
    }

    private void OnFlashProgress(object? sender, OtaFlashProgressEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            PbFlashProgress.Value = e.ProgressPercentage;
            TxtProgressPercent.Text = $"{e.ProgressPercentage:F0}%";
            TxtProgressStatus.Text = e.StatusMessage;
            TxtLog.Text += $"\n[OTA] {e.StatusMessage}";
            TxtLog.ScrollToEnd();
        });
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
