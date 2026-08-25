using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>How this sample's arrival relates to the moment it describes.</summary>
public enum ArrivalKind
{
    /// <summary>Observed on this machine. One clock, so the question does not arise.</summary>
    Local,

    /// <summary>
    /// It crossed a network, and nothing can be said about how old it is.
    /// </summary>
    /// <remarks>
    /// The state that must not be confused with <see cref="Prompt"/>. Without a bounded offset
    /// between the two clocks, a peer running three hours behind and a sample that spent three
    /// hours in a buffer produce the identical arithmetic, and no amount of looking at the numbers
    /// separates them.
    /// </remarks>
    Undetermined,

    /// <summary>Determined, and no older than the uncertainty can distinguish from immediate.</summary>
    Prompt,

    /// <summary>Determined, and provably older than that.</summary>
    Late
}

/// <summary>
/// Whether a sample arrived promptly or was carried, and whether that is knowable at all.
/// </summary>
/// <remarks>
/// ARCHITECTURE §4: "backfilled data is marked as late-arriving. A sample that took four hours to
/// arrive and one that arrived instantly are different facts, and an alert threshold crossed four
/// hours ago that only surfaces now must not be presented as current."
/// <para>
/// This is the first consumer of <see cref="ClockOffsetEstimate.CanOrder"/>, and the dependency is
/// not incidental — it is why §3 has to be settled before §4 can mean anything. Age is
/// <c>(arrival − the sender's clock) − offset</c>, so it is only as good as the offset, and an
/// unbounded offset makes a three-hour-slow peer indistinguishable from a three-hour-old sample.
/// </para>
/// <para>
/// The arithmetic works out cleanly. The offset estimate is the minimum observation, which already
/// absorbs the fastest transit, so for an ordinary sample the residual age lands between zero and
/// the spread — inside the noise, and reported as <see cref="ArrivalKind.Prompt"/>. A sample that
/// sat in a buffer over a partition lands far outside it. The threshold is therefore not a constant
/// anybody chose: it is what this link's own timing variability will support, which is the same
/// shape as the forecast bound in the worked example at the end of ARCHITECTURE.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the four situations this is.</param>
/// <param name="LateBySec">
/// How much older than the instant it describes, or null when that was not determinable. Present
/// for <see cref="ArrivalKind.Prompt"/> too, where it is the residual inside the noise.
/// </param>
/// <param name="UncertaintySec">
/// The spread the judgement was made against, or null when there was none to make it against.
/// </param>
public readonly record struct ArrivalAge(ArrivalKind Kind, double? LateBySec, double? UncertaintySec)
{
    /// <summary>Nothing crossed a network to get here.</summary>
    public static ArrivalAge Local { get; } = new(ArrivalKind.Local, null, null);

    /// <summary>Whether this host is willing to say the sample describes a past that has passed.</summary>
    public bool IsLate => Kind == ArrivalKind.Late;

    /// <summary>Whether an age was established at all.</summary>
    public bool IsDetermined => Kind is ArrivalKind.Prompt or ArrivalKind.Late;

    /// <summary>Works out how old a sample was by the time it arrived.</summary>
    /// <param name="observedAt">The sending node's own clock, or null when there was none.</param>
    /// <param name="arrivedUtc">This host's clock when the sample landed.</param>
    /// <param name="offset">What is known about the difference between the two clocks.</param>
    /// <remarks>
    /// Returns <see cref="ArrivalKind.Undetermined"/> rather than guessing whenever the offset
    /// carries no error bar — including the case where exactly one observation exists, which
    /// produces an offset and no way to judge it. Answering <see cref="ArrivalKind.Prompt"/> there
    /// would be the confident zero this project exists to refuse, arriving by a longer route.
    /// </remarks>
    public static ArrivalAge Determine(
        DateTime? observedAt, DateTime arrivedUtc, ClockOffsetEstimate offset)
    {
        if (observedAt is not { } observed) return Local;
        if (!offset.IsBounded) return new ArrivalAge(ArrivalKind.Undetermined, null, offset.SpreadSec);

        // (arrival - sent) is offset + however long the sample was held plus transit. Subtracting
        // the offset leaves the holding, which is the thing §4 is about.
        double ageSec = (arrivedUtc - observed).TotalSeconds - offset.OffsetSec;
        if (!double.IsFinite(ageSec)) return new ArrivalAge(ArrivalKind.Undetermined, null, offset.SpreadSec);

        // CanOrder is exactly the right question: can this separation be told apart from zero,
        // given what is known about the two clocks. A negative age -- the sample appearing to
        // predate its own observation -- is as much a real ordering as a positive one, and it means
        // the offset moved under us rather than that the sample is fresh.
        return new ArrivalAge(
            offset.CanOrder(ageSec) && ageSec > 0 ? ArrivalKind.Late : ArrivalKind.Prompt,
            ageSec,
            offset.SpreadSec);
    }

    /// <summary>A sentence for a banner, a relay or an incident report.</summary>
    public string Describe() => Kind switch
    {
        ArrivalKind.Local => "observed on this machine",
        ArrivalKind.Undetermined =>
            "arrived over a network with no bounded clock offset, so its age cannot be established "
            + "-- a slow peer and a held sample look identical here",
        ArrivalKind.Late =>
            $"describes an instant {LateBySec:0.###}s before it arrived, which is outside the "
            + $"+/-{UncertaintySec:0.###}s this link's timing can account for",
        _ => $"arrived within {UncertaintySec:0.###}s of the instant it describes"
    };
}
