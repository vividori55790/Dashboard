using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// How far a channel's recent average has run ahead of its long-run average.
/// </summary>
/// <remarks>
/// The blind spot in every detector this product had. A rolling z-score measures a reading against
/// the window it just came from, so a channel that moves slowly enough drags its own baseline along
/// with it and never scores: a rail sagging a millivolt an hour, a case temperature climbing as a
/// fan bearing wears, a sensor whose calibration is walking away. Everything stays inside its
/// limits, every z-score stays near zero, and the unit has been getting worse for three weeks.
/// <para>
/// Two averages with different memories is the whole mechanism. The slow one is where the channel
/// has been living; the fast one is where it is now, smoothed enough that a single spike is not
/// mistaken for a trend. Their difference is the drift.
/// </para>
/// <para>
/// What this measures, stated exactly, because the obvious misreading is more useful than the truth
/// and therefore tempting: it is a <em>sustained rate of change</em>, not a distance from a factory
/// baseline. A step change shows briefly and then decays as the slow average catches up — correctly,
/// because a step is a transient and the z-score already catches those. A ramp shows for as long as
/// the ramp continues. Nothing here remembers where the unit was when it was commissioned.
/// </para>
/// </remarks>
public sealed class DriftMonitor
{
    /// <summary>Memory of the "where it is now" average, in seconds.</summary>
    public double FastSeconds { get; init; } = 30.0;

    /// <summary>Memory of the "where it has been living" average, in seconds.</summary>
    /// <remarks>
    /// Far longer than the fast one, or the two track each other and their difference is noise. The
    /// ratio is what sets how slow a ramp this can still see: the drift a ramp produces is roughly
    /// its rate multiplied by this constant, so a longer memory sees gentler slopes and takes
    /// longer to be sure of them.
    /// </remarks>
    public double SlowSeconds { get; init; } = 900.0;

    /// <summary>
    /// Seconds of samples required before a drift figure is offered at all.
    /// </summary>
    /// <remarks>
    /// The slow average is seeded by its first sample, so for the first stretch of a run it sits
    /// wherever the channel happened to be at start-up and the difference against it is start-up
    /// noise, not drift. Reporting that would make every restart look like a fault.
    /// </remarks>
    public double WarmUpSeconds { get; init; } = 120.0;

    private readonly ExponentialAverage _fast = new();
    private readonly ExponentialAverage _slow = new();
    private double _observedSeconds;

    /// <summary>Where the channel is now, smoothed. NaN before the first sample.</summary>
    public double Recent => _fast.Value;

    /// <summary>Where the channel has been living. NaN before the first sample.</summary>
    public double Baseline => _slow.Value;

    /// <summary>Seconds of samples folded in.</summary>
    public double ObservedSeconds => _observedSeconds;

    /// <summary>Whether enough has been seen for a drift figure to mean anything.</summary>
    public bool IsWarm => _observedSeconds >= WarmUpSeconds && _slow.HasValue;

    /// <summary>
    /// Folds one sample in and returns the drift, or null while still warming up.
    /// </summary>
    /// <param name="value">The reading.</param>
    /// <param name="elapsedSeconds">Seconds since this channel's previous reading.</param>
    /// <remarks>
    /// Null rather than zero before the warm-up completes. Zero is a measurement — "this channel is
    /// not drifting" — and publishing it during the very window where the answer is unknown is the
    /// reassurance a monitoring product must never give.
    /// <para>
    /// A non-positive gap advances nothing. Two readings sharing a timestamp describe no elapsed
    /// time, and folding one in at an alpha derived from zero would weight it as though no time had
    /// passed, which is exactly what it means.
    /// </para>
    /// </remarks>
    public double? Update(double value, double elapsedSeconds)
    {
        if (!double.IsFinite(value)) return null;

        // The first sample seeds both averages outright, whatever it claims about elapsed time.
        // Discarding it and waiting for the second would leave the baseline unseeded through the
        // gap between them, and on a channel that reports once a minute that gap is the minute.
        if (!_slow.HasValue)
        {
            _fast.Update(value, 1.0);
            _slow.Update(value, 1.0);
            return null;
        }

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return IsWarm ? Drift() : null;

        _fast.Update(value, ExponentialAverage.AlphaForTimeConstant(elapsedSeconds, FastSeconds));
        _slow.Update(value, ExponentialAverage.AlphaForTimeConstant(elapsedSeconds, SlowSeconds));
        _observedSeconds += elapsedSeconds;

        return IsWarm ? Drift() : null;
    }

    /// <summary>Drift as a fraction of the baseline, or NaN when the baseline has no magnitude.</summary>
    /// <remarks>
    /// Offered beside the absolute figure because a limit written in the channel's own unit is the
    /// one an engineer can reason about — half a volt on a 48 V rail — while a fraction is what
    /// compares across channels. Neither is right for both jobs, so both are available and neither
    /// is computed from the other behind the caller's back.
    /// </remarks>
    public double RelativeDrift()
    {
        if (!IsWarm) return double.NaN;

        double baseline = Math.Abs(Baseline);
        return baseline > 0 ? Drift() / baseline : double.NaN;
    }

    /// <summary>Forgets both averages, so the next sample starts a fresh baseline.</summary>
    /// <remarks>
    /// Called when the source changes. A slow average carried across a rig change would spend a
    /// quarter of an hour reporting the difference between two machines as drift in one of them.
    /// </remarks>
    public void Reset()
    {
        _fast.Reset();
        _slow.Reset();
        _observedSeconds = 0;
    }

    private double Drift() => _fast.Value - _slow.Value;
}
