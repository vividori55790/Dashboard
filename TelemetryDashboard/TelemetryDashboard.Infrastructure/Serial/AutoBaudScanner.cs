namespace TelemetryDashboard.Infrastructure.Serial;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

/// <summary>
/// Zero-Config Auto-Baud Rate & Format Scanner for automatic baud rate scanning and protocol format detection.
/// </summary>
public class AutoBaudScanner
{
    private readonly ISerialManager _serialManager;

    public static readonly int[] StandardBaudRates = { 9600, 19200, 38400, 57600, 115200, 921600 };

    public AutoBaudScanner(ISerialManager serialManager)
    {
        _serialManager = serialManager;
    }

    public async Task<ScanResult> ScanAsync(
        string portName,
        IEnumerable<int>? candidateBaudRates = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ScanResult(false, 0, PacketFormat.Unknown, portName);
        }

        IEnumerable<int> sourceBauds = candidateBaudRates ?? StandardBaudRates;
        int[] validRates = sourceBauds.Where(b => b > 0).ToArray();

        if (validRates.Length == 0)
        {
            return new ScanResult(false, 0, PacketFormat.Unknown, portName);
        }

        foreach (int baud in validRates)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                bool connected = await _serialManager.ConnectPortAsync(portName, baud, cancellationToken).ConfigureAwait(false);
                if (!connected) continue;

                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(300);

                try
                {
                    await foreach (RawPacket packet in _serialManager.PacketReader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    {
                        byte[] rawBytes = Encoding.UTF8.GetBytes(packet.RawLine);
                        PacketFormat format = DetectFormat(rawBytes);

                        if (format != PacketFormat.Unknown)
                        {
                            await _serialManager.DisconnectPortAsync(portName).ConfigureAwait(false);
                            return new ScanResult(true, baud, format, portName);
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Continue scanning next candidate rate on port read/connect errors
            }
            finally
            {
                try
                {
                    await _serialManager.DisconnectPortAsync(portName).ConfigureAwait(false);
                }
                catch { }
            }
        }

        return new ScanResult(false, 0, PacketFormat.Unknown, portName);
    }

    public PacketFormat DetectFormat(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return PacketFormat.Unknown;

        // Check for binary garbage: presence of null bytes or excessive non-printable control chars
        int nonPrintableCount = 0;
        foreach (byte b in bytes)
        {
            if (b == 0x00 || (b < 0x09) || (b > 0x0D && b < 0x20) || b >= 0x7F)
            {
                nonPrintableCount++;
            }
        }

        if (nonPrintableCount > 0)
        {
            return PacketFormat.Unknown;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes).Trim();
        }
        catch
        {
            return PacketFormat.Unknown;
        }

        return DetectFormat(text);
    }

    public PacketFormat DetectFormat(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return PacketFormat.Unknown;

        string trimmed = line.Trim();

        if (trimmed.StartsWith("$HEX,", StringComparison.OrdinalIgnoreCase))
        {
            return PacketFormat.Hex;
        }

        if (trimmed.StartsWith("$", StringComparison.OrdinalIgnoreCase))
        {
            return PacketFormat.Prefix;
        }

        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            return PacketFormat.Json;
        }

        string[] parts = trimmed.Split(',');
        if (parts.Length >= 3 && parts.All(p => !p.Any(c => char.IsControl(c))))
        {
            return PacketFormat.Columns;
        }

        return PacketFormat.Unknown;
    }
}
