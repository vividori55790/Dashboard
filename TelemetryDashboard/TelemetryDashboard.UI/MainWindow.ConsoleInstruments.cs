using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI;

/// <summary>
/// Handing the shell's own instruments to the console it serves.
/// </summary>
/// <remarks>
/// The desktop shell runs the same streaming server as the headless host and serves the same
/// pages, and attached none of them: <c>Archive</c>, <c>Limits</c> and <c>Control</c> were left
/// null. So a browser pointed at the shell — which is the whole reason an engineer at a bench can
/// look at the rig from a phone — was told this host has no archive, no declared limits and
/// nothing that can be commanded, while the application behind it had an archive open on disk,
/// seven bands under watch, and a simulator taking setpoints.
/// <para>
/// Each is the same object the shell itself uses, not a copy. A second limit monitor would track
/// its own breaches and could disagree with the banner on screen about whether a rail is outside
/// its band; a second archive handle would answer from a different transaction. The browser is
/// meant to be looking at this application, not at a reconstruction of it.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Points the console at the durable store this shell has open, or at none.</summary>
    private void PublishArchiveToConsole() => _streamingServer.Archive = _archive;

    /// <summary>Points the console at the bands the active profile declares.</summary>
    private void PublishLimitsToConsole() => _streamingServer.Limits = ControlPanel.Limits;

    /// <summary>
    /// Offers the running simulator to the console, or withdraws it.
    /// </summary>
    /// <remarks>
    /// Null while nothing is generating, so <c>/api/control</c> answers that there is nothing to
    /// command rather than accepting a setpoint for an engine that has stopped. Only generated
    /// sources implement this; a shell reading real hardware has nothing to offer here, which is
    /// what the emergency interlock is for and is armed separately and on purpose.
    /// </remarks>
    private void PublishControlToConsole(ProfileSimulatorEngine? engine) =>
        _streamingServer.Control = engine;
}
