using System;
using System.Globalization;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Presents <see cref="TelemetryMlAnalyticsEngine"/> as one detector among several.
/// </summary>
/// <remarks>
/// The engine was the only detector this system had, wired straight into the ingest path. It is a
/// good detector and nothing here replaces it — this adapter exists so it stops being the only
/// possible answer. Two consequences follow immediately: an operator can run a second copy at a
/// different window over the same channel and compare, and every verdict now carries the same shape
/// as every other detector's, so a stored result says which one produced it.
///
/// <para>The warm-up rule passes through unchanged. The engine leaves
/// <see cref="AnomalyResult.AnalyzerId"/> null until it has a baseline, and this adapter turns that
/// into <see cref="DetectorVerdict.NotJudged"/> rather than into a confident zero — the two are the
/// same statement in two vocabularies.</para>
///
/// <para><b>What it catches:</b> excursions from a recent mean, on a channel with stable spread.
/// <b>What it misses:</b> anything its own baseline has already absorbed — a level shift stops being
/// anomalous once it fills the window, and a large outlier inflates the sigma that the samples
/// after it are measured against.</para>
/// </remarks>
public sealed class RollingZScoreDetector : IChannelDetector
{
    private readonly TelemetryMlAnalyticsEngine _engine;
    private readonly ChannelSelector _channels;
    private readonly string? _label;
    private readonly int _window;

    public RollingZScoreDetector(
        int window = 50,
        double threshold = 2.5,
        double sampleRateHz = 20.0,
        ChannelSelector? channels = null,
        string? label = null)
    {
        _window = window;
        _label = label;
        _channels = channels ?? ChannelSelector.All;
        _engine = new TelemetryMlAnalyticsEngine(window, sampleRateHz) { ZScoreThreshold = threshold };
    }

    /// <summary>The engine behind this detector, for callers that need its trend and anomaly history.</summary>
    public TelemetryMlAnalyticsEngine Engine => _engine;

    /// <inheritdoc />
    /// <remarks>
    /// Read from the engine on every call rather than cached, because
    /// <see cref="TelemetryMlAnalyticsEngine.ZScoreThreshold"/> is settable at runtime and a cached
    /// id would keep attributing new verdicts to the settings that were in force at construction.
    /// </remarks>
    public string DetectorId => DetectorNaming.Compose(_label, _engine.AnalyzerId, string.Empty);

    /// <inheritdoc />
    public bool CanHandle(string channelName) => _channels.Matches(channelName);

    /// <inheritdoc />
    public void Reset(string channelName) => _engine.ResetChannel(channelName ?? string.Empty);

    /// <inheritdoc />
    public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (!double.IsFinite(value))
        {
            return DetectorVerdict.NotJudged("sample is not a finite number; a dropped reading is not an excursion");
        }

        AnomalyResult result = _engine.AnalyzeChannel(channelName ?? string.Empty, value);
        double evidence = _window <= 0 ? 0.0 : Math.Clamp((double)result.SampleCount / _window, 0.0, 1.0);

        if (!result.HasVerdict)
        {
            string reason = result.RestartedAfterEviction
                ? $"warm-up restarted after eviction: {result.SampleCount} of {_engine.MinimumSamples} samples"
                : $"warm-up: {result.SampleCount} of {_engine.MinimumSamples} samples, no baseline yet";

            return DetectorVerdict.NotJudged(reason, result.SampleCount, evidence);
        }

        return DetectorVerdict.Judged(
            DetectorId, result.IsAnomaly, result.ZScore, DetectorScoreKind.Sigma, evidence, result.SampleCount,
            string.Create(CultureInfo.InvariantCulture,
                $"{result.ZScore:0.00} sigma from mean {result.Mean:G6} (threshold {_engine.ZScoreThreshold:0.##})"));
    }
}
