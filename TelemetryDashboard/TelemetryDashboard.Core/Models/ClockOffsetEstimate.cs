using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// How far another node's clock is from this one's, and how well that is known.
/// </summary>
/// <remarks>
/// ARCHITECTURE §3 is about the second half. An offset places a sample on a shared timeline; an
/// uncertainty is what says whether two samples can be <em>ordered</em> at all. Two events a
/// millisecond apart on different nodes cannot be ordered unless the offset between those nodes is
/// known to better than a millisecond, and a point estimate carries no claim about that — it is
/// read as a guarantee because nothing beside it says otherwise.
/// <para>
/// So the offset never travels alone here. <see cref="Samples"/> says how many observations it
/// rests on, and the three cases are deliberately different facts:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>None.</b> Nothing was ever measured for this node. <see cref="Unmeasured"/>, and
/// <see cref="HasOffset"/> is false — not an offset of zero, which is the claim that two clocks
/// agree perfectly and is exactly what a caller would go on to publish.
/// </description></item>
/// <item><description>
/// <b>One.</b> There is an offset and there is no error bar, because a spread needs two
/// observations to exist. <see cref="SpreadSec"/> is null and <see cref="CanOrder"/> answers false
/// for every separation. This is the case §3 was written about, and treating a single measurement
/// as exact is the failure it names.
/// </description></item>
/// <item><description>
/// <b>Two or more.</b> An offset and a measured spread, and ordering becomes a question the data
/// can answer.
/// </description></item>
/// </list>
/// <para>
/// <b>What the spread is not.</b> These observations come from one-way messages: a sample carries
/// the sending node's clock and arrives at some later reading of ours, so each observation is
/// <c>offset + transit</c> and no amount of them separates the two. The spread therefore measures
/// how much the transit <em>varied</em>, and the offset stays biased by however long the fastest
/// message took — a quantity nothing here can observe without a round trip. That makes
/// <see cref="SpreadSec"/> a <b>lower bound</b> on the real uncertainty, and it is documented as
/// one rather than presented as the answer. Reporting a floor as though it were a ceiling would be
/// the same error this type exists to prevent, one level further in.
/// </para>
/// </remarks>
/// <param name="OffsetSec">
/// Seconds to add to the other node's clock to reach ours, or NaN when nothing was measured.
/// </param>
/// <param name="SpreadSec">
/// Observed dispersion of the measurements, or null when fewer than two exist. A lower bound on
/// the uncertainty, never the whole of it.
/// </param>
/// <param name="Samples">How many observations this rests on.</param>
public readonly record struct ClockOffsetEstimate(double OffsetSec, double? SpreadSec, int Samples)
{
    /// <summary>Nobody has ever compared this node's clock to ours.</summary>
    public static ClockOffsetEstimate Unmeasured { get; } = new(double.NaN, null, 0);

    /// <summary>Whether an offset was measured at all.</summary>
    public bool HasOffset => Samples > 0;

    /// <summary>Whether the offset carries an error bar, rather than being a bare point estimate.</summary>
    public bool IsBounded => SpreadSec is { } spread && double.IsFinite(spread);

    /// <summary>
    /// Whether two events this far apart on the two clocks can be put in order.
    /// </summary>
    /// <remarks>
    /// The question §3 exists to make askable. False whenever the separation is inside the
    /// uncertainty, and false whenever there is no uncertainty to compare against — an unmeasured
    /// or single-sample offset cannot order anything, and answering true there would hand a caller
    /// the guarantee this type was built to withhold.
    /// <para>
    /// A separation of zero is never orderable: simultaneous readings of two clocks say nothing
    /// about which event happened first.
    /// </para>
    /// </remarks>
    public bool CanOrder(double separationSec)
    {
        if (!IsBounded) return false;
        if (!double.IsFinite(separationSec)) return false;

        return Math.Abs(separationSec) > SpreadSec!.Value;
    }

    /// <summary>A sentence an operator can read, for the banner and for a report.</summary>
    public string Describe() => Samples switch
    {
        0 => "clock offset not measured",
        1 => $"clock offset {OffsetSec:0.###}s from one observation -- no error bar, so nothing "
             + "on this node can be ordered against another",
        _ => $"clock offset {OffsetSec:0.###}s +/- {SpreadSec!.Value:0.###}s over {Samples} "
             + "observations (a lower bound: one-way transit is not separable from the offset)"
    };
}
