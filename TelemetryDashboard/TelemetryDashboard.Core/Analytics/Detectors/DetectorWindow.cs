using System;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// A fixed-size ring of recent samples, with the newest one separable from the baseline.
/// </summary>
/// <remarks>
/// The same baseline rule <see cref="RollingChannelStatistics"/> follows, restated because the
/// robust detectors need indexed access rather than a mean: the sample under test is excluded from
/// the baseline it is measured against. Include it and a large excursion widens the very spread it
/// is being compared to, so the bigger the fault the better it hides.
///
/// <para>Allocation-free after construction, including the scratch buffer the median sort needs.
/// This is per-channel state on the ingest path, where a per-sample array would produce more
/// garbage than telemetry.</para>
/// </remarks>
public sealed class DetectorWindow
{
    private readonly double[] _samples;
    private readonly double[] _scratch;
    private int _head;
    private int _count;

    public DetectorWindow(int capacity)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "A window must hold at least two samples.");

        _samples = new double[capacity];
        _scratch = new double[capacity];
    }

    /// <summary>Configured window size.</summary>
    public int Capacity => _samples.Length;

    /// <summary>Samples currently held.</summary>
    public int Count => _count;

    /// <summary>Samples forming the comparison baseline: everything but the newest.</summary>
    public int BaselineCount => _count > 1 ? _count - 1 : 0;

    /// <summary>Most recently added sample, or NaN when empty.</summary>
    public double Latest => _count == 0 ? double.NaN : this[_count - 1];

    /// <summary>Share of the configured window that is populated, 0 to 1.</summary>
    public double Fill => (double)_count / _samples.Length;

    /// <summary>The i-th oldest retained sample.</summary>
    public double this[int index] => _samples[(_head + index) % _samples.Length];

    public void Add(double value)
    {
        _samples[(_head + _count) % _samples.Length] = value;

        if (_count < _samples.Length) _count++;
        else _head = (_head + 1) % _samples.Length;
    }

    public void Clear()
    {
        Array.Clear(_samples, 0, _samples.Length);
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Median of the baseline, and the median absolute deviation about it.
    /// </summary>
    /// <remarks>
    /// Returns false when fewer than two baseline samples are held, because a median of one point
    /// is that point and a MAD about it is zero — a scale of nothing, which would score every
    /// subsequent sample as infinitely deviant.
    /// <para>
    /// <paramref name="mad"/> can legitimately be zero on a baseline where more than half the
    /// samples are identical. That is a real state of the data, not an error, and the caller is the
    /// one that has to decide it cannot measure against it.
    /// </para>
    /// </remarks>
    public bool TryBaselineMedian(out double median, out double mad)
    {
        median = 0;
        mad = 0;

        int n = BaselineCount;
        if (n < 2) return false;

        for (int i = 0; i < n; i++) _scratch[i] = this[i];
        median = MedianOfScratch(n);

        for (int i = 0; i < n; i++) _scratch[i] = Math.Abs(this[i] - median);
        mad = MedianOfScratch(n);

        return true;
    }

    /// <summary>Mean absolute deviation of the baseline about <paramref name="centre"/>.</summary>
    /// <remarks>
    /// The fallback scale when MAD collapses to zero. Still entirely derived from the data — it is
    /// a less resistant estimator, not an invented one — and the caller records which of the two it
    /// used, because a robust score computed from a non-robust scale is not the same measurement.
    /// </remarks>
    public double BaselineMeanAbsoluteDeviation(double centre)
    {
        int n = BaselineCount;
        if (n < 1) return 0.0;

        double total = 0.0;
        for (int i = 0; i < n; i++) total += Math.Abs(this[i] - centre);
        return total / n;
    }

    /// <summary>Median of the first <paramref name="n"/> scratch entries, sorting them in place.</summary>
    private double MedianOfScratch(int n)
    {
        Array.Sort(_scratch, 0, n);
        return (n & 1) == 1 ? _scratch[n / 2] : (_scratch[n / 2 - 1] + _scratch[n / 2]) / 2.0;
    }
}
