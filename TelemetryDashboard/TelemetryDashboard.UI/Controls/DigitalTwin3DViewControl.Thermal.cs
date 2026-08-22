using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// The half of the twin that shows state rather than shape.
/// </summary>
/// <remarks>
/// Geometry alone is a picture; a digital twin is supposed to answer "how is it doing". This takes
/// the temperatures the shell already has, places them where the active profile says those devices
/// sit, and paints the field between them through
/// <see cref="ViewModels.HeatmapInterpolationService"/> — which shipped complete, with a defence of
/// its own interpolation choice, and was constructed by nothing.
/// </remarks>
public partial class DigitalTwin3DViewControl
{
    /// <summary>
    /// Shortest gap between redraws of the field.
    /// </summary>
    /// <remarks>
    /// The shell's telemetry tick runs at 20 Hz. Rebuilding a 625-vertex mesh and its gradient that
    /// often is work nobody can see: a converter's case temperature moves over seconds, and the
    /// readout is being read by a person. Throttling here rather than at the caller keeps the panel
    /// safe to feed from any tick rate a future source arrives at.
    /// </remarks>
    public static readonly TimeSpan ThermalRedrawInterval = TimeSpan.FromMilliseconds(500);

    private readonly TwinThermalField _thermal = new();
    private DateTime _lastThermalDraw = DateTime.MinValue;

    /// <summary>What the thermal readout is showing.</summary>
    public string ThermalSummary => ThermalText.Text;

    /// <summary>Sensors currently placed in the field.</summary>
    public int ThermalSensorCount => _thermal.Readings.Count;

    /// <summary>
    /// Resolves temperatures against the active profile's placements and redraws the field.
    /// </summary>
    /// <param name="profile">Profile in force, which is what knows where each device sits.</param>
    /// <param name="celsiusByNode">Temperature per node id, as the shell measured it.</param>
    /// <remarks>
    /// A reading whose node the profile does not place is dropped rather than put somewhere. There
    /// is no defensible somewhere: a converter drawn at the origin because nobody said where it was
    /// is a wrong answer that looks exactly like a right one, and the readout says how many sensors
    /// it is actually showing so the omission is visible.
    /// </remarks>
    public void UpdateThermal(MonitoringProfile? profile, IReadOnlyDictionary<string, double> celsiusByNode)
    {
        ArgumentNullException.ThrowIfNull(celsiusByNode);

        TwinThermalReading[] readings = profile is null
            ? Array.Empty<TwinThermalReading>()
            : profile.Nodes
                .Where(node => node.Placement is not null && celsiusByNode.ContainsKey(node.Id))
                .Select(node => new TwinThermalReading
                {
                    NodeId = node.Id,
                    Label = node.Label,
                    Placement = node.Placement!,
                    Celsius = celsiusByNode[node.Id]
                })
                .ToArray();

        _thermal.Update(readings);
        ThermalText.Text = _thermal.Summary();

        // Redrawn on a clock, but the text above is not: a number an operator is reading should not
        // wait half a second behind the machine just because the mesh does.
        DateTime now = DateTime.UtcNow;
        if (now - _lastThermalDraw < ThermalRedrawInterval) return;
        _lastThermalDraw = now;

        RedrawThermal();
    }

    /// <summary>Replaces whatever the thermal host was showing.</summary>
    private void RedrawThermal()
    {
        ThermalHost.Children.Clear();
        foreach (Visual3D visual in TwinThermalVisual.Build(_thermal)) ThermalHost.Children.Add(visual);
    }
}
