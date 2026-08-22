using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>One operator-adjustable input of the monitored system.</summary>
/// <remarks>
/// <see cref="Id"/> is the contract between a profile and whatever is being driven: the simulator
/// keeps its setpoints in a dictionary under these ids, so a profile can name channels the built-in
/// model has never heard of without either side needing to know about the other.
/// </remarks>
public sealed class ProfileChannel
{
    /// <summary>Stable key, e.g. <c>ambient.temperature</c>. Not shown to the operator.</summary>
    public required string Id { get; init; }

    /// <summary>What the operator reads beside the slider.</summary>
    public required string Label { get; init; }

    /// <summary>Engineering unit, shown after the value. Empty for a dimensionless channel.</summary>
    public string Unit { get; init; } = string.Empty;

    public double Minimum { get; init; }
    public double Maximum { get; init; }

    /// <summary>Where the channel sits when nothing is wrong. Also the value "reset" returns to.</summary>
    public double Nominal { get; init; }

    /// <summary>
    /// Decimal places for display. A profile owns this because the right precision is a property of
    /// the quantity, not of the widget: a 350-450 V bus reads as 400 V, a 38-54 V rail as 48.1 V.
    /// </summary>
    public int Decimals { get; init; }

    /// <summary>
    /// Set when this channel is the running total of another one instead of a drifting reading.
    /// </summary>
    /// <remarks>
    /// Null for every ordinary channel, which is nearly all of them. See
    /// <see cref="ChannelIntegration"/> for why this is the single exception to the rule that
    /// channels here do not depend on one another.
    /// </remarks>
    public ChannelIntegration? Integrates { get; init; }
}

/// <summary>A named operating situation the operator can put the simulation into.</summary>
public sealed class ProfileScenario
{
    public required string Id { get; init; }

    /// <summary>Button caption. Sentence case, describing the situation rather than the click.</summary>
    public required string Label { get; init; }

    /// <summary>One line of tooltip explaining what the scenario does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Channel values this scenario applies, keyed by <see cref="ProfileChannel.Id"/>.</summary>
    public IReadOnlyDictionary<string, double> Setpoints { get; init; } =
        new Dictionary<string, double>();

    /// <summary>
    /// Optional name of a fault the host injects alongside the setpoints, e.g. <c>DabOvercurrent</c>.
    /// The host resolves it against its own fault model and reports a name it does not recognise
    /// rather than quietly ignoring it. Null or empty clears any active fault.
    /// </summary>
    public string? Fault { get; init; }
}

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
}

/// <summary>
/// The set of nodes, channels and scenarios that describe one monitored system.
/// </summary>
/// <remarks>
/// This type exists because the first ribbon tab used to be one customer's converter, welded into
/// XAML: named buttons for their grid and their DC bus, sliders bounded by their voltages. Anyone
/// else installing the application was looking at somebody else's hardware. A profile is that same
/// information as data, so the tab renders whichever system the operator is actually watching, and
/// a new one arrives as a JSON file rather than as a rebuild.
/// </remarks>
public sealed class MonitoringProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>One line describing what this profile monitors, shown under the picker.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Devices the operator can command. Empty is a legitimate answer, not a missing value: the
    /// control panel then says the profile declares none rather than offering invented ones.
    /// </summary>
    public IReadOnlyList<ProfileNode> Nodes { get; init; } = [];

    public IReadOnlyList<ProfileChannel> Channels { get; init; } = [];

    public IReadOnlyList<ProfileScenario> Scenarios { get; init; } = [];

    /// <summary>
    /// Quantities derived from the channels, declared as <c>id[unit] = expression</c>.
    /// </summary>
    /// <remarks>
    /// A profile is the right place for these because they are a property of the system being
    /// watched rather than of the viewer: efficiency means one thing on a DAB/PSFB chain and
    /// another on a pump, and both of those are decided by whoever described the rig -- not by
    /// whoever opened a browser. Held as text so a profile arriving as JSON can carry them, and
    /// parsed by the one parser in <see cref="Analytics.ComputedChannel"/>.
    /// </remarks>
    public IReadOnlyList<string> Computed { get; init; } = [];

    /// <summary>
    /// Engineering limits on this system's channels, as <c>channel[unit] in lo..hi</c>.
    /// </summary>
    /// <remarks>
    /// Not the same as a channel's <see cref="ProfileChannel.Minimum"/> and
    /// <see cref="ProfileChannel.Maximum"/>, which bound what an operator may set. These bound what
    /// the machine may safely do, and the two differ on purpose: a slider that reaches past a
    /// ceiling is how a fault gets injected.
    /// </remarks>
    public IReadOnlyList<string> Limits { get; init; } = [];

    /// <summary>The name an operator would use for this profile.</summary>
    /// <remarks>
    /// The picker draws its rows through a display path, so the visible text was always right — but
    /// everything that stringifies a profile without one got "TelemetryDashboard.Core.Simulator.
    /// MonitoringProfile", including the name a screen reader announces for the selected row. A
    /// type whose whole job is to be chosen from a list should be able to say what it is called.
    /// </remarks>
    public override string ToString() => DisplayName;
}
