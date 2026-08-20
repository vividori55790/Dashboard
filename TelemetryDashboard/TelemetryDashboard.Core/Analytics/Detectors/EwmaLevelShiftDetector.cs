using System;
using System.Globalization;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// An EWMA control chart: catches a channel that has <em>moved</em>, not a channel that twitched.
/// </summary>
/// <remarks>
/// A rolling z-score compares each sample to a window that the sample itself will shortly be part
/// of, so a level shift is anomalous for exactly as long as it takes to fill the window and then
/// becomes the new normal — a converter that settled 8 degrees hotter and stayed there raises an
/// alert for fifty samples and is silent afterwards. This detector fixes a baseline once, during an
/// explicit training phase, and never lets the data move it.
///
/// <para><b>What it catches:</b> sustained level shifts and slow drift, including shifts far too
/// small to trip a per-sample sigma threshold — the smoothing is what accumulates a persistent bias
/// into a detectable one.</para>
///
/// <para><b>What it misses:</b> single isolated outliers, by construction. A one-sample spike moves
/// the EWMA by a fraction of lambda and is gone; that is the trade this chart makes and the reason
/// <see cref="MedianAbsoluteDeviationDetector"/> runs beside it. It also never judges a channel
/// whose training window was perfectly constant: limits derived from zero spread have zero width,
/// so every later sample — including one bit of sensor quantisation — would be an anomaly at
/// infinite sigma. It says so and stays silent until <see cref="Reset"/>.</para>
/// </remarks>
public sealed class EwmaLevelShiftDetector : IChannelDetector
{
    /// <summary>Mean successive difference to standard deviation, the individuals-chart estimator.</summary>
    /// <remarks>
    /// Chosen over the sample standard deviation of the training window on purpose: if the channel
    /// is already drifting while training runs, the sample deviation absorbs the drift and calls it
    /// normal spread. A mean of successive differences sees only sample-to-sample movement.
    /// </remarks>
    public const double MeanRangeToSigma = 1.128;

    private readonly BoundedChannelRegistry<EwmaState> _states;
    private readonly ChannelSelector _channels;
    private readonly int _training;
    private readonly double _lambda;
    private readonly double _limitSigma;

    public EwmaLevelShiftDetector(
        int trainingSamples = 40,
        double lambda = 0.2,
        double limitSigma = 3.0,
        ChannelSelector? channels = null,
        string? label = null,
        int maxChannels = 50_000)
    {
        if (trainingSamples < 5) throw new ArgumentOutOfRangeException(nameof(trainingSamples), "Training needs at least five samples.");
        if (lambda is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(lambda), "Lambda must be in (0, 1].");
        if (!(limitSigma > 0)) throw new ArgumentOutOfRangeException(nameof(limitSigma), "The control limit must be positive.");

        _training = trainingSamples;
        _lambda = lambda;
        _limitSigma = limitSigma;
        _channels = channels ?? ChannelSelector.All;
        _states = new BoundedChannelRegistry<EwmaState>(maxChannels);

        DetectorId = DetectorNaming.Compose(label, "ewma",
            $"n{trainingSamples}/L{DetectorNaming.Number(lambda)}/k{DetectorNaming.Number(limitSigma)}");
    }

    /// <inheritdoc />
    public string DetectorId { get; }

    /// <inheritdoc />
    public bool CanHandle(string channelName) => _channels.Matches(channelName);

    public void Reset(string channelName) => _states.Remove(channelName ?? string.Empty);

    /// <inheritdoc />
    public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (!double.IsFinite(value))
        {
            return DetectorVerdict.NotJudged("sample is not a finite number; a dropped reading is not an excursion");
        }

        EwmaState state = _states.GetOrAdd(channelName ?? string.Empty, _ => new EwmaState(), out _);

        lock (state) return state.Trained ? Judge(state, value) : state.Train(value, _training);
    }

    private DetectorVerdict Judge(EwmaState state, double value)
    {
        if (state.Sigma <= 0)
        {
            return DetectorVerdict.NotJudged(
                "training window was perfectly constant; limits from zero spread would flag every "
                + "later sample at infinite sigma", state.Seen, 1.0);
        }

        state.Steps++;
        state.Ewma = _lambda * value + (1.0 - _lambda) * state.Ewma;

        // Time-varying limit. The asymptotic form is wider than the truth for the first samples
        // after training, which is precisely when a shift that started during training shows up.
        double variance = _lambda / (2.0 - _lambda) * (1.0 - Math.Pow(1.0 - _lambda, 2.0 * state.Steps));
        double score = Math.Abs(state.Ewma - state.Mean) / (state.Sigma * Math.Sqrt(variance));
        bool isAnomaly = score >= _limitSigma;

        string reason = string.Create(CultureInfo.InvariantCulture,
            $"EWMA {state.Ewma:G6} vs trained level {state.Mean:G6}, {score:0.00} sigma of the smoothed statistic (limit {_limitSigma:0.##})");

        return DetectorVerdict.Judged(
            DetectorId, isAnomaly, score, DetectorScoreKind.Sigma, 1.0, state.Seen + state.Steps, reason);
    }

    /// <summary>Per-channel training accumulator and smoothed level.</summary>
    private sealed class EwmaState
    {
        public bool Trained;
        public int Seen;
        public int Steps;
        public double Mean;
        public double Sigma;
        public double Ewma;

        private double _sum;
        private double _successiveDifference;
        private double _previous;

        public DetectorVerdict Train(double value, int required)
        {
            if (Seen > 0) _successiveDifference += Math.Abs(value - _previous);
            _previous = value;
            _sum += value;
            Seen++;

            if (Seen < required)
            {
                return DetectorVerdict.NotJudged(
                    $"training: {Seen} of {required} samples, no baseline to compare against yet",
                    Seen, (double)Seen / required);
            }

            Trained = true;
            Mean = _sum / Seen;
            Ewma = Mean;
            Sigma = _successiveDifference / (Seen - 1) / MeanRangeToSigma;

            return DetectorVerdict.NotJudged(
                $"training complete over {Seen} samples; judging from the next one",
                Seen, 1.0);
        }
    }
}
