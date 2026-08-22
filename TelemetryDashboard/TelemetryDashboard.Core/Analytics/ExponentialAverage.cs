using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// An exponentially weighted moving average: a value with a memory whose length you choose.
/// </summary>
/// <remarks>
/// Extracted from <see cref="AnomalyEngine"/>, which held the only one in this codebase and bolted
/// it onto a batch z-score evaluator it shares nothing with — separate fields, separate methods, no
/// interaction. Nothing constructed that class, so the only smoothing this product had was
/// unreachable.
/// <para>
/// The smoothing factor is supplied per update rather than stored. A fixed alpha means something
/// different on a rig sampling at 20 Hz and one sampling at 1 Hz — the same setting would give one
/// of them a memory of seconds and the other minutes — so a caller that cares about time computes
/// alpha from the gap it actually observed. <see cref="AlphaForTimeConstant"/> is that conversion.
/// </para>
/// </remarks>
public sealed class ExponentialAverage
{
    private double _value;

    /// <summary>Current average, or NaN before the first sample.</summary>
    public double Value => HasValue ? _value : double.NaN;

    /// <summary>Whether any sample has been folded in yet.</summary>
    public bool HasValue { get; private set; }

    /// <summary>Samples folded in so far.</summary>
    public long SampleCount { get; private set; }

    /// <summary>
    /// Folds one sample in at the given smoothing factor and returns the updated average.
    /// </summary>
    /// <remarks>
    /// The first sample seeds the average outright. Starting from zero would otherwise inject a
    /// ramp from zero up to the channel's operating point that looks exactly like a real transient
    /// — and on a drift monitor, exactly like the drift it is watching for.
    /// <para>
    /// Alpha is clamped rather than rejected. One tracks the raw signal with no memory and zero
    /// freezes the average at its seed; both are legitimate choices, and a caller deriving alpha
    /// from a measured time gap can land marginally outside the range through rounding alone.
    /// </para>
    /// </remarks>
    public double Update(double value, double alpha)
    {
        if (!double.IsFinite(value)) return Value;

        double weight = double.IsFinite(alpha) ? Math.Clamp(alpha, 0.0, 1.0) : 0.0;

        _value = HasValue ? weight * value + (1.0 - weight) * _value : value;
        HasValue = true;
        SampleCount++;
        return _value;
    }

    /// <summary>Forgets the average, so the next sample seeds it again.</summary>
    public void Reset()
    {
        _value = 0.0;
        HasValue = false;
        SampleCount = 0;
    }

    /// <summary>
    /// The smoothing factor that gives a memory of <paramref name="timeConstantSeconds"/> when
    /// samples arrive <paramref name="elapsedSeconds"/> apart.
    /// </summary>
    /// <remarks>
    /// The continuous form, <c>1 - exp(-dt / tau)</c>, rather than a fixed per-sample constant. It
    /// keeps the memory the same length in seconds whatever rate the data arrives at, and it stays
    /// correct when the rate varies — which it does on any real link, and dramatically on one that
    /// is dropping frames.
    /// </remarks>
    public static double AlphaForTimeConstant(double elapsedSeconds, double timeConstantSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return 0.0;
        if (!double.IsFinite(timeConstantSeconds) || timeConstantSeconds <= 0) return 1.0;

        return 1.0 - Math.Exp(-elapsedSeconds / timeConstantSeconds);
    }
}
