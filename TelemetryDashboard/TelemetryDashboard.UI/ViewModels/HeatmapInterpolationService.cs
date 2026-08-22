using System;
using System.Collections.Generic;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Estimates temperature anywhere on the digital twin from a sparse set of sensor readings using
/// inverse-distance weighting, and maps the estimate onto the display gradient.
/// </summary>
/// <remarks>
/// Inverse-distance weighting rather than a fitted surface: sensors on a machine are few and
/// irregularly placed, and IDW is exact at every sensor by construction. The reading an operator
/// can physically walk over and verify is the one that must never be wrong, and a fitted surface
/// will happily disagree with its own input points.
/// </remarks>
public sealed class HeatmapInterpolationService
{
    /// <summary>Squared distance below which a probe counts as sitting on the sensor itself.</summary>
    private const double CoincidenceEpsilon = 1e-9;

    private readonly Dictionary<(double X, double Y, double Z), double> _sensors = new();

    private double _gradientMin;
    private double _gradientMax = 100.0;

    /// <summary>Sensors currently contributing to the field.</summary>
    public int SensorCount => _sensors.Count;

    /// <summary>
    /// Records a reading at a point, replacing any previous reading at the same coordinate.
    /// </summary>
    /// <remarks>
    /// Last write wins rather than averaging. Two entries at one coordinate mean the same physical
    /// sensor reported twice, and averaging a sensor against its own stale value would smear
    /// exactly the step response the operator is watching for. Non-finite input is dropped so a
    /// decode fault cannot poison the whole field.
    /// </remarks>
    public void AddSensor(double x, double y, double z, double temp)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || !double.IsFinite(temp)) return;

        _sensors[(x, y, z)] = temp;
    }

    /// <summary>
    /// Forgets every sensor, so the next field is built only from what is there now.
    /// </summary>
    /// <remarks>
    /// Added when the twin was wired to it. The dictionary is keyed by coordinate, so a live rig
    /// re-reporting the same sensors overwrites them and needs nothing -- but switching profiles
    /// replaces the machine, and without this the previous rig's sensors stayed in the field
    /// forever. The result is not a stale reading, which an operator might notice; it is an
    /// interpolation between two different machines, which looks exactly like a real gradient.
    /// </remarks>
    public void Clear() => _sensors.Clear();

    /// <summary>
    /// Interpolates the temperature at a point, or NaN when no sensor has been registered.
    /// </summary>
    /// <remarks>
    /// Weights accumulate each sensor's deviation from a reference sensor rather than its absolute
    /// temperature. IDW is a convex combination, so a uniform field is guaranteed on paper to
    /// interpolate to its own constant — but summing absolute values and then dividing lets
    /// rounding move the result by an ULP, which paints visible banding across a surface that is
    /// genuinely at one temperature. Deviations from a reference cancel exactly instead.
    /// <para>
    /// A probe landing on a sensor returns that sensor outright, which is both the mathematical
    /// limit of the weighting and the guard against dividing by a zero distance.
    /// </para>
    /// </remarks>
    public double Interpolate(double x, double y, double z)
    {
        if (_sensors.Count == 0) return double.NaN;

        // NaN is a safe "not yet set" marker here because AddSensor rejects non-finite readings.
        double reference = double.NaN;
        double weightSum = 0.0;
        double weightedDeviation = 0.0;

        foreach (KeyValuePair<(double X, double Y, double Z), double> sensor in _sensors)
        {
            double dx = x - sensor.Key.X;
            double dy = y - sensor.Key.Y;
            double dz = z - sensor.Key.Z;
            double distanceSquared = dx * dx + dy * dy + dz * dz;

            if (distanceSquared <= CoincidenceEpsilon) return sensor.Value;

            if (double.IsNaN(reference)) reference = sensor.Value;

            double weight = 1.0 / distanceSquared;
            weightSum += weight;
            weightedDeviation += weight * (sensor.Value - reference);
        }

        return reference + weightedDeviation / weightSum;
    }

    /// <summary>
    /// Sets the temperature span the colour gradient covers.
    /// </summary>
    /// <remarks>
    /// Bounds arriving reversed are ordered rather than rejected: they come from two independent
    /// operator fields, and an inverted gradient would silently paint hot surfaces blue. Non-finite
    /// bounds are ignored so a half-typed value cannot leave the map uncolourable.
    /// </remarks>
    public void SetGradientBounds(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return;

        _gradientMin = Math.Min(min, max);
        _gradientMax = Math.Max(min, max);
    }

    /// <summary>
    /// Maps a temperature onto the blue-through-green-to-red gradient, clamped to the bounds.
    /// </summary>
    /// <remarks>
    /// Always returns a colour, never null. An out-of-range or unreadable reading still has to
    /// paint something, and a hole in the mesh is far harder for an operator to interpret than a
    /// surface pinned at an end stop. A degenerate span collapses to the cold end rather than
    /// dividing by zero.
    /// </remarks>
    public HeatColor GetColorForTemperature(double t)
    {
        double span = _gradientMax - _gradientMin;
        double ratio = span > 0.0 && double.IsFinite(t)
            ? Math.Clamp((t - _gradientMin) / span, 0.0, 1.0)
            : 0.0;

        byte red = (byte)Math.Round(255.0 * ratio);
        byte blue = (byte)Math.Round(255.0 * (1.0 - ratio));

        // Green peaks at mid-scale so the ramp passes through cyan and yellow instead of a muddy
        // purple, which is what keeps mid-range differences readable at a glance.
        byte green = (byte)Math.Round(255.0 * (1.0 - Math.Abs(2.0 * ratio - 1.0)));

        return new HeatColor(red, green, blue);
    }
}
