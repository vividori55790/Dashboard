using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Judging a whole recorded window rather than its newest sample.
/// </summary>
/// <remarks>
/// <see cref="AnomalyEngine.Evaluate"/> answers "is the latest reading unusual", which is the right
/// question live and the wrong one about a window somebody is reading back after the fact. An
/// incident window runs from before a fault to after it, so its newest sample is the recovery — and
/// asking about that reports "normal" for exactly the channel that caused the alarm.
/// </remarks>
public sealed partial class AnomalyEngine
{
    /// <summary>
    /// The worst the live detector would have scored anywhere inside <paramref name="samples"/>.
    /// </summary>
    /// <remarks>
    /// Walked in order through the same <see cref="RollingChannelStatistics"/> the live path uses,
    /// one sample at a time, keeping the highest score it would have produced. That is deliberate
    /// and it is the only construction here that does not need new statistics: each sample is
    /// measured against the ones before it, and against nothing after, so a reading cannot inflate
    /// the baseline it is being judged by — which is what happens to any scheme that scores a
    /// sample against a window containing it, and which hides exactly the largest excursions.
    /// <para>
    /// So the answer means something precise and sayable: <em>if this detector had been watching
    /// this window live, this is the worst it would have seen, and when</em>. It is not a claim
    /// about what a different detector, or a human, would have called unusual.
    /// </para>
    /// </remarks>
    public AnomalyEvaluation EvaluateWindow(IReadOnlyList<double>? samples)
    {
        List<double> usable = new(samples?.Count ?? 0);
        if (samples is not null)
        {
            foreach (double sample in samples)
            {
                if (double.IsFinite(sample)) usable.Add(sample);
            }
        }

        if (usable.Count < MinimumUsableSamples)
        {
            // "I could not tell" and "nothing was wrong" are the same to a caller reading only a
            // boolean, and an operator who cannot separate them reads an unjudged channel as a
            // healthy one. This endpoint's whole value is triage, so the distinction is the point.
            return new AnomalyEvaluation(
                false,
                $"Not judged: {usable.Count} usable sample(s), {MinimumUsableSamples} required.",
                0.0,
                usable.Count) { Judged = false };
        }

        var rolling = new RollingChannelStatistics(WindowBaselineSamples);
        double peak = 0.0;
        int peakIndex = -1;
        double peakValue = 0.0;

        for (int i = 0; i < usable.Count; i++)
        {
            rolling.Add(usable[i]);

            // Nothing is scored until the detector has a baseline worth measuring against, which is
            // the same refusal the live path makes on its first samples.
            if (i + 1 < MinimumUsableSamples) continue;

            double sigmas = rolling.ZScoreOf(usable[i]);

            // The first scored sample establishes the peak even when it scores zero; later ones
            // replace it only if they beat it. Requiring a strict improvement to record anything
            // left a perfectly flat window with no peak at all, and it was then described as being
            // shorter than the baseline -- which was false, and would have put every idle channel
            // on a rig into the "could not judge" bucket of an incident report.
            if (peakIndex >= 0 && sigmas <= peak) continue;

            peak = sigmas;
            peakIndex = i;
            peakValue = usable[i];
        }

        double bar = BarFor(usable.Count);
        bool isAnomaly = peak > bar;
        return new AnomalyEvaluation(
            isAnomaly, DescribeWindow(isAnomaly, peak, peakIndex, peakValue, usable.Count, bar),
            peak, usable.Count);
    }

    /// <summary>
    /// Samples the rolling baseline spans while walking a window.
    /// </summary>
    /// <remarks>
    /// Bounded rather than the whole window, so a channel that spent an hour at one value and then
    /// moved is judged against the recent past rather than against an hour of stillness that has
    /// nothing to do with the moment. It matches the order of the live detector's own window.
    /// </remarks>
    private const int WindowBaselineSamples = 50;

    /// <summary>
    /// The score a window of <paramref name="sampleCount"/> samples has to beat to mean anything.
    /// </summary>
    /// <remarks>
    /// Not the live detector's fixed bar, and this is the whole difference between a batch verdict
    /// and a live one. Live, each sample is judged once. Here the <em>largest</em> of several
    /// hundred scores is taken, and the largest of many draws from ordinary noise is large by
    /// construction: for n samples it lands near sqrt(2 ln n), about 3.1 at 128 and 3.7 at 1000.
    /// <para>
    /// Measured before this existed, on a live host: a channel carrying nothing but 20 mV of noise
    /// was reported anomalous at 3.16 sigma over a 128-sample window -- textbook, and exactly the
    /// expected maximum for that many draws. Across a thirty-channel rig the triage list would have
    /// named ten every time, which is worse than no list, because a list that is usually wrong
    /// stops being read. So the bar is where noise alone would be expected to reach, plus a margin,
    /// and never below the live detector's own.
    /// </para>
    /// </remarks>
    public static double BarFor(int sampleCount)
    {
        if (sampleCount < 2) return AnomalyThresholdSigma;

        double expectedNoisePeak = Math.Sqrt(2.0 * Math.Log(sampleCount));
        return Math.Max(AnomalyThresholdSigma, expectedNoisePeak + NoiseMargin);
    }

    /// <summary>How far past the expected noise peak a window has to go.</summary>
    /// <remarks>
    /// One sigma. The expected maximum is where noise lands on average, so half of all quiet
    /// windows exceed it; the margin is what turns "typical for noise" into "further than noise
    /// usually gets".
    /// </remarks>
    private const double NoiseMargin = 1.0;

    private static string DescribeWindow(bool isAnomaly, double peak, int index, double value, int count, double bar)
    {
        if (index < 0)
        {
            return "Not judged: the window is shorter than the baseline the detector needs.";
        }

        string where = $"sample {index + 1} of {count}";

        return isAnomaly
            ? $"Worst moment: {value:G6} at {where}, {peak:F1} sigma against a bar of {bar:F1} "
              + $"for a {count}-sample window."
            : peak <= 1e-9
                ? "Nothing moved: the window carries no measurable variance to score against."
                : $"Quiet: the worst moment reached {peak:F1} sigma, inside the {bar:F1} sigma a "
                  + $"{count}-sample window of noise would be expected to reach anyway.";
    }
}
