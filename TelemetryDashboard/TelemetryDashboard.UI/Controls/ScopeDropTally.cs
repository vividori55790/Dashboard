using System;
using System.Globalization;
using System.Threading;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// What the scope did not draw, and why.
/// </summary>
/// <remarks>
/// The scope discarded samples down three separate paths and counted none of them, so a chart that
/// was missing data looked exactly like a chart that was not. That is the failure this product
/// names everywhere else — <c>IngestRateGuard</c>'s own remarks say dropping is announced rather
/// than done silently — and the panel an operator watches most was the one place doing the
/// opposite.
/// <para>
/// The idea is taken from <c>ScopeViewModel</c>, which held a valid-point count beside a total one
/// and documented the pair as a decode-health indicator, and which nothing ever constructed. Its
/// other premise did not survive reading: it guards every buffer with a lock because "samples
/// arrive on serial and parser threads while the dispatcher re-reads the same buffers", and this
/// control never lets an ingest thread near its buffers at all — it hands off through a
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> and mutates only on the
/// dispatcher. So the class is gone and the counting is here.
/// </para>
/// <para>
/// Counters are interlocked because the non-finite path is reached from whichever thread pushed the
/// sample, while the rest run on the dispatcher.
/// </para>
/// </remarks>
public sealed class ScopeDropTally
{
    private long _nonFinite;
    private long _overflowed;
    private long _beyondChannelCap;
    private long _whilePaused;

    /// <summary>Readings that were not a number, so nothing could be plotted for them.</summary>
    /// <remarks>
    /// A decode fault, a sensor answering NaN, a divide by zero upstream. Silently skipping these
    /// is the difference between "this channel is quiet" and "this channel is talking nonsense",
    /// and an operator cannot tell those apart from a flat trace.
    /// </remarks>
    public long NonFinite => Interlocked.Read(ref _nonFinite);

    /// <summary>Samples discarded because the queue was full before the batch timer drained it.</summary>
    public long Overflowed => Interlocked.Read(ref _overflowed);

    /// <summary>
    /// Samples for channels beyond the plot's ceiling, which are never drawn at all.
    /// </summary>
    /// <remarks>
    /// The one most likely to bite. A rig reporting more channels than the scope will hold loses
    /// whole channels rather than samples, and derived channels multiply the count — a ten-channel
    /// rig with intervals and drift turned on reports thirty.
    /// </remarks>
    public long BeyondChannelCap => Interlocked.Read(ref _beyondChannelCap);

    /// <summary>Samples that arrived while the operator had the scope paused.</summary>
    /// <remarks>
    /// Counted but reported separately from the rest: this one the operator asked for, and folding
    /// it in with the losses they did not ask for would make every pause look like a fault.
    /// </remarks>
    public long WhilePaused => Interlocked.Read(ref _whilePaused);

    /// <summary>Everything lost that nobody asked to lose.</summary>
    public long Unintended => NonFinite + Overflowed + BeyondChannelCap;

    public void CountNonFinite() => Interlocked.Increment(ref _nonFinite);

    public void CountOverflowed() => Interlocked.Increment(ref _overflowed);

    public void CountBeyondChannelCap() => Interlocked.Increment(ref _beyondChannelCap);

    public void CountWhilePaused() => Interlocked.Increment(ref _whilePaused);

    /// <summary>Forgets everything, for a cleared plot.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _nonFinite, 0);
        Interlocked.Exchange(ref _overflowed, 0);
        Interlocked.Exchange(ref _beyondChannelCap, 0);
        Interlocked.Exchange(ref _whilePaused, 0);
    }

    /// <summary>
    /// One clause for the scope's readout, or empty when nothing was lost.
    /// </summary>
    /// <remarks>
    /// Empty on a healthy run rather than "0 dropped". A counter that is always on screen stops
    /// being read; one that appears only when it has something to say is a change an eye catches.
    /// </remarks>
    public string Summary()
    {
        if (Unintended == 0) return string.Empty;

        var parts = new System.Collections.Generic.List<string>(3);
        if (BeyondChannelCap > 0) parts.Add($"{BeyondChannelCap:N0} past channel cap");
        if (NonFinite > 0) parts.Add($"{NonFinite:N0} not a number");
        if (Overflowed > 0) parts.Add($"{Overflowed:N0} queue overflow");

        return string.Create(CultureInfo.InvariantCulture,
            $" | dropped {Unintended:N0} ({string.Join(", ", parts)})");
    }
}
