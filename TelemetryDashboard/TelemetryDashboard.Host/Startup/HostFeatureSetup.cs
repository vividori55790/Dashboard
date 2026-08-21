using System;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Attaches the features a run's configuration asks for to the running server.
/// </summary>
/// <remarks>
/// One place, and one profile resolution. Each feature used to resolve the profile for itself, so a
/// simulated run loaded and parsed the profile set three times to answer the same question — and
/// three call sites is three chances for them to disagree about which machine this run is watching,
/// which is the class of disagreement profiles exist to remove.
/// <para>
/// Order matters and is not incidental: the limit monitor is read by the ingest publisher when the
/// publisher is constructed, so a monitor attached after the pump would watch nothing while
/// <c>/api/limits</c> showed a clean alarm list.
/// </para>
/// </remarks>
public static class HostFeatureSetup
{
    /// <summary>Attaches derived channels and engineering limits. Call before building the pump.</summary>
    public static void Attach(HostOptions options, TelemetryStreamingServer server)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(server);

        MonitoringProfile? profile = options.GeneratesFromProfile
            ? ProfileResolution.Resolve(options.ProfileId, AppContext.BaseDirectory).Profile
            : null;

        ComputedChannelSetup.Attach(options, server, profile);
        LimitSetup.Attach(options, server, profile);
    }
}
