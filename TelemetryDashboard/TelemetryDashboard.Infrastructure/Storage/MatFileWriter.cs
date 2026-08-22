using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// Exports telemetry to a MATLAB Level 4 MAT-file, readable by MATLAB, Octave and SciPy.
/// </summary>
/// <remarks>
/// Level 4 is chosen deliberately: its header is a fixed 20-byte record followed by
/// column-major float64 data, so the writer is small enough to verify by inspection while
/// producing a genuinely loadable file. Each channel becomes one named matrix of
/// <c>[timestamp, value]</c> rows.
/// </remarks>
public sealed class MatFileWriter
{
    private const int MatrixTypeLittleEndianDouble = 0000; // little-endian, double, full matrix


    /// <summary>Writes one matrix per channel. Throws when the destination cannot be written.</summary>
    public void WritePackets(string targetFilePath, IEnumerable<TelemetryPacket> packets)
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            throw new ArgumentException("Target path must be provided.", nameof(targetFilePath));
        }

        // Surface an unusable destination before any partial file is produced.
        string? directory = Path.GetDirectoryName(Path.GetFullPath(targetFilePath));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Export directory does not exist: {directory}");
        }

        List<TelemetryPacket> all = (packets ?? Enumerable.Empty<TelemetryPacket>())
            .Where(p => p is not null)
            .ToList();

        // Grouping by channel alone merged every node reporting that channel into one matrix: two
        // converters on one hub, both publishing Vout, arrived as a single column of interleaved
        // readings from two devices with nothing left to separate them by. Invisible while the only
        // caller was a desktop app watching one rig, and the normal case for a hub.
        bool qualify = all
            .Select(p => p.NodeId ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

        var byChannel = all.GroupBy(p => MatVariableName.For(p, qualify), StringComparer.OrdinalIgnoreCase);

        using var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in byChannel)
        {
            List<TelemetryPacket> rows = channel.OrderBy(p => p.Timestamp).ToList();
            WriteMatrix(writer, MatVariableName.Sanitize(channel.Key, taken), rows);
        }
    }

    /// <summary>Writes one Level 4 matrix: 20-byte header, name, then column-major doubles.</summary>
    private static void WriteMatrix(BinaryWriter writer, string name, List<TelemetryPacket> rows)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);

        writer.Write(MatrixTypeLittleEndianDouble);
        writer.Write(rows.Count);          // mrows
        writer.Write(2);                   // ncols: timestamp, value
        writer.Write(0);                   // imagf: real only
        writer.Write(nameBytes.Length + 1); // namlen includes the terminator
        writer.Write(nameBytes);
        writer.Write((byte)0);

        // MAT is column-major: the entire timestamp column precedes the entire value column.
        foreach (TelemetryPacket packet in rows)
        {
            writer.Write(ToMatlabDateNumber(packet.Timestamp));
        }

        foreach (TelemetryPacket packet in rows)
        {
            writer.Write(packet.Value);
        }
    }

    /// <summary>Converts to MATLAB's datenum: days since year 0, where 1970-01-01 is 719529.</summary>
    private static double ToMatlabDateNumber(DateTime timestamp) =>
        719529.0 + (timestamp.ToUniversalTime() - DateTime.UnixEpoch).TotalDays;
}
