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

/// <summary>
/// The set of channels and scenarios that describe one monitored system.
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

    public IReadOnlyList<ProfileChannel> Channels { get; init; } = [];

    public IReadOnlyList<ProfileScenario> Scenarios { get; init; } = [];
}
