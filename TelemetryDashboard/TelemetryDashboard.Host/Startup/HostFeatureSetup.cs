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
    /// <summary>
    /// Attaches derived channels, engineering limits and the control surface. Call before the pump.
    /// </summary>
    public static void Attach(
        HostOptions options, TelemetryStreamingServer server, Ingest.ITelemetrySource? source)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(server);

        MonitoringProfile? profile = ActiveProfile(options);

        ComputedChannelSetup.Attach(options, server, profile);
        LimitSetup.Attach(options, server, profile);
        ControlSetup.Attach(server, source);

        SignalSetup.Result signals = SignalSetup.Apply(options, source);
        foreach (string line in SignalSetup.BannerLines(signals)) Console.WriteLine(line);
    }

    /// <summary>
    /// The profile this run should be judged against, or null when nobody named one.
    /// </summary>
    /// <remarks>
    /// A generated source needs a profile to exist at all, so it gets one whether or not the
    /// operator named it. A real device does not need one to produce readings — but the profile is
    /// also where the rig's safe bands, derived channels and twin placements are declared, and this
    /// used to consult it only for generated sources. So a bench with an MCU attached ran with no
    /// bands, no computed channels and no placements, and the flag that would have supplied them
    /// was accepted and ignored.
    /// <para>
    /// On a real device it applies only when asked for by name. Falling back to the bundled
    /// converter profile there would impose one customer's numbers on somebody else's hardware,
    /// and a band nobody chose is worse than none: it either cries wolf or, written for a machine
    /// this is not, never fires.
    /// </para>
    /// </remarks>
    public static MonitoringProfile? ActiveProfile(HostOptions options) =>
        options.GeneratesFromProfile || options.ProfileId is not null
            ? ProfileResolution.Resolve(options.ProfileId, AppContext.BaseDirectory).Profile
            : null;
}
