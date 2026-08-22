using System;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// The arithmetic mean of the last N values, updated in constant time.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Analytics.RollingChannelStatistics"/>, which this project already
/// has and which the mean-reversion strategy does use. That class excludes the newest sample from
/// its baseline on purpose — a spike must not be allowed to inflate the deviation it is being
/// measured against — and so its <c>Mean</c> is a mean of the previous N-1 samples. Correct for a
/// z-score; a one-bar-lagged average for a crossover, which would shift every signal by a session
/// and change the result while looking entirely reasonable.
/// <para>
/// The running sum is refreshed from the ring whenever the window fills, rather than only added to
/// and subtracted from. Over ten thousand bars the incremental sum drifts in the last few
/// significant digits, and two averages drifting differently is exactly how a crossover fires on
/// arithmetic instead of on prices.
/// </para>
/// </remarks>
public sealed class MovingAverage
{
    private readonly double[] _window;
    private int _next;
    private int _count;
    private double _sum;

    /// <summary>Builds an average over <paramref name="period"/> values.</summary>
    public MovingAverage(int period)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period), "A period needs at least one value.");
        _window = new double[period];
    }

    /// <summary>How many values the window spans once full.</summary>
    public int Period => _window.Length;

    /// <summary>Whether enough values have arrived for the average to span its whole period.</summary>
    public bool IsReady => _count >= _window.Length;

    /// <summary>Mean of the values held, or NaN before any arrived.</summary>
    public double Value => _count == 0 ? double.NaN : _sum / _count;

    /// <summary>Adds one value, evicting the oldest once the window is full.</summary>
    public void Add(double value)
    {
        if (_count == _window.Length)
        {
            _window[_next] = value;
            _next = (_next + 1) % _window.Length;

            _sum = 0;
            for (int i = 0; i < _window.Length; i++) _sum += _window[i];
            return;
        }

        _window[_next] = value;
        _next = (_next + 1) % _window.Length;
        _count++;
        _sum += value;
    }

    /// <summary>Empties the window.</summary>
    public void Reset()
    {
        Array.Clear(_window, 0, _window.Length);
        _next = 0;
        _count = 0;
        _sum = 0;
    }
}
