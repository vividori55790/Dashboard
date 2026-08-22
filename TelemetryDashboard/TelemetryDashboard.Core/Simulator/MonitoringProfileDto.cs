using System.Collections.Generic;

namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// The literal shape of <c>profiles.json</c>.
/// </summary>
/// <remarks>
/// Every member is nullable and nothing is required, because a file written by hand is expected to
/// be wrong sometimes and the job of this layer is to notice rather than to throw. Validation lives
/// in <see cref="MonitoringProfileReader"/>; these types only describe what may appear on disk.
/// </remarks>
internal sealed class ProfileFileDto
{
    public List<ProfileDto>? Profiles { get; init; }
}

internal sealed class ProfileDto
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Summary { get; init; }
    public List<NodeDto>? Nodes { get; init; }
    public List<ChannelDto>? Channels { get; init; }
    public List<ScenarioDto>? Scenarios { get; init; }
    public List<string>? Computed { get; init; }
    public List<string>? Limits { get; init; }
}

internal sealed class NodeDto
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }

    /// <summary>Optional physical position, as {"x": .., "y": .., "z": ..}.</summary>
    public PlacementDto? Placement { get; init; }
}

/// <summary>A node's position on the rig, in the twin's own coordinates.</summary>
/// <remarks>
/// All three are nullable so a half-written placement can be refused by name. Defaulting a missing
/// axis to zero would put a converter on the floor at the origin and draw it there confidently.
/// </remarks>
internal sealed class PlacementDto
{
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
}

internal sealed class ChannelDto
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Unit { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Nominal { get; init; }
    public int Decimals { get; init; }

    /// <summary>Channel this one accumulates, when it is a running total rather than a reading.</summary>
    public string? Integrates { get; init; }

    /// <summary>Movement per second per unit of <see cref="Integrates"/>. Ignored without it.</summary>
    public double IntegralPerSecond { get; init; }
}

internal sealed class ScenarioDto
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? Fault { get; init; }
    public Dictionary<string, double>? Setpoints { get; init; }
}
