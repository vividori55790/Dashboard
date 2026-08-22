using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Archive;

/// <summary>
/// What an export says it did.
/// </summary>
/// <remarks>
/// Only facts this command actually established: how many samples the query returned, which layout
/// they came out of, what was asked for, and the size of the file on disk afterwards. It does not
/// report a channel count, because the exporter returns a packet total and counting channels again
/// here would mean a second read that could disagree with the one that wrote the file.
/// </remarks>
public static class ExportReport
{
    /// <summary>Renders the block printed after a successful export.</summary>
    public static string Render(int written, string target, ExportCommandLine command, ArchiveLayoutKind layout)
    {
        ArgumentNullException.ThrowIfNull(command);

        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine(Line("exported", $"{written:N0} sample(s)"));
        text.AppendLine(Line("from", $"{command.ArchivePath} ({Describe(layout)})"));
        text.AppendLine(Line("selection", Selection(command.Filter)));
        text.AppendLine(Line("to", $"{target} ({Size(target)})"));
        text.AppendLine(Line(string.Empty, $"MATLAB / Octave   load('{Path.GetFileName(target)}')"));
        text.AppendLine(Line(string.Empty, $"Python            scipy.io.loadmat('{Path.GetFileName(target)}')"));
        text.AppendLine();
        return text.ToString();
    }

    /// <summary>Describes what the filter asked for, in the words the flags used.</summary>
    /// <remarks>
    /// Printed on success and on an empty result, and it is the empty case it is really for: an
    /// operator who exported nothing needs to see which of their four flags narrowed it to nothing.
    /// </remarks>
    public static string Selection(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.NodeId)) parts.Add($"node {filter.NodeId}");
        if (!string.IsNullOrWhiteSpace(filter.Variable)) parts.Add($"channel {filter.Variable}");
        if (filter.StartTime is { } from) parts.Add($"from {Stamp(from)}");
        if (filter.EndTime is { } to) parts.Add($"to {Stamp(to)}");
        if (filter.Limit != int.MaxValue) parts.Add($"first {filter.Limit:N0}");

        return parts.Count == 0 ? "the whole archive" : string.Join(", ", parts);
    }

    private static string Describe(ArchiveLayoutKind layout) => layout switch
    {
        ArchiveLayoutKind.Rows => "row layout",
        ArchiveLayoutKind.Tiered => "tiered layout",
        _ => layout.ToString().ToLowerInvariant()
    };

    private static string Stamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Size of the file that was just written, read from disk rather than estimated.</summary>
    private static string Size(string target)
    {
        long bytes;
        try
        {
            bytes = new FileInfo(target).Length;
        }
        catch (IOException)
        {
            return "size unavailable";
        }

        return bytes < 1024L
            ? $"{bytes} B"
            : bytes < 1024L * 1024L
                ? $"{bytes / 1024.0:N1} KB"
                : $"{bytes / (1024.0 * 1024.0):N1} MB";
    }

    private static string Line(string label, string value) => $"  {label,-13} {value}";
}
