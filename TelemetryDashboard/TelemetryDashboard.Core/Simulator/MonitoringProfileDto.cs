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
    public List<ChannelDto>? Channels { get; init; }
    public List<ScenarioDto>? Scenarios { get; init; }
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
}

internal sealed class ScenarioDto
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? Fault { get; init; }
    public Dictionary<string, double>? Setpoints { get; init; }
}
