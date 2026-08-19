using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;

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
    public double PredictedValueIn60s { get; set; }

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

    private readonly ConcurrentDictionary<string, RollingChannelStatistics> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Most recent anomalous result per channel, for diagnosis and alerting.</summary>
    private readonly ConcurrentDictionary<string, AnomalyResult> _recentAnomalies =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly int _windowSize;

    /// <summary>Sigma threshold at or above which a sample is flagged anomalous.</summary>
    public double ZScoreThreshold { get; set; } = 2.5;

    /// <summary>Nominal feed rate used to convert between samples and seconds.</summary>
    public double SampleRateHz { get; }

    /// <summary>Minimum samples required before statistics are considered meaningful.</summary>
    public int MinimumSamples { get; init; } = 5;

    public TelemetryMlAnalyticsEngine(int windowSize = 50, double sampleRateHz = 20.0)
    {
        if (windowSize < 2) throw new ArgumentOutOfRangeException(nameof(windowSize), "Window must hold at least two samples.");
        if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be positive.");

        _windowSize = windowSize;
        SampleRateHz = sampleRateHz;
    }

    /// <summary>Channel names seen so far.</summary>
    public int TrackedChannelCount => _channels.Count;

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
        _recentAnomalies.Values.OrderByDescending(a => a.ZScore).ToList();

    /// <summary>Clears the retained anomaly set.</summary>
    public void ClearRecentAnomalies() => _recentAnomalies.Clear();

    public AnomalyResult AnalyzeChannel(string channelName, double newValue, double warningUpperThreshold = 95.0)
    {
        channelName ??= string.Empty;
        var channel = _channels.GetOrAdd(channelName, _ => new RollingChannelStatistics(_windowSize));

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
            double slopePerSample = channel.TrendSlopePerSample();
            double slopePerSecond = slopePerSample * SampleRateHz;
            result.TrendPerSecond = slopePerSecond;

            result.PredictedValueIn60s = newValue + slopePerSecond * ForecastHorizonSec;
            result.EstimatedTimeToBreachSec = EstimateBreachSeconds(newValue, slopePerSecond, warningUpperThreshold);

            if (result.IsAnomaly)
            {
                _recentAnomalies[channelName] = result;
            }

            return result;
        }
    }

    /// <summary>Discards the history of one channel.</summary>
    public void ResetChannel(string channelName) => _channels.TryRemove(channelName ?? string.Empty, out _);

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
