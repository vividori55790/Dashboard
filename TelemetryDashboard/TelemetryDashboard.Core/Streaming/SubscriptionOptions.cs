using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What one client asked to be sent: which channels, how often, and how many points it can draw.
/// </summary>
/// <remarks>
/// Without this every connected browser received every channel at the full ingest rate. At a
/// million samples a second that is roughly 220 MB/s per viewer of JSON the browser must parse
/// and then throw away, because a chart a thousand pixels wide can resolve about two thousand
/// points. A client that asks for four channels at 10 Hz is now sent four channels at 10 Hz.
/// </remarks>
public sealed class SubscriptionOptions
{
    public const int DefaultMaxPoints = 2000;
    public const double DefaultWindowSec = 60.0;
    public const double DefaultMaxUpdateHz = 10.0;

    /// <summary>Ceiling on the served rate. Above this the wire, not the screen, is the limit.</summary>
    public const double MaxSupportedUpdateHz = 100.0;

    /// <summary>Ceiling on points per channel per frame, roughly four 4K displays wide.</summary>
    public const int MaxSupportedPoints = 16_000;

    public SubscriptionOptions(
        IReadOnlyList<string> channels,
        double maxUpdateHz = DefaultMaxUpdateHz,
        int maxPoints = DefaultMaxPoints,
        double windowSec = DefaultWindowSec,
        ReductionMethod method = ReductionMethod.MinMax)
    {
        ArgumentNullException.ThrowIfNull(channels);

        Channels = channels;
        Method = method;
        MaxUpdateHz = Clamp(maxUpdateHz, 0.05, MaxSupportedUpdateHz, DefaultMaxUpdateHz);
        WindowSec = Clamp(windowSec, 0.05, 86_400.0, DefaultWindowSec);

        int floor = SeriesReducer.MinimumPointBudget(method);
        MaxPoints = maxPoints < floor ? floor
                  : maxPoints > MaxSupportedPoints ? MaxSupportedPoints
                  : maxPoints;
    }

    /// <summary>Channels this client wants. Empty means it asked for nothing and is sent nothing.</summary>
    public IReadOnlyList<string> Channels { get; }

    /// <summary>Frames per second this client is willing to receive.</summary>
    public double MaxUpdateHz { get; }

    /// <summary>Points per channel per frame. The server never exceeds it.</summary>
    public int MaxPoints { get; }

    /// <summary>Span of history each frame covers, in seconds.</summary>
    public double WindowSec { get; }

    /// <summary>Reduction applied server-side to meet <see cref="MaxPoints"/>.</summary>
    public ReductionMethod Method { get; }

    /// <summary>Minimum gap between two frames to this client.</summary>
    public double IntervalSec => 1.0 / MaxUpdateHz;

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || value <= 0.0) return fallback;
        return value < min ? min : value > max ? max : value;
    }
}
