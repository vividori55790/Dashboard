namespace TelemetryDashboard.Core.Simulator;

/// <summary>Where a device physically sits on the rig, in the twin's own coordinates.</summary>
/// <remarks>
/// Declared by the profile rather than written into the viewport, for the reason
/// <see cref="ProfileNode"/> exists at all: the control panel once named two of one customer's
/// devices in XAML, so every other installation was offered power switches for hardware it does not
/// own. Geometry is the same kind of fact. A rig with its converters stacked and a rig with them
/// side by side are different machines, and only the profile knows which one is in front of the
/// operator.
/// <para>
/// The units are the twin's, not millimetres or metres. Nothing here converts anything: the
/// viewport normalises whatever it is given into a fixed box, so a profile written in centimetres
/// and one written in inches both frame correctly, and only the <em>relative</em> spacing carries
/// meaning. Saying so is the point — a placement is a layout, not a measurement.
/// </para>
/// </remarks>
public sealed class SensorPlacement
{
    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Z { get; init; }
}
