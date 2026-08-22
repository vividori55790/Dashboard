using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Builds one incident report and puts it on disk.
/// </summary>
/// <remarks>
/// Split from <see cref="IncidentCaptureRelay"/> because deciding whether to capture and writing
/// what was captured are different jobs, and only one of them touches a file system. The relay
/// holds the throttle and the counters; this holds the wait, the query and the bytes.
/// </remarks>
internal static class IncidentReportWriter
{
    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    /// <summary>
    /// Waits, reads the window around the crossing, and writes it. Returns the path, or null.
    /// </summary>
    /// <remarks>
    /// The wait is not politeness. Capturing at the instant of the crossing produced a report
    /// containing neither the event nor its aftermath: the archive is written through a bounded
    /// channel and a drain, so the samples that caused the breach were still in flight, and the
    /// seconds of response had not happened yet. The first live capture held a 47.96..48.07 V
    /// window for a channel that had just gone to 54 V and named nothing as anomalous.
    /// </remarks>
    public static async Task<string?> WriteAsync(
        IDataLogger archive, string directory, ScoredSample sample, BreachedLimit breach, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        IncidentEndpoint.Result report = await IncidentEndpoint.QueryAsync(
            archive, sample.TimestampUtc,
            IncidentCaptureRelay.LeadSeconds, IncidentCaptureRelay.TrailSeconds,
            sample.NodeId).ConfigureAwait(false);

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileNameFor(sample));

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                new { trigger = breach.Rule.Declaration, channel = sample.Channel, report }, Layout),
            Core.Services.Utf8Files.WithoutBom).ConfigureAwait(false);

        return path;
    }

    /// <summary>A name that sorts by time and says what tripped, without the file being opened.</summary>
    private static string FileNameFor(ScoredSample sample) =>
        $"incident-{sample.TimestampUtc:yyyyMMdd-HHmmss.fff}-{Sanitise(sample.Channel)}.json";

    /// <summary>Channel ids carry dots and can carry worse; a file name may not.</summary>
    private static string Sanitise(string raw)
    {
        var clean = new StringBuilder(raw.Length);
        foreach (char c in raw) clean.Append(char.IsLetterOrDigit(c) ? c : '_');

        return clean.Length == 0 ? "channel" : clean.ToString();
    }
}
