using System;
using System.Globalization;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Flags a channel that moved faster than the physical process behind it can move.
/// </summary>
/// <remarks>
/// The only detector here that needs no baseline and no statistics. That is the point of it: every
/// scale-based detector divides by a spread the data has to supply, so a perfectly steady channel
/// defeats all of them — and a steady channel taking a sudden step is the most obvious fault there
/// is. An operator who knows a coolant loop cannot gain 40 degrees in a second can say so, and this
/// enforces it from the first pair of samples.
///
/// <para><b>What it catches:</b> instantaneous jumps, on any channel, including ones the statistical
/// detectors decline because their baseline has no spread. It is judging against a stated physical
/// fact rather than against the channel's own history, so it cannot be poisoned by that history.</para>
///
/// <para><b>What it misses:</b> everything gradual. A ramp that reaches the same fault over a minute
/// never exceeds the limit on any single step, and this detector will report every one of those
/// samples as normal. It also cannot learn its own limit — a limit nobody set is a detector that
/// does nothing — and it is useless on a feed whose samples do not carry distinct timestamps,
/// where it declines rather than divide by a zero interval.</para>
/// </remarks>
public sealed class RateOfChangeDetector : IChannelDetector
{
    /// <summary>
    /// Default gap beyond which two samples are too far apart to describe a rate.
    /// </summary>
    /// <remarks>
    /// A large change divided by a long silence looks like a slow, calm change. That is the failure
    /// this codebase exists to prevent — an outage rendering as health — so a gap produces no
    /// verdict rather than a reassuring one. A channel that genuinely reports less often than this
    /// must configure a larger gap; the detector cannot infer the intent.
    /// </remarks>
    public const double DefaultMaxGapSeconds = 5.0;

    private readonly BoundedChannelRegistry<RateState> _states;
    private readonly ChannelSelector _channels;
    private readonly double _maxRate;
    private readonly double _maxGapSeconds;

    /// <param name="maxRatePerSecond">Largest change per second the process can physically produce.</param>
    /// <param name="maxGapSeconds">Longest interval that still describes a rate. See <see cref="DefaultMaxGapSeconds"/>.</param>
    public RateOfChangeDetector(
        double maxRatePerSecond,
        double maxGapSeconds = DefaultMaxGapSeconds,
        ChannelSelector? channels = null,
        string? label = null,
        int maxChannels = 50_000)
    {
        if (!(maxRatePerSecond > 0) || !double.IsFinite(maxRatePerSecond))
        {
            throw new ArgumentOutOfRangeException(nameof(maxRatePerSecond),
                "A rate limit must be a positive, finite number of units per second; there is no default worth guessing.");
        }
        if (!(maxGapSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(maxGapSeconds), "The gap limit must be positive.");

        _maxRate = maxRatePerSecond;
        _maxGapSeconds = maxGapSeconds;
        _channels = channels ?? ChannelSelector.All;
        _states = new BoundedChannelRegistry<RateState>(maxChannels);

        DetectorId = DetectorNaming.Compose(label, "rate",
            $"max{DetectorNaming.Number(maxRatePerSecond)}ps/gap{DetectorNaming.Number(maxGapSeconds)}s");
    }

    /// <inheritdoc />
    public string DetectorId { get; }

    /// <inheritdoc />
    public bool CanHandle(string channelName) => _channels.Matches(channelName);

    /// <inheritdoc />
    public void Reset(string channelName) => _states.Remove(channelName ?? string.Empty);

    /// <inheritdoc />
    public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (!double.IsFinite(value))
        {
            return DetectorVerdict.NotJudged("sample is not a finite number; a dropped reading is not an excursion");
        }

        RateState state = _states.GetOrAdd(channelName ?? string.Empty, _ => new RateState(), out _);

        lock (state)
        {
            if (!state.HasPrevious)
            {
                state.Accept(value, observedUtc);
                return DetectorVerdict.NotJudged("first sample; a rate needs two", 1);
            }

            double seconds = (observedUtc - state.At).TotalSeconds;
            DetectorVerdict verdict = Judge(state, value, seconds);
            state.Accept(value, observedUtc);
            return verdict;
        }
    }

    private DetectorVerdict Judge(RateState state, double value, double seconds)
    {
        if (seconds <= 0)
        {
            return DetectorVerdict.NotJudged(
                "samples carry the same timestamp or an earlier one; there is no interval to divide by", 2);
        }

        if (seconds > _maxGapSeconds)
        {
            string gap = string.Create(CultureInfo.InvariantCulture,
                $"{seconds:0.###} s since the previous sample, over the {_maxGapSeconds:0.###} s limit");

            return DetectorVerdict.NotJudged(
                gap + "; a change spread across a gap cannot be told from a step", 2);
        }

        double rate = Math.Abs(value - state.Value) / seconds;
        bool isAnomaly = rate >= _maxRate;

        return DetectorVerdict.Judged(
            DetectorId, isAnomaly, rate, DetectorScoreKind.UnitsPerSecond, 1.0, 2,
            string.Create(CultureInfo.InvariantCulture,
                $"{rate:0.###} units/s over {seconds:0.###} s (limit {_maxRate:0.###})"));
    }

    /// <summary>The one sample a rate needs to remember.</summary>
    private sealed class RateState
    {
        public bool HasPrevious;
        public double Value;
        public DateTime At;

        public void Accept(double value, DateTime at)
        {
            Value = value;
            At = at;
            HasPrevious = true;
        }
    }
}
