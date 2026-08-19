using System;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Two draggable measurement cursors on the scope and the delta readout between them.
/// </summary>
/// <remarks>
/// The service holds cursor positions only, never the trace. A cursor that owned a copy of the
/// samples would have to be invalidated on every incoming packet, and the readout would flicker
/// while the operator is trying to read it.
/// </remarks>
public sealed class DeltaCursorService
{
    private bool _hasBounds;
    private double _boundsMin;
    private double _boundsMax;

    private bool _hasCursor1;
    private bool _hasCursor2;

    /// <summary>Time coordinate of the first cursor.</summary>
    public double Cursor1Time { get; private set; }

    /// <summary>Value coordinate of the first cursor.</summary>
    public double Cursor1Value { get; private set; }

    /// <summary>Time coordinate of the second cursor.</summary>
    public double Cursor2Time { get; private set; }

    /// <summary>Value coordinate of the second cursor.</summary>
    public double Cursor2Value { get; private set; }

    /// <summary>
    /// True once both cursors are placed, which is when a delta means anything.
    /// </summary>
    /// <remarks>
    /// The HUD reads this to decide between showing a measurement and showing "no data". One
    /// cursor down is a half-finished gesture, and a delta computed against an unset second cursor
    /// would read as a real measurement against time zero.
    /// </remarks>
    public bool HasValidMeasurement => _hasCursor1 && _hasCursor2;

    /// <summary>Signed time between the cursors, second minus first.</summary>
    /// <remarks>
    /// Signed rather than absolute because the sign carries the drag direction, which is how the
    /// operator tells a lead from a lag when comparing two channels.
    /// </remarks>
    public double DeltaTime => HasValidMeasurement ? Cursor2Time - Cursor1Time : 0.0;

    /// <summary>Signed value difference between the cursors, second minus first.</summary>
    public double DeltaValue => HasValidMeasurement ? Cursor2Value - Cursor1Value : 0.0;

    /// <summary>Magnitude of <see cref="DeltaTime"/>, for readouts that show an interval.</summary>
    public double AbsoluteDeltaTime => Math.Abs(DeltaTime);

    /// <summary>Places the first cursor, clamping its time into the visible data range.</summary>
    public void SetCursor1(double time, double value)
    {
        if (!IsPlaceable(time, value)) return;

        Cursor1Time = ClampToBounds(time);
        Cursor1Value = value;
        _hasCursor1 = true;
    }

    /// <summary>Places the second cursor, clamping its time into the visible data range.</summary>
    public void SetCursor2(double time, double value)
    {
        if (!IsPlaceable(time, value)) return;

        Cursor2Time = ClampToBounds(time);
        Cursor2Value = value;
        _hasCursor2 = true;
    }

    /// <summary>
    /// Declares the time range covered by the plotted data.
    /// </summary>
    /// <remarks>
    /// Cursors placed afterwards are clamped into this range. A drag that runs off the plot edge
    /// would otherwise report an interval over empty canvas as though data existed there, which is
    /// the one reading a measurement tool must never produce. Bounds arriving reversed are ordered
    /// rather than rejected, since they come from whichever plot edge the caller sampled first.
    /// </remarks>
    public void SetDataBounds(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return;

        _boundsMin = Math.Min(min, max);
        _boundsMax = Math.Max(min, max);
        _hasBounds = true;
    }

    /// <summary>
    /// Retracts both cursors and forgets the data range.
    /// </summary>
    /// <remarks>
    /// Called when the trace is cleared or a new capture starts. Leaving the cursors placed would
    /// keep the HUD showing a measurement taken from a recording that is no longer on screen.
    /// </remarks>
    public void ClearData()
    {
        _hasCursor1 = false;
        _hasCursor2 = false;
        _hasBounds = false;
        Cursor1Time = 0.0;
        Cursor1Value = 0.0;
        Cursor2Time = 0.0;
        Cursor2Value = 0.0;
    }

    /// <summary>
    /// A cursor is only placeable at finite coordinates.
    /// </summary>
    /// <remarks>
    /// Rejecting the move outright leaves the previous position intact. Accepting a NaN would
    /// poison every derived readout at once, and a frozen cursor is far easier to diagnose than a
    /// HUD that has quietly gone blank.
    /// </remarks>
    private static bool IsPlaceable(double time, double value) =>
        double.IsFinite(time) && double.IsFinite(value);

    private double ClampToBounds(double time) =>
        _hasBounds ? Math.Clamp(time, _boundsMin, _boundsMax) : time;
}
