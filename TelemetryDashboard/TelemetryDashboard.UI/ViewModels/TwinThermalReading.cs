using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>One device's temperature, at the place the profile says that device sits.</summary>
/// <remarks>
/// Carries the label as well as the id because the twin is looked at, not queried: an operator
/// reading "COM4 92.0 °C" has to remember which board that is, and "PSFB 서버 레일" does not need
/// remembering. The id stays for the readout to stay unambiguous when two rigs use the same labels.
/// </remarks>
public sealed record TwinThermalReading
{
    /// <summary>Node id from the profile, e.g. <c>COM3</c>.</summary>
    public required string NodeId { get; init; }

    /// <summary>Operator-facing name of the device.</summary>
    public required string Label { get; init; }

    /// <summary>Where the profile says it sits.</summary>
    public required SensorPlacement Placement { get; init; }

    /// <summary>Its temperature in degrees Celsius.</summary>
    public required double Celsius { get; init; }
}
