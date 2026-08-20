using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Outcome of analysing one sample against a channel's rolling history.
/// </summary>
public class AnomalyResult
{
    public string ChannelName { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double ZScore { get; set; }
    public bool IsAnomaly { get; set; }

    /// <summary>Regression extrapolation of the channel 60 <em>seconds</em> ahead.</summary>
    /// <remarks>Only meaningful when <see cref="HasForecast"/> is true.</remarks>
    public double PredictedValueIn60s { get; set; }

    /// <summary>
    /// True when the fitted trend explains enough of the variation to extrapolate from.
    /// </summary>
    /// <remarks>
    /// The same distinction <see cref="HasVerdict"/> draws for the z-score, for the same reason. A
    /// least-squares line always exists, so a noisy channel yields a confident slope pointing
    /// nowhere: on a live Wikipedia feed this produced a forecast of minus 228,000 bytes for a page
    /// size. Callers must check this before rendering the number, exactly as they check
    /// <see cref="HasVerdict"/> before rendering a sigma.
    /// </remarks>
    public bool HasForecast { get; set; }

    /// <summary>How much of the channel's variation the fitted trend explains, 0 to 1.</summary>
    public double TrendRSquared { get; set; }

    /// <summary>Seconds until the rising trend crosses the warning threshold, or -1 when not trending toward it.</summary>
    public double EstimatedTimeToBreachSec { get; set; }

    /// <summary>Slope of the fitted trend in units per second.</summary>
    public double TrendPerSecond { get; set; }

    /// <summary>Samples currently backing the statistics.</summary>
    public int SampleCount { get; set; }

    /// <summary>
    /// Identifies the analyzer and the settings behind <see cref="ZScore"/>, or <c>null</c> when
    /// no verdict was reached — during warm-up, before enough samples exist to judge anything.
    /// </summary>
    /// <remarks>
    /// The warm-up path leaves <see cref="ZScore"/> at 0 and <see cref="IsAnomaly"/> false, which
    /// reads identically to a genuinely calm channel. A dashboard showing "0.0 sigma, normal"
    /// during the first seconds after connect is asserting a normality it has not established.
    /// Callers should check <see cref="HasVerdict"/> before rendering either field.
    /// </remarks>
    public string? AnalyzerId { get; set; }

    /// <summary>True when this result carries an actual judgement.</summary>
    public bool HasVerdict => !string.IsNullOrEmpty(AnalyzerId);

    /// <summary>
    /// True when this channel's history was discarded by the channel ceiling and this sample is
    /// re-starting warm-up rather than continuing a series.
    /// </summary>
    /// <remarks>
    /// Without this flag an evicted channel is indistinguishable from a newly connected one: both
    /// report a small <see cref="SampleCount"/> and no verdict. The difference matters because the
    /// second is expected and the first means the system is over its configured limit and is
    /// silently dropping the very history the operator is relying on. It stays set for the whole
    /// re-warm-up, not just the first sample after readmission, so a caller that samples partway
    /// through still learns why there is no verdict.
    /// </remarks>
    public bool RestartedAfterEviction { get; set; }
}

/// <summary>
/// Real-time anomaly detection and trend forecasting per telemetry channel.
/// </summary>
/// <remarks>
/// Forecast horizons are expressed in seconds and converted to samples through
/// <see cref="SampleRateHz"/>. The previous implementation extrapolated 60 <em>samples</em> ahead
/// while reporting the result as a 60-<em>second</em> prediction, and separately assumed a fixed
/// 20 Hz feed when estimating breach time — so the two figures described different futures.
/// </remarks>
public class TelemetryMlAnalyticsEngine
{
    /// <summary>Horizon, in seconds, of <see cref="AnomalyResult.PredictedValueIn60s"/>.</summary>
    public const double ForecastHorizonSec = 60.0;

    /// <summary>
    /// Resident channels, capped. Was an unbounded <c>ConcurrentDictionary</c> that retained every
    /// channel name the process had ever seen; measured at about 690 managed bytes per channel, so
    /// a million channels cost 657 MB and nothing stopped it climbing past that.
    /// </summary>
    private readonly BoundedChannelRegistry<RollingChannelStatistics> _channels;

    /// <summary>
    /// Most recent anomalous result per channel, for diagnosis and alerting. Capped separately and
    /// far lower: <see cref="RecentAnomalies"/> sorts this on every read, which was measured at
    /// 407 ms for the 360,639 entries a million channels produced.
    /// </summary>
    private readonly BoundedChannelRegistry<AnomalyResult> _recentAnomalies;

    private readonly int _windowSize;

    /// <summary>Channel ceiling when the caller does not pick one. 50,000 channels measured at 35 MB.</summary>
    public const int DefaultMaxChannels = 50_000;

    /// <summary>Ceiling on retained anomalies, which are read and sorted by the UI.</summary>
    public const int DefaultMaxRecentAnomalies = 1_000;

    /// <summary>Sigma threshold at or above which a sample is flagged anomalous.</summary>
    public double ZScoreThreshold { get; set; } = 2.5;

    /// <summary>Nominal feed rate used to convert between samples and seconds.</summary>
    public double SampleRateHz { get; }

    /// <summary>Minimum samples required before statistics are considered meaningful.</summary>
    public int MinimumSamples { get; init; } = 5;

    public TelemetryMlAnalyticsEngine(
        int windowSize = 50,
        double sampleRateHz = 20.0,
        int maxChannels = DefaultMaxChannels,
        int maxRecentAnomalies = DefaultMaxRecentAnomalies)
    {
        if (windowSize < 2) throw new ArgumentOutOfRangeException(nameof(windowSize), "Window must hold at least two samples.");
        if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be positive.");

        _windowSize = windowSize;
        SampleRateHz = sampleRateHz;
        _channels = new BoundedChannelRegistry<RollingChannelStatistics>(maxChannels);
        _recentAnomalies = new BoundedChannelRegistry<AnomalyResult>(maxRecentAnomalies);
    }

    /// <summary>Channels whose statistics are resident right now. Never exceeds <see cref="ChannelCapacity"/>.</summary>
    /// <remarks>
    /// This used to be the count of every channel name the engine had ever seen, which only ever
    /// went up. It is now a live occupancy figure, so a reading below the ceiling means the engine
    /// is holding everything it has been shown and a reading at the ceiling means it is not.
    /// </remarks>
    public int TrackedChannelCount => _channels.Count;

    /// <summary>The declared ceiling on resident channels.</summary>
    public int ChannelCapacity => _channels.Capacity;

    /// <summary>Channels whose statistics have been discarded to stay within the ceiling.</summary>
    public long ChannelEvictions => _channels.Evictions;

    /// <summary>Anomaly records dropped to stay within the retained-anomaly ceiling.</summary>
    public long AnomalyEvictions => _recentAnomalies.Evictions;

    /// <summary>Occupancy of the channel store, for an operator watching the limit approach.</summary>
    public ChannelCardinalityReport ChannelCardinality => _channels.Report("analytics channels");

    /// <summary>Occupancy of the retained-anomaly store.</summary>
    public ChannelCardinalityReport AnomalyCardinality => _recentAnomalies.Report("retained anomalies");

    /// <summary>Raised when a channel's statistics are discarded to stay within the ceiling.</summary>
    public event EventHandler<string>? ChannelEvicted
    {
        add => _channels.Evicted += value;
        remove => _channels.Evicted -= value;
    }

    /// <summary>
    /// Stamped onto every verdict this engine issues, so a stored result can be traced back to the
    /// configuration that produced it.
    /// </summary>
    /// <remarks>
    /// The window, threshold and minimum sample count are all part of the identity because each
    /// one changes the answer for identical input. Recomputed on read rather than cached:
    /// <see cref="ZScoreThreshold"/> is settable at runtime, and a cached id would keep attributing
    /// new verdicts to the old configuration.
    /// </remarks>
    public string AnalyzerId =>
        $"zscore-rolling/w{_windowSize}/t{ZScoreThreshold.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}/n{MinimumSamples}";

    /// <summary>
    /// The latest anomalous reading for each channel, most severe first.
    /// </summary>
    /// <remarks>
    /// Diagnosis and alerting need to know what recently went wrong. Without this the LLM dialog
    /// had nothing real to analyse and shipped a hard-coded anomaly set to the model instead,
    /// so the returned report described telemetry the system had never seen.
    /// </remarks>
    public IReadOnlyList<AnomalyResult> RecentAnomalies =>
        _recentAnomalies.Snapshot().OrderByDescending(a => a.ZScore).ToList();

    /// <summary>Clears the retained anomaly set.</summary>
    public void ClearRecentAnomalies() => _recentAnomalies.Clear();

    public AnomalyResult AnalyzeChannel(string channelName, double newValue, double warningUpperThreshold = 95.0)
    {
        channelName ??= string.Empty;
        var channel = _channels.GetOrAdd(
            channelName,
            _ => new RollingChannelStatistics(_windowSize),
            out ChannelAdmission admission);

        lock (channel)
        {
            channel.Add(newValue);

            var result = new AnomalyResult
            {
                ChannelName = channelName,
                CurrentValue = newValue,
                SampleCount = channel.Count
            };

            if (channel.Count < MinimumSamples)
            {
                // Warm-up. If this channel is one the ceiling threw away, say so rather than let a
                // restarted series look like a newly connected sensor.
                result.RestartedAfterEviction =
                    admission == ChannelAdmission.ReadmittedAfterEviction
                    || _channels.WasRecentlyEvicted(channelName);

                // AnalyzerId stays null: there is no baseline yet, so there is no verdict to
                // report. Leaving it unset is what lets callers distinguish "not judged" from
                // "judged normal" instead of showing a reassuring 0.0 sigma to the operator.
                result.Mean = newValue;
                result.PredictedValueIn60s = newValue;
                result.EstimatedTimeToBreachSec = -1;
                return result;
            }

            result.Mean = channel.Mean;
            result.StdDev = channel.StandardDeviation;
            result.ZScore = channel.ZScoreOf(newValue);
            result.IsAnomaly = result.ZScore >= ZScoreThreshold;
            result.AnalyzerId = AnalyzerId;

            // Trend is fitted per sample index, then converted to physical units per second.
            TrendFit fit = channel.TrendFitOverWindow();
            double slopePerSecond = fit.SlopePerSample * SampleRateHz;
            result.TrendPerSecond = slopePerSecond;
            result.TrendRSquared = fit.RSquared;

            // No forecast unless the line actually describes the data. Extrapolating a slope fitted
            // to scatter is how a page-size channel came to be predicted at a negative byte count.
            result.HasForecast = fit.SupportsForecast;
            result.PredictedValueIn60s = result.HasForecast
                ? newValue + slopePerSecond * ForecastHorizonSec
                : newValue;

            result.EstimatedTimeToBreachSec = result.HasForecast
                ? EstimateBreachSeconds(newValue, slopePerSecond, warningUpperThreshold)
                : -1;

            if (result.IsAnomaly)
            {
                _recentAnomalies.Set(channelName, result);
            }

            return result;
        }
    }

    /// <summary>Discards the history of one channel.</summary>
    public void ResetChannel(string channelName) => _channels.Remove(channelName ?? string.Empty);

    /// <summary>Discards all channel history.</summary>
    public void Reset() => _channels.Clear();

    /// <summary>
    /// Seconds until a rising trend reaches <paramref name="threshold"/>.
    /// Returns -1 when the channel is flat, falling, or already past the threshold.
    /// </summary>
    private static double EstimateBreachSeconds(double currentValue, double slopePerSecond, double threshold)
    {
        if (double.IsNaN(slopePerSecond) || slopePerSecond <= 1e-9) return -1;
        if (double.IsInfinity(threshold) || currentValue >= threshold) return -1;

        return (threshold - currentValue) / slopePerSecond;
    }
}
