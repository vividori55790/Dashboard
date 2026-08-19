using System;
using System.Collections.Generic;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Backing state for the 2D scope: per-channel sample buffers and the Y-axis window the plot
/// control draws against.
/// </summary>
/// <remarks>
/// Every member is guarded by one lock. Samples arrive on serial and parser threads while the
/// dispatcher clears and re-reads the same buffers; an unguarded list resized mid-enumeration
/// either throws or reports a torn count, and both look like data loss from the outside.
/// No change notifications are raised either: notifying from an ingestion thread would marshal one
/// dispatcher callback per packet and starve the render loop at burst rates, so the scope control
/// pulls a snapshot on its own redraw tick.
/// </remarks>
public sealed class ScopeViewModel
{
    /// <summary>Y window used when no finite sample exists to scale against.</summary>
    private const double DefaultYMin = -1.0;
    private const double DefaultYMax = 1.0;

    /// <summary>Half-height given a perfectly flat trace so the axis keeps a non-zero range.</summary>
    private const double FlatTracePadding = 1.0;

    private readonly object _gate = new();
    private readonly Dictionary<string, List<double>> _channels = new(StringComparer.Ordinal);

    private double _yMin = DefaultYMin;
    private double _yMax = DefaultYMax;
    private int _totalPoints;

    /// <summary>Lower edge of the rendered Y axis.</summary>
    public double YMin { get { lock (_gate) { return _yMin; } } }

    /// <summary>Upper edge of the rendered Y axis.</summary>
    public double YMax { get { lock (_gate) { return _yMax; } } }

    /// <summary>True when no channel holds a sample, so the plot area has nothing to draw.</summary>
    /// <remarks>
    /// Answered from a running total rather than by walking the channels. The redraw tick asks
    /// every frame, and an O(channels) scan taken under the ingestion lock would put back-pressure
    /// on the serial threads for a question with a one-word answer.
    /// </remarks>
    public bool IsPlotAreaEmpty { get { lock (_gate) { return _totalPoints == 0; } } }

    /// <summary>
    /// Appends samples to a channel, creating the channel on first use. Non-finite samples are
    /// stored rather than dropped: the buffer records what the device actually sent, and silently
    /// shortening it breaks the index alignment between a channel's samples and the timestamps
    /// held beside them. Rendering and scaling filter them out instead.
    /// </summary>
    public void AddDataPoints(string channel, double[] values)
    {
        if (string.IsNullOrEmpty(channel) || values is null || values.Length == 0) return;

        lock (_gate)
        {
            if (!_channels.TryGetValue(channel, out List<double>? points))
            {
                points = new List<double>(values.Length);
                _channels[channel] = points;
            }
            points.AddRange(values);
            _totalPoints += values.Length;
        }
    }

    /// <summary>
    /// Samples on <paramref name="channel"/> that can actually be plotted. Its gap from
    /// <see cref="GetTotalPointCount"/> counts NaN and infinite readings, so the pair doubles as a
    /// decode-health indicator.
    /// </summary>
    public int GetValidPointCount(string channel)
    {
        lock (_gate)
        {
            if (channel is null || !_channels.TryGetValue(channel, out List<double>? points)) return 0;

            int valid = 0;
            foreach (double value in points)
            {
                if (double.IsFinite(value)) valid++;
            }
            return valid;
        }
    }

    /// <summary>Samples held for <paramref name="channel"/>, including unplottable ones.</summary>
    public int GetTotalPointCount(string channel)
    {
        lock (_gate)
        {
            return channel is not null && _channels.TryGetValue(channel, out List<double>? points)
                ? points.Count
                : 0;
        }
    }

    /// <summary>
    /// Drops every buffered sample on every channel and restores the default Y window. Channel
    /// entries themselves survive, so a re-arm keeps each series' identity: colour, label and axis
    /// assignment all hang off the channel name downstream.
    /// </summary>
    public void ClearPoints()
    {
        lock (_gate)
        {
            foreach (List<double> points in _channels.Values)
            {
                points.Clear();
            }
            _totalPoints = 0;
            _yMin = DefaultYMin;
            _yMax = DefaultYMax;
        }
    }

    /// <summary>
    /// Fits the Y axis to the finite samples buffered. Infinities are excluded because an infinite
    /// bound cannot be rendered at all — the axis renderer divides by the range to place ticks, so
    /// one blanks the plot rather than distorting it. Nothing finite falls back to the defaults.
    /// </summary>
    public void AutoScaleYAxis()
    {
        lock (_gate)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            foreach (List<double> points in _channels.Values)
            {
                foreach (double value in points)
                {
                    if (!double.IsFinite(value)) continue;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
            }

            bool scalable = double.IsFinite(min) && double.IsFinite(max);
            double padding = scalable && max - min < double.Epsilon ? FlatTracePadding : 0.0;

            _yMin = scalable ? min - padding : DefaultYMin;
            _yMax = scalable ? max + padding : DefaultYMax;
        }
    }
}
