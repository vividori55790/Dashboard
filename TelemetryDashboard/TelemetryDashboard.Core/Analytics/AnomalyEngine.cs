using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Statistical anomaly detection over a batch of samples.
/// </summary>
/// <remarks>
/// The smoothing this used to implement inline now lives in <see cref="ExponentialAverage"/>, which
/// was the half of this class worth having: it was the only exponentially weighted average in the
/// codebase, it shared nothing with the batch evaluator below -- separate fields, separate methods,
/// no interaction -- and nothing constructed this class, so it was unreachable. <c>DriftMonitor</c>
/// is built on it, and the methods here delegate so there is one implementation rather than two.
/// <para>
/// All statistics are delegated to <see cref="RollingChannelStatistics"/> so the engine and the
/// live per-channel path agree by construction. Two detectors that compute their own mean and
/// sigma eventually disagree at the boundary, and the operator is left with two numbers for the
/// same channel and no way to choose between them.
/// <para>
/// The file lives under <c>Analytics/</c> with the rest of the maths but publishes into the
/// <c>Core.Services</c> namespace alongside the other service-layer contracts.
/// </para>
/// </remarks>
public sealed class AnomalyEngine
{
    /// <summary>Deviation, in standard deviations, at which a sample is called an anomaly.</summary>
    private const double AnomalyThresholdSigma = 3.0;

    /// <summary>
    /// Usable samples required before the engine will issue a verdict.
    /// </summary>
    /// <remarks>
    /// Below this a single reading dominates the mean it is being measured against, so the engine
    /// would largely be reporting on its own arithmetic. Declining to answer is more useful than a
    /// confident number derived from three points.
    /// </remarks>
    private const int MinimumUsableSamples = 5;

    /// <summary>Sigma below which a baseline is treated as having no scale at all.</summary>
    /// <remarks>Mirrors the guard inside <see cref="RollingChannelStatistics.ZScoreOf"/>.</remarks>
    private const double DegenerateSigma = 1e-9;

    private readonly ExponentialAverage _ewma = new();
    private double _ewmaAlpha = 0.3;

    /// <summary>Current exponentially weighted moving average, or NaN before the first update.</summary>
    public double CurrentEwma => _ewma.Value;

    /// <summary>
    /// Judges the newest sample of <paramref name="samples"/> against the ones before it.
    /// </summary>
    /// <remarks>
    /// Non-finite readings are dropped rather than treated as zero. A dropped frame is missing
    /// data; scoring it as zero invents an excursion the device never reported.
    /// </remarks>
    public AnomalyEvaluation Evaluate(double[]? samples)
    {
        List<double> usable = CollectFiniteSamples(samples);

        if (usable.Count < MinimumUsableSamples)
        {
            return new AnomalyEvaluation(
                false,
                $"Insufficient data: {usable.Count} usable samples, {MinimumUsableSamples} required.",
                0.0,
                usable.Count);
        }

        RollingChannelStatistics baseline = new(usable.Count);
        foreach (double sample in usable)
        {
            baseline.Add(sample);
        }

        double latest = baseline.Latest;
        double sigmas = baseline.StandardDeviation > DegenerateSigma
            ? baseline.ZScoreOf(latest)
            : SigmasAgainstWidenedBaseline(usable, latest);

        bool isAnomaly = sigmas > AnomalyThresholdSigma;
        return new AnomalyEvaluation(isAnomaly, DescribeVerdict(isAnomaly, latest, sigmas), sigmas, usable.Count);
    }

    /// <summary>Sets the EWMA smoothing factor, clamped to [0,1].</summary>
    /// <remarks>
    /// One tracks the raw signal with no memory, zero freezes the average at its seed. Both are
    /// legitimate operator choices rather than mistakes — alpha of one is how a raw channel gets
    /// watched through the same widget — so out-of-range input is clamped, not rejected.
    /// </remarks>
    public void SetEwmaAlpha(double alpha)
    {
        if (!double.IsFinite(alpha)) return;

        _ewmaAlpha = Math.Clamp(alpha, 0.0, 1.0);
    }

    /// <summary>Folds one sample into the EWMA and returns the updated average.</summary>
    /// <remarks>
    /// The first sample seeds the average outright. Starting from zero would otherwise inject a
    /// ramp from zero up to the channel's operating point that looks exactly like a real transient.
    /// </remarks>
    public double UpdateEwma(double value) => _ewma.Update(value, _ewmaAlpha);

    /// <summary>
    /// Re-scores the newest sample against a baseline that includes it.
    /// </summary>
    /// <remarks>
    /// The primary baseline deliberately excludes the sample under test, which is the sound way to
    /// measure it — but when that baseline is perfectly flat there is no scale to divide by, and a
    /// step out of a flat line, the most obvious fault there is, would score zero. Re-presenting
    /// the sample so the baseline spans the whole series restores a finite spread that still comes
    /// entirely from the data. A series that never moved at all remains at zero either way.
    /// </remarks>
    private static double SigmasAgainstWidenedBaseline(List<double> usable, double latest)
    {
        RollingChannelStatistics widened = new(usable.Count + 1);
        foreach (double sample in usable)
        {
            widened.Add(sample);
        }
        widened.Add(latest);

        return widened.ZScoreOf(latest);
    }

    private static string DescribeVerdict(bool isAnomaly, double latest, double sigmas)
    {
        if (isAnomaly) return $"Sample {latest:G6} sits {sigmas:F1} sigma from the baseline mean.";

        return sigmas <= DegenerateSigma
            ? "Series carries no measurable variance; nothing to compare a sample against."
            : $"Sample {latest:G6} is within {AnomalyThresholdSigma:F0} sigma of the baseline.";
    }

    private static List<double> CollectFiniteSamples(double[]? samples)
    {
        List<double> usable = new(samples?.Length ?? 0);
        if (samples is null) return usable;

        foreach (double sample in samples)
        {
            if (double.IsFinite(sample)) usable.Add(sample);
        }
        return usable;
    }
}
