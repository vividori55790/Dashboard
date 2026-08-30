using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>How an attempt to recover a missed interval ended.</summary>
public enum GapFillOutcome
{
    /// <summary>Nobody asked. The default, and what an operator sees without <c>--backfill</c>.</summary>
    NotAttempted,

    /// <summary>The peer answered and had samples for the window.</summary>
    Filled,

    /// <summary>The peer answered and had nothing. Not the same as not asking.</summary>
    NothingThere,

    /// <summary>
    /// The peer keeps no archive, so it cannot answer for a time that has passed.
    /// </summary>
    /// <remarks>
    /// Distinguished from <see cref="NothingThere"/> because the operator's next action differs
    /// entirely: one means the plant was quiet, the other means the peer needs <c>--archive</c>.
    /// </remarks>
    SenderHasNoArchive,

    /// <summary>The peer could not be reached, or refused.</summary>
    Unreachable,

    /// <summary>
    /// The outage was longer than this host is willing to pull in one go.
    /// </summary>
    /// <remarks>
    /// A bound rather than a failure, and reported as its own outcome so it is not read as the
    /// peer having nothing. A four-hour partition against a 20 Hz rig is a quarter of a million
    /// samples, which is a decision about someone's bandwidth and memory rather than one this
    /// should make silently on their behalf.
    /// </remarks>
    TooLong
}

/// <summary>One attempt to recover what an outage cost.</summary>
public readonly record struct GapFill(
    DateTime FromUtc, DateTime ToUtc, GapFillOutcome Outcome, int Recovered, bool Truncated)
{
    /// <summary>The window it covered.</summary>
    public TimeSpan Window => ToUtc - FromUtc;

    /// <summary>A sentence for the console and the banner.</summary>
    public string Describe() => Outcome switch
    {
        GapFillOutcome.Filled when Truncated =>
            $"recovered {Recovered} samples of a {Window.TotalSeconds:0.#}s gap, and the peer's "
            + "answer hit its own limit -- there is more in the gap than arrived",
        GapFillOutcome.Filled => $"recovered {Recovered} samples of a {Window.TotalSeconds:0.#}s gap",
        GapFillOutcome.NothingThere =>
            $"the peer answered for the {Window.TotalSeconds:0.#}s gap and had nothing in it",
        GapFillOutcome.SenderHasNoArchive =>
            "the peer keeps no archive, so nothing can be recovered for any gap -- start it with "
            + "--archive if this link is worth backfilling",
        GapFillOutcome.Unreachable => "the peer could not be asked",
        GapFillOutcome.TooLong =>
            $"the {Window.TotalSeconds:0.#}s gap is longer than this host will pull in one request",
        _ => "no attempt was made"
    };
}

/// <summary>
/// What has been recovered after outages, and what could not be.
/// </summary>
/// <remarks>
/// ARCHITECTURE §4 asks that a node backfill when the link returns. The exchange here is pull — a
/// receiver subscribes to a sender's stream — so the sender has no memory of who was listening and
/// cannot push what they missed. The receiver has to ask, and it can: the outage ledger knows the
/// interval, and <c>/api/history</c> on the sender answers for a time that has passed.
/// <para>
/// Kept separate from the outage ledger even though the two describe the same intervals. An outage
/// is a fact about the link and is recorded whether or not anybody tried to do anything about it;
/// a fill is an action this host took. Merging them would make "no fill" and "no outage" share a
/// rendering, and the first is the one an operator can act on.
/// </para>
/// </remarks>
public sealed class BackfillLedger
{
    /// <summary>How many attempts are kept.</summary>
    public const int Kept = 16;

    private readonly Queue<GapFill> _recent = new();
    private readonly object _gate = new();
    private int _attempts;
    private long _recovered;

    /// <summary>How many gaps this host has tried to fill.</summary>
    public int Attempts { get { lock (_gate) return _attempts; } }

    /// <summary>How many samples it has recovered in total.</summary>
    public long Recovered { get { lock (_gate) return _recovered; } }

    /// <summary>Records one attempt.</summary>
    public void Record(GapFill fill)
    {
        lock (_gate)
        {
            _attempts++;
            _recovered += fill.Recovered;
            _recent.Enqueue(fill);
            while (_recent.Count > Kept) _recent.Dequeue();
        }
    }

    /// <summary>The recent attempts, oldest first.</summary>
    public IReadOnlyList<GapFill> Recent()
    {
        lock (_gate) return new List<GapFill>(_recent);
    }
}
