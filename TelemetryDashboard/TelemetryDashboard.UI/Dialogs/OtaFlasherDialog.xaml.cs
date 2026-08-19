using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows;
using TelemetryDashboard.Core.Firmware;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.UI.Dialogs;

public partial class OtaFlasherDialog : Window
{
    private readonly EdgeMcuOtaFlasher _flasher = new();

    public OtaFlasherDialog()
    {
        InitializeComponent();
        LoadPorts();
    }

    private void LoadPorts()
    {
        CboPort.Items.Clear();
        var ports = SerialPort.GetPortNames();
        foreach (var p in ports) CboPort.Items.Add(p);
        if (CboPort.Items.Count == 0) CboPort.Items.Add("COM3 (Virtual DAB)");
        CboPort.SelectedIndex = 0;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Firmware Binary/Hex (*.bin;*.hex;*.elf)|*.bin;*.hex;*.elf|All Files (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            TxtFirmwarePath.Text = ofd.FileName;
        }
    }

    private async void BtnStartFlash_Click(object sender, RoutedEventArgs e)
    {
        string targetPort = CboPort.SelectedItem?.ToString() ?? string.Empty;
        string filePath = TxtFirmwarePath.Text;

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
            TxtProgressStatus.Text = "❌ 펌웨어 파싱 실패";
            TxtLog.Text += $"\n[ERROR] {ex.Message}";
            MessageBox.Show(this, ex.Message, "펌웨어 파싱 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        uint expectedCrc = EdgeMcuOtaFlasher.ComputeCrc32(image);

        BtnStartFlash.IsEnabled = false;
        PbFlashProgress.Value = 0;
        TxtProgressStatus.Text = $"상태: {targetPort} 부트로더 진입 중...";
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
                TxtProgressStatus.Text = $"✅ 전송 완료 — {result.BytesSent:N0} bytes";
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
                TxtProgressStatus.Text = "❌ 플래싱 실패";
                TxtLog.Text += $"\n[FAIL] {result.Message}";
                MessageBox.Show(this, result.Message, "OTA 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _flasher.FlashProgressChanged -= OnFlashProgress;
            BtnStartFlash.IsEnabled = true;
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

    /// <summary>Injected chunk transport; set by the host once a bootloader link is open.</summary>
    private Func<byte[], Task<bool>>? _transport;

    /// <summary>Attaches the serial or IP transport used to deliver firmware chunks.</summary>
    public void AttachTransport(Func<byte[], Task<bool>> transport) => _transport = transport;

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
