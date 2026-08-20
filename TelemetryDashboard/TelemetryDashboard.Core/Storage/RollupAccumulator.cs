using System;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// Running count, min, max, sum and sum-of-squared-deviations for one bucket.
/// </summary>
/// <remarks>
/// Every field is maintained as samples arrive. Nothing here ever re-reads raw data, which is the
/// whole point: at a million samples a second, a rollup that needs a second pass over the raw rows
/// is a rollup that can never catch up.
/// <para>
/// Deviation is tracked as Welford's <c>M2</c> rather than a running sum of squares. Telemetry is
/// typically a large offset with a small wobble on top — a 400 V bus rail moving by millivolts —
/// and <c>sum(x²) - sum(x)²/n</c> on that shape subtracts two nearly equal large numbers, which can
/// return a negative variance. M2 accumulates the deviations themselves, so it stays non-negative.
/// </para>
/// </remarks>
public sealed class RollupAccumulator
{
    /// <summary>Samples folded in. Zero means this bucket holds no measurement at all.</summary>
    public long Count { get; private set; }

    /// <summary>Smallest sample seen, or NaN while <see cref="Count"/> is zero.</summary>
    public double Min { get; private set; } = double.NaN;

    /// <summary>Largest sample seen, or NaN while <see cref="Count"/> is zero.</summary>
    public double Max { get; private set; } = double.NaN;

    /// <summary>Sum of the samples seen. Divided by <see cref="Count"/> this is the mean.</summary>
    public double Sum { get; private set; }

    /// <summary>Running mean, carried for the merge below rather than derived from <see cref="Sum"/>.</summary>
    public double Mean { get; private set; }

    /// <summary>Sum of squared deviations from the mean. Variance is this over <see cref="Count"/>.</summary>
    public double M2 { get; private set; }

    /// <summary>Whether this bucket has anything in it. A bucket that does not must never be stored.</summary>
    public bool HasMeasurement => Count > 0;

    /// <summary>
    /// Folds one sample in. Returns false for NaN, which is discarded.
    /// </summary>
    /// <remarks>
    /// NaN is this project's marker for "no reading", so it is not a value to average — counting it
    /// would either poison every derived figure with NaN or, if coerced, report a sensor that said
    /// nothing as a sensor that said zero. A bucket of nothing but NaN therefore ends with
    /// <see cref="Count"/> at zero and is not written anywhere.
    /// </remarks>
    public bool Add(double value)
    {
        if (double.IsNaN(value)) return false;

        Count++;
        Sum += value;

        double delta = value - Mean;
        Mean += delta / Count;
        M2 += delta * (value - Mean);

        Min = Count == 1 ? value : Math.Min(Min, value);
        Max = Count == 1 ? value : Math.Max(Max, value);
        return true;
    }

    /// <summary>
    /// Folds another accumulator over the same bucket into this one.
    /// </summary>
    /// <remarks>
    /// Chan's parallel variance combination. This is what lets a bucket be built from many
    /// independent batches — or many machines — without the samples ever meeting: each writer
    /// aggregates what it saw, and the partial results merge to the same answer a single pass would
    /// have produced.
    /// </remarks>
    public void Merge(RollupAccumulator other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count == 0) return;

        if (Count == 0)
        {
            Count = other.Count;
            Min = other.Min;
            Max = other.Max;
            Sum = other.Sum;
            Mean = other.Mean;
            M2 = other.M2;
            return;
        }

        long combined = Count + other.Count;
        double delta = other.Mean - Mean;

        M2 += other.M2 + delta * delta * Count * other.Count / combined;
        Mean += delta * other.Count / combined;
        Sum += other.Sum;
        Count = combined;
        Min = Math.Min(Min, other.Min);
        Max = Math.Max(Max, other.Max);
    }

    /// <summary>Standard deviation over the samples in the bucket, treating them as the whole set.</summary>
    public double PopulationStandardDeviation =>
        Count > 0 ? Math.Sqrt(M2 / Count) : double.NaN;

    /// <summary>
    /// Standard deviation with Bessel's correction, or NaN from a single sample.
    /// </summary>
    /// <remarks>
    /// One sample has no spread to estimate, and NaN says so. Returning zero would claim a
    /// perfectly steady channel on the strength of one reading.
    /// </remarks>
    public double SampleStandardDeviation =>
        Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : double.NaN;
}
