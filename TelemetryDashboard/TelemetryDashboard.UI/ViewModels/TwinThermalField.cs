using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// The temperature field under the twin: sampled from the sensors, read at any point on a plane.
/// </summary>
/// <remarks>
/// <see cref="HeatmapInterpolationService"/> does the estimating and the colour mapping and was
/// constructed by nothing. It is inverse-distance weighted rather than a fitted surface, which
/// matters for exactly one reason and it is the reason this can be trusted: IDW is exact at every
/// sensor by construction. The reading an operator can walk over and check against a hand-held
/// probe is the one that must never be wrong, and a fitted surface will happily disagree with its
/// own input points.
/// <para>
/// This turns that into something drawable — a grid of sample points across the machine's footprint
/// with a normalised temperature at each — while owning no renderer type of its own. The viewport
/// turns the normalised values into texture coordinates; nothing here knows that.
/// </para>
/// </remarks>
public sealed class TwinThermalField
{
    /// <summary>Divisions along each axis of the plan: 24 gives a 25 x 25 grid of 625 samples.</summary>
    public const int GridResolution = 24;

    /// <summary>How far past the outermost sensor the plan extends, as a fraction of its span.</summary>
    /// <remarks>
    /// Without the margin the plan stops exactly at the sensors and the hottest part of a board
    /// sits on the boundary, which reads as the field being cut off rather than as the board being
    /// at the edge of what is measured. Extrapolating further would be inventing readings.
    /// </remarks>
    public const double Margin = 0.35;

    private readonly HeatmapInterpolationService _heat = new();

    /// <summary>Readings the field was last built from.</summary>
    public IReadOnlyList<TwinThermalReading> Readings { get; private set; } = Array.Empty<TwinThermalReading>();

    /// <summary>Coldest and hottest reading, or NaN when there are none.</summary>
    public (double Min, double Max) Bounds { get; private set; } = (double.NaN, double.NaN);

    /// <summary>The hottest device, or null when nothing was placed.</summary>
    public TwinThermalReading? Hottest { get; private set; }

    /// <summary>Whether there is a field to draw.</summary>
    public bool HasField => Readings.Count > 0;

    /// <summary>
    /// Rebuilds the field from <paramref name="readings"/>.
    /// </summary>
    /// <remarks>
    /// The gradient is stretched to the readings actually present rather than pinned to a fixed
    /// span. A fixed 0-100 range paints a rig running between 40 and 44 degrees in one flat colour,
    /// which is the case where the operator most wants to see which board is the warmer one. A span
    /// that has collapsed -- every sensor at the same temperature -- is widened by a degree, because
    /// the honest picture of a uniform field is uniform, not a division by zero.
    /// </remarks>
    public void Update(IReadOnlyList<TwinThermalReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        TwinThermalReading[] usable = readings
            .Where(r => double.IsFinite(r.Celsius))
            .ToArray();

        _heat.Clear();
        Readings = usable;
        Hottest = null;
        Bounds = (double.NaN, double.NaN);
        if (usable.Length == 0) return;

        foreach (TwinThermalReading reading in usable)
        {
            _heat.AddSensor(reading.Placement.X, reading.Placement.Y, reading.Placement.Z, reading.Celsius);
        }

        double min = usable.Min(r => r.Celsius);
        double max = usable.Max(r => r.Celsius);
        if (max - min < 1.0) max = min + 1.0;

        Bounds = (min, max);
        _heat.SetGradientBounds(min, max);
        Hottest = usable.OrderByDescending(r => r.Celsius).First();
    }

    /// <summary>Estimated temperature at a point.</summary>
    public double At(double x, double y, double z) => _heat.Interpolate(x, y, z);

    /// <summary>Where 0 is the coldest of the gradient and 1 the hottest.</summary>
    public double NormalisedAt(double x, double y, double z)
    {
        (double min, double max) = Bounds;
        if (!double.IsFinite(min) || max <= min) return 0.0;

        double value = At(x, y, z);
        return double.IsFinite(value) ? Math.Clamp((value - min) / (max - min), 0.0, 1.0) : 0.0;
    }

    /// <summary>The gradient ramp as <paramref name="stops"/> colours, cold end first.</summary>
    /// <remarks>
    /// Sampled from the same service that colours the sensors, so the legend and the markers cannot
    /// disagree about what a temperature looks like.
    /// </remarks>
    public IReadOnlyList<HeatColor> Ramp(int stops = 16)
    {
        (double min, double max) = Bounds;
        if (!double.IsFinite(min)) return Array.Empty<HeatColor>();

        var ramp = new List<HeatColor>(stops);
        for (int i = 0; i < stops; i++)
        {
            ramp.Add(_heat.GetColorForTemperature(min + (max - min) * i / (stops - 1.0)));
        }
        return ramp;
    }

    /// <summary>Colour of one device's own reading.</summary>
    public HeatColor ColorOf(TwinThermalReading reading) => _heat.GetColorForTemperature(reading.Celsius);

    /// <summary>Extent of the plan: the sensors' bounding square, widened by <see cref="Margin"/>.</summary>
    public (double MinX, double MaxX, double MinZ, double MaxZ, double Y) Footprint()
    {
        if (Readings.Count == 0) return (-1, 1, -1, 1, 0);

        double minX = Readings.Min(r => r.Placement.X), maxX = Readings.Max(r => r.Placement.X);
        double minZ = Readings.Min(r => r.Placement.Z), maxZ = Readings.Max(r => r.Placement.Z);
        double padX = Math.Max((maxX - minX) * Margin, 1.0);
        double padZ = Math.Max((maxZ - minZ) * Margin, 1.0);

        return (minX - padX, maxX + padX, minZ - padZ, maxZ + padZ, Readings.Min(r => r.Placement.Y));
    }

    /// <summary>One line for the toolbar: how many sensors, over what span, and which is hottest.</summary>
    public string Summary()
    {
        if (Hottest is null) return "온도 센서 없음 — 프로파일이 노드 위치를 선언하지 않았습니다";

        (double min, double max) = Bounds;
        return string.Create(CultureInfo.InvariantCulture,
            $"{Readings.Count} sensors · {min:0.0}–{max:0.0} °C · hottest {Hottest.Label} {Hottest.Celsius:0.0} °C");
    }
}
