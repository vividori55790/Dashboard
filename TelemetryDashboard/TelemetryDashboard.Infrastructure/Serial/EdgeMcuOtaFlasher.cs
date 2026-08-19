using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Firmware;

namespace TelemetryDashboard.Infrastructure.Serial;

public class OtaFlashProgressEventArgs : EventArgs
{
    public string DevicePort { get; set; } = string.Empty;
    public int BytesSent { get; set; }
    public int TotalBytes { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? BytesSent / (double)TotalBytes * 100.0 : 0.0;
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>Outcome of an OTA session.</summary>
public sealed class OtaFlashResult
{
    public required bool Success { get; init; }
    public required int BytesSent { get; init; }
    public required int TotalBytes { get; init; }
    public required string Message { get; init; }

    /// <summary>CRC-32 over the decoded firmware payload, for the device to verify against.</summary>
    public uint ImageCrc32 { get; init; }
}

/// <summary>
/// Streams firmware to an edge MCU over serial or IP with chunked transfer and progress reporting.
/// </summary>
/// <remarks>
/// Accepts raw <c>.bin</c> images and Intel <c>.hex</c> files, which are decoded to binary before
/// transmission rather than sent as ASCII text. Each chunk is retried a bounded number of times,
/// and a CRC-32 of the decoded image is reported so the device can confirm what it received.
/// </remarks>
public class EdgeMcuOtaFlasher
{
    /// <summary>Payload bytes per transfer chunk.</summary>
    public int ChunkSize { get; set; } = 256;

    /// <summary>Attempts per chunk before the transfer is abandoned.</summary>
    public int MaxRetriesPerChunk { get; set; } = 5;

    /// <summary>Delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Pacing delay between successful chunks.</summary>
    public TimeSpan ChunkPacing { get; set; } = TimeSpan.FromMilliseconds(10);

    public event EventHandler<OtaFlashProgressEventArgs>? FlashProgressChanged;

    /// <summary>Loads and decodes a firmware file into a flat binary image.</summary>
    public static FirmwareImage LoadImage(string firmwareFilePath)
    {
        byte[] raw = File.ReadAllBytes(firmwareFilePath);
        string extension = Path.GetExtension(firmwareFilePath);

        bool declaredHex = extension.Equals(".hex", StringComparison.OrdinalIgnoreCase);
        bool looksHex = raw.Length > 0 && IntelHexParser.LooksLikeIntelHex(Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, 64)));

        if (declaredHex || looksHex)
        {
            return IntelHexParser.Parse(Encoding.ASCII.GetString(raw));
        }

        return new FirmwareImage
        {
            Format = "bin",
            Segments = new[] { new FirmwareSegment { BaseAddress = 0, Data = raw } }
        };
    }

    public async Task<OtaFlashResult> FlashFirmwareAsync(
        string portName,
        string firmwareFilePath,
        Func<byte[], Task<bool>> txChunkSender,
        CancellationToken ct = default)
    {
        if (!File.Exists(firmwareFilePath))
        {
            return Fail(portName, 0, 0, "펌웨어 파일을 찾을 수 없습니다.");
        }

        FirmwareImage image;
        try
        {
            image = LoadImage(firmwareFilePath);
        }
        catch (FormatException ex)
        {
            // A corrupt .hex must abort before a single byte reaches the device.
            return Fail(portName, 0, 0, $"펌웨어 파싱 실패: {ex.Message}");
        }

        int totalBytes = image.TotalBytes;
        uint crc = ComputeCrc32(image);

        ReportProgress(portName, 0, totalBytes,
            $"OTA 개시 — 형식 {image.Format}, {image.Segments.Count}개 세그먼트, {totalBytes:N0} bytes, CRC32 0x{crc:X8}");

        int sent = 0;
        foreach (FirmwareSegment segment in image.Segments)
        {
            int offset = 0;
            while (offset < segment.Length)
            {
                ct.ThrowIfCancellationRequested();

                int length = Math.Min(ChunkSize, segment.Length - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(segment.Data, offset, chunk, 0, length);

                bool acknowledged = await SendWithRetryAsync(portName, chunk, txChunkSender, sent, totalBytes, ct)
                    .ConfigureAwait(false);

                if (!acknowledged)
                {
                    // Bounded retries: the old loop spun forever on a device that never ACKed.
                    return Fail(portName, sent, totalBytes,
                        $"청크 전송 실패 — {MaxRetriesPerChunk}회 재시도 후 ACK 없음 (offset 0x{segment.BaseAddress + offset:X8})");
                }

                offset += length;
                sent += length;
                ReportProgress(portName, sent, totalBytes, $"전송 중... ({sent:N0}/{totalBytes:N0} bytes)");

                if (ChunkPacing > TimeSpan.Zero)
                {
                    await Task.Delay(ChunkPacing, ct).ConfigureAwait(false);
                }
            }
        }

        ReportProgress(portName, sent, totalBytes, $"OTA 완료 — {sent:N0} bytes 전송, CRC32 0x{crc:X8} 검증 요청");

        return new OtaFlashResult
        {
            Success = true,
            BytesSent = sent,
            TotalBytes = totalBytes,
            ImageCrc32 = crc,
            Message = "OTA 펌웨어 플래싱 완료"
        };
    }

    private async Task<bool> SendWithRetryAsync(
        string portName, byte[] chunk, Func<byte[], Task<bool>> sender,
        int sentSoFar, int totalBytes, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= Math.Max(1, MaxRetriesPerChunk); attempt++)
        {
            if (await sender(chunk).ConfigureAwait(false)) return true;

            ReportProgress(portName, sentSoFar, totalBytes,
                $"ACK 누락 — 재시도 {attempt}/{MaxRetriesPerChunk}");

            if (RetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <summary>CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) over the decoded payload.</summary>
    public static uint ComputeCrc32(FirmwareImage image)
    {
        uint crc = 0xFFFFFFFF;

        foreach (FirmwareSegment segment in image.Segments)
        {
            foreach (byte value in segment.Data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
        }

        return ~crc;
    }

    private OtaFlashResult Fail(string port, int sent, int total, string message)
    {
        ReportProgress(port, sent, total, message);
        return new OtaFlashResult { Success = false, BytesSent = sent, TotalBytes = total, Message = message };
    }

    private void ReportProgress(string port, int sent, int total, string message) =>
        FlashProgressChanged?.Invoke(this, new OtaFlashProgressEventArgs
        {
            DevicePort = port,
            BytesSent = sent,
            TotalBytes = total,
            StatusMessage = message
        });
}
