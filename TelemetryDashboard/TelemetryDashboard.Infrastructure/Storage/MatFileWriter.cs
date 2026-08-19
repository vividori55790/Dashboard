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

    /// <summary>Longest variable name MATLAB accepts.</summary>
    private const int MaxNameLength = 31;

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

        var byChannel = (packets ?? Enumerable.Empty<TelemetryPacket>())
            .Where(p => p is not null)
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Variable) ? "channel" : p.Variable,
                     StringComparer.OrdinalIgnoreCase);

        using var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in byChannel)
        {
            List<TelemetryPacket> rows = channel.OrderBy(p => p.Timestamp).ToList();
            WriteMatrix(writer, SanitizeName(channel.Key, taken), rows);
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

    /// <summary>MAT variable names must be ASCII identifiers, and distinct within one file.</summary>
    /// <remarks>
    /// The accepted character set is ASCII-only on purpose. <see cref="char.IsLetterOrDigit(char)"/>
    /// accepts any Unicode letter, so a Korean channel name such as "온도" passed sanitisation intact
    /// and was flattened to "??" by <see cref="Encoding.ASCII"/> further down — not a legal MATLAB
    /// identifier, and the name every other non-ASCII channel collapsed onto as well.
    /// <para>
    /// <paramref name="taken"/> is what makes the collapse survivable rather than silent. Two
    /// channels writing the same name produce two matrices called the same thing, and a loader keeps
    /// whichever it read last — the first channel is gone from the export with nothing to show it was
    /// ever there. Truncation to <see cref="MaxNameLength"/> collides the same way.
    /// </para>
    /// </remarks>
    private static string SanitizeName(string raw, HashSet<string> taken)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        // MATLAB identifiers must begin with a letter, so a leading digit or underscore is prefixed
        // rather than only a leading digit: sanitising "온도" yields "__", which loads in SciPy but
        // is rejected by isvarname and cannot be typed at a MATLAB prompt.
        string name = builder.ToString();
        if (name.Length == 0 || !char.IsAsciiLetter(name[0])) name = "ch_" + name;
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        string unique = name;
        for (int suffix = 2; !taken.Add(unique); suffix++)
        {
            string tail = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            unique = name.Length + tail.Length > MaxNameLength
                ? name[..(MaxNameLength - tail.Length)] + tail
                : name + tail;
        }

        return unique;
    }
}
