using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Fixed-window rolling statistics for a single telemetry channel: mean, standard deviation,
/// z-score and least-squares trend slope.
/// </summary>
/// <remarks>
/// Backed by a pre-allocated ring buffer, so ingesting a sample allocates nothing — the previous
/// implementation called <c>Queue.ToArray()</c> plus several LINQ projections on every packet,
/// which at burst rates produced far more garbage than telemetry.
/// <para>
/// The z-score baseline deliberately <em>excludes</em> the sample under test. Including it lets a
/// large spike inflate the standard deviation it is being measured against and partially mask
/// itself — the larger the excursion, the more it hides.
/// </para>
/// </remarks>
public sealed class RollingChannelStatistics
{
    private readonly double[] _window;
    private int _head;   // index of the oldest retained sample
    private int _count;

    public RollingChannelStatistics(int windowSize)
    {
        if (windowSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window must hold at least two samples.");
        }
        _window = new double[windowSize];
    }

    public int Capacity => _window.Length;

    public int Count => _count;

    /// <summary>Most recently added sample, or NaN when empty.</summary>
    public double Latest => _count == 0 ? double.NaN : this[_count - 1];

    /// <summary>Mean of the baseline (all retained samples except the newest).</summary>
    public double Mean => ComputeMean(BaselineCount);

    /// <summary>Sample standard deviation of the baseline.</summary>
    public double StandardDeviation
    {
        get
        {
            int n = BaselineCount;
            if (n < 2) return 0.0;

            double mean = ComputeMean(n);
            double sumSquaredDeviation = 0.0;
            for (int i = 0; i < n; i++)
            {
                double deviation = this[i] - mean;
                sumSquaredDeviation += deviation * deviation;
            }

            return Math.Sqrt(sumSquaredDeviation / (n - 1));
        }
    }

    /// <summary>Samples forming the comparison baseline: everything but the newest sample.</summary>
    private int BaselineCount => _count > 1 ? _count - 1 : _count;

    /// <summary>The i-th oldest retained sample.</summary>
    public double this[int index] => _window[(_head + index) % _window.Length];

    public void Add(double value)
    {
        int tail = (_head + _count) % _window.Length;
        _window[tail] = value;

        if (_count < _window.Length)
        {
            _count++;
        }
        else
        {
            _head = (_head + 1) % _window.Length; // overwrite the oldest sample
        }
    }

    /// <summary>
    /// Absolute z-score of <paramref name="value"/> against the baseline.
    /// Returns 0 when the baseline has no measurable variance, since a channel that has never
    /// moved carries no information about what an excursion would look like.
    /// </summary>
    public double ZScoreOf(double value)
    {
        double standardDeviation = StandardDeviation;
        if (standardDeviation <= 1e-9 || double.IsNaN(standardDeviation)) return 0.0;

        return Math.Abs(value - Mean) / standardDeviation;
    }

    /// <summary>
    /// Least-squares slope over the retained window, in units per sample.
    /// Returns 0 when fewer than two samples are held.
    /// </summary>
    public double TrendSlopePerSample()
    {
        int n = _count;
        if (n < 2) return 0.0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < n; i++)
        {
            double y = this[i];
            sumX += i;
            sumY += y;
            sumXY += i * y;
            sumXX += (double)i * i;
        }

        double denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-12) return 0.0;

        return (n * sumXY - sumX * sumY) / denominator;
    }

    public void Clear()
    {
        Array.Clear(_window, 0, _window.Length);
        _head = 0;
        _count = 0;
    }

    private double ComputeMean(int n)
    {
        if (n <= 0) return 0.0;

        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            sum += this[i];
        }
        return sum / n;
    }
}
