namespace TelemetryDashboard.Core.Simulator;

/// <summary>One addressable device the operator can command, e.g. a port or a converter board.</summary>
/// <remarks>
/// A node is separate from a channel because the two answer different questions: a channel is a
/// quantity being watched, a node is a box that can be switched on and off. The control panel used
/// to name two of them in XAML — one customer's battery converter and their server rail — so every
/// other installation was offered power switches for hardware it does not own. A profile that
/// declares no nodes gets no switches and is told so, which is the honest answer when the
/// application has not been told what is out there.
/// </remarks>
public sealed class ProfileNode
{
    /// <summary>Stable key sent in the command, e.g. <c>COM3</c>. Not prose.</summary>
    public required string Id { get; init; }

    /// <summary>Button caption. Sentence case, naming the device rather than the click.</summary>
    public required string Label { get; init; }

    /// <summary>One line of tooltip. Empty when the label already says enough.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Where this device sits on the rig, or null when the profile does not say.
    /// </summary>
    /// <remarks>
    /// Null for most profiles, and the digital twin says so rather than guessing: a machine drawn
    /// from invented coordinates is a picture of nothing, and it is worse than no picture because
    /// it looks like one. See <see cref="SensorPlacement"/> for why this is the profile's business.
    /// </remarks>
    public SensorPlacement? Placement { get; init; }
}
