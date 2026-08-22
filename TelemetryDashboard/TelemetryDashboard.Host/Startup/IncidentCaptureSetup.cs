using System.Collections.Generic;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Outbound;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Whether the incident capture can run, and what the banner says about it either way.
/// </summary>
/// <remarks>
/// Its own file for the reason <see cref="ArchiveSetup"/> is: the decision has a precondition, and
/// a precondition buried among four other relay constructions is one that gets forgotten. The
/// window comes out of the archive, so without <c>--archive</c> there is nothing to capture — and
/// the honest response is to say so once at start-up rather than to write empty reports all night.
/// </remarks>
public static class IncidentCaptureSetup
{
    /// <summary>Builds the relay, or explains on the banner why it is not running.</summary>
    public static IncidentCaptureRelay? Create(
        HostOptions options, IDataLogger? archive, List<string> banner)
    {
        if (options.IncidentDirectory is null) return null;

        if (archive is null)
        {
            banner.Add("  incidents     REFUSED -- --incident-dir needs --archive; the report is the "
                + "window before the crossing and that comes out of the archive");
            return null;
        }

        banner.Add($"  incidents     {options.IncidentDirectory}");
        banner.Add($"                one report per rule per "
            + $"{IncidentCaptureRelay.DefaultCooldown.TotalMinutes:0} min, "
            + $"{IncidentCaptureRelay.LeadSeconds:0}s before the crossing and "
            + $"{IncidentCaptureRelay.TrailSeconds:0}s after");

        return new IncidentCaptureRelay(archive, options.IncidentDirectory);
    }
}
