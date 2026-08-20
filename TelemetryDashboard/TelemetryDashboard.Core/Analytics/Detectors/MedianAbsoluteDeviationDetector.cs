using System;
using System.Globalization;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Flags samples that sit far from the channel's <em>median</em>, measured in units of median
/// absolute deviation.
/// </summary>
/// <remarks>
/// Exists because the mean and standard deviation the rolling z-score uses are both wrecked by the
/// thing they are supposed to find. One spike of 1000 in a channel that lives near 50 drags the
/// mean up and inflates sigma, so the spike scores low and — worse — the samples after it are
/// measured against a baseline the spike invented. A median ignores up to half its inputs moving
/// arbitrarily far, which is exactly the resistance this needs.
///
/// <para><b>What it catches:</b> isolated outliers, including several in one window, and it keeps
/// catching them where a z-score's own baseline has already been poisoned.</para>
///
/// <para><b>What it misses:</b> a sustained level shift, once the new level occupies more than half
/// the window — at that point the new level <em>is</em> the median and the old one is the outlier.
/// A slow ramp, for the same reason. And a channel whose baseline is perfectly constant, where MAD
/// and the mean-absolute-deviation fallback are both zero: there is no scale, so this detector
/// declines rather than divide by one it made up. <see cref="RateOfChangeDetector"/> is what covers
/// that case, and covers it without needing a scale at all.</para>
/// </remarks>
public sealed class MedianAbsoluteDeviationDetector : IChannelDetector
{
    /// <summary>MAD to standard deviation, for normally distributed data.</summary>
    /// <remarks>
    /// The MAD of a normal sample is about 0.6745 sigma, so dividing by it puts this detector's
    /// score on the same scale as a z-score. That is a convenience for the operator, not an
    /// equivalence: on data that is not normal the two numbers are not interchangeable, which is
    /// why the verdict carries <see cref="DetectorScoreKind.RobustSigma"/> rather than
    /// <see cref="DetectorScoreKind.Sigma"/>.
    /// </remarks>
    public const double MadToSigma = 0.6745;

    /// <summary>Mean absolute deviation to standard deviation, for normally distributed data.</summary>
    public const double MeanDeviationToSigma = 0.7979;

    /// <summary>Baseline samples required before this detector will answer at all.</summary>
    public const int MinimumBaselineSamples = 5;

    private readonly BoundedChannelRegistry<DetectorWindow> _windows;
    private readonly ChannelSelector _channels;
    private readonly int _window;
    private readonly double _threshold;

    public MedianAbsoluteDeviationDetector(
        int window = 32,
        double threshold = 3.5,
        ChannelSelector? channels = null,
        string? label = null,
        int maxChannels = 50_000)
    {
        if (window < MinimumBaselineSamples + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window),
                $"A window below {MinimumBaselineSamples + 1} can never hold enough baseline to answer.");
        }
        if (!(threshold > 0)) throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be positive.");

        _window = window;
        _threshold = threshold;
        _channels = channels ?? ChannelSelector.All;
        _windows = new BoundedChannelRegistry<DetectorWindow>(maxChannels);

        DetectorId = DetectorNaming.Compose(label, "mad",
            $"w{window}/t{DetectorNaming.Number(threshold)}");
    }

    /// <inheritdoc />
    public string DetectorId { get; }

    /// <inheritdoc />
    public bool CanHandle(string channelName) => _channels.Matches(channelName);

    /// <summary>Channels whose windows are resident right now.</summary>
    public int TrackedChannelCount => _windows.Count;

    /// <inheritdoc />
    public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (!double.IsFinite(value))
        {
            return DetectorVerdict.NotJudged("sample is not a finite number; a dropped reading is not an excursion");
        }

        DetectorWindow window = _windows.GetOrAdd(channelName ?? string.Empty, _ => new DetectorWindow(_window), out _);

        lock (window)
        {
            window.Add(value);
            return Judge(window, value);
        }
    }

    /// <inheritdoc />
    public void Reset(string channelName) => _windows.Remove(channelName ?? string.Empty);

    private DetectorVerdict Judge(DetectorWindow window, double value)
    {
        if (window.BaselineCount < MinimumBaselineSamples)
        {
            return DetectorVerdict.NotJudged(
                $"warming up: {window.BaselineCount} of {MinimumBaselineSamples} baseline samples",
                window.Count, window.Fill);
        }

        window.TryBaselineMedian(out double median, out double mad);

        double scale = mad * (1.0 / MadToSigma);
        string basis = "MAD";

        if (mad <= 0)
        {
            // More than half the baseline is one value, so MAD is zero and there is no robust
            // scale. The mean absolute deviation still comes entirely from the data and survives a
            // tie at the median, so it is tried before giving up.
            double meanDeviation = window.BaselineMeanAbsoluteDeviation(median);
            if (meanDeviation <= 0)
            {
                return DetectorVerdict.NotJudged(
                    "baseline is perfectly constant; no spread to measure a deviation against",
                    window.Count, window.Fill);
            }

            scale = meanDeviation * (1.0 / MeanDeviationToSigma);
            basis = "mean-deviation fallback";
        }

        double score = Math.Abs(value - median) / scale;
        bool isAnomaly = score >= _threshold;

        return DetectorVerdict.Judged(
            DetectorId, isAnomaly, score, DetectorScoreKind.RobustSigma, window.Fill, window.Count,
            string.Create(CultureInfo.InvariantCulture,
                $"{score:0.00} robust sigma from median {median:G6} ({basis}, threshold {_threshold:0.##})"));
    }
}
