namespace TelemetryDashboard.Core.Simulator;

/// <summary>
/// Declares that a channel accumulates another channel over time rather than wandering.
/// </summary>
/// <remarks>
/// The simulator's rule is that channels are independent, and that rule is load-bearing: inventing
/// a correlation nobody put there is a fabricated reading one level up. This is the one exception,
/// and it is narrow on purpose — not a correlation but an <em>identity</em>. A battery's state of
/// charge is not a quantity that happens to follow the current; it is the integral of the current,
/// by definition, which is how a coulomb counter measures it on real hardware.
/// <para>
/// What this exists to prevent is worse than a missing feature. Declared as an ordinary channel,
/// state of charge would drift around its nominal at 8 % of its range, rising while the bank
/// discharges and falling while it charges, and it would look exactly like every other reading on
/// the screen. The previous simulator in this repository did a gentler version of the same thing —
/// <c>94.5 - t * 0.0005</c>, a ramp in wall-clock time that ran at the same rate whether the
/// battery was charging at +12 A or discharging at -32 A. An operator cannot tell that apart from
/// a measurement, and the number it produces is the one a UPS is bought for.
/// </para>
/// <para>
/// <b>Two things this deliberately does not model.</b> There is no efficiency term, so a full
/// charge-discharge cycle returns to where it started, which no battery does. And nothing stops the
/// source at either end of the range: at 0 % the accumulator clamps but the current keeps being
/// reported, so a bank that has run flat goes on showing discharge power. Both are visible in the
/// numbers rather than hidden by them.
/// </para>
/// </remarks>
public sealed class ChannelIntegration
{
    /// <summary>Id of the channel being accumulated.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// How far this channel moves per second, per one unit of <see cref="Source"/>.
    /// </summary>
    /// <remarks>
    /// Stated as a rate rather than as a capacity so this type needs no idea what it is measuring.
    /// For a 200 Ah bank reporting charge in percent, one amp for one hour is one amp-hour, which
    /// is 0.5 % of the bank: <c>100 / (200 * 3600)</c> = 1.3889e-4 %/(A·s).
    /// </remarks>
    public required double PerSecond { get; init; }
}
