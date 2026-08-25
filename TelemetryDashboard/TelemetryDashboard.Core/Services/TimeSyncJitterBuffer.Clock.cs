using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Estimating how far another node's clock is from this one's, with an error bar.
/// </summary>
/// <remarks>
/// Split from the buffering half because they answer different questions. That one places a value
/// on a timeline; this one says how much the timeline itself can be trusted.
/// <para>
/// ARCHITECTURE §3 is about this file and it was unmet. The estimator existed, produced an
/// exponential moving average of the measured offsets, and reported it as a bare double — with
/// 0.0 for a node nobody had ever compared clocks with. Two separate versions of the same defect:
/// a value nobody measured presented as a measurement, and a measurement presented without the
/// thing that makes it usable.
/// </para>
/// </remarks>
public partial class TimeSyncJitterBuffer
{
    /// <summary>How many recent observations an estimate rests on.</summary>
    /// <remarks>
    /// Long enough that the spread reflects more than a lucky pair, short enough to follow a clock
    /// that is drifting: an estimate computed over an unbounded history keeps quoting a minimum
    /// that stopped being reachable an hour ago, and the error bar stops covering the truth
    /// without ever getting wider.
    /// </remarks>
    public const int MaxClockObservations = 64;

    /// <summary>Records one comparison between our clock and the node's.</summary>
    /// <remarks>
    /// A non-finite reading is dropped rather than recorded. Under §7 what arrives from elsewhere
    /// is data and is not trusted: a peer sending a NaN timestamp must not be able to poison the
    /// minimum below and take an entire node's timeline with it, and a frame that fails its
    /// checksum is already dropped rather than scraped for numbers.
    /// </remarks>
    public void SyncNodeClock(string nodeId, double masterTime, double nodeTime)
    {
        double observed = masterTime - nodeTime;
        if (!double.IsFinite(observed)) return;

        var nodeBuf = _buffers.GetOrAdd(nodeId, _ => new NodeBuffer());
        lock (nodeBuf.Lock)
        {
            nodeBuf.ClockObservations.Enqueue(observed);
            while (nodeBuf.ClockObservations.Count > MaxClockObservations)
            {
                nodeBuf.ClockObservations.Dequeue();
            }

            nodeBuf.Offset = Estimate(nodeBuf.ClockObservations);
        }
    }

    /// <summary>Every node this host has been able to compare clocks with.</summary>
    /// <remarks>
    /// Only nodes with at least one observation. A node that is reporting but whose samples carry
    /// no clock of their own is absent rather than present with an unmeasured offset -- the caller
    /// asking "whose clocks do I know" is asking about the observations, and every other node in
    /// the fleet would answer Unmeasured, which is a longer way of saying nothing.
    /// </remarks>
    public IReadOnlyList<NodeClock> ObservedClocks()
    {
        var known = new List<NodeClock>();

        foreach (var pair in _buffers)
        {
            ClockOffsetEstimate estimate = Read(pair.Value);
            if (estimate.HasOffset) known.Add(new NodeClock(pair.Key, estimate));
        }

        known.Sort((left, right) => string.CompareOrdinal(left.NodeId, right.NodeId));
        return known;
    }

    /// <inheritdoc />
    public ClockOffsetEstimate GetClockOffset(string nodeId) =>
        _buffers.TryGetValue(nodeId, out var nodeBuf) ? Read(nodeBuf) : ClockOffsetEstimate.Unmeasured;

    private static ClockOffsetEstimate Read(NodeBuffer nodeBuf)
    {
        lock (nodeBuf.Lock)
        {
            return nodeBuf.Offset;
        }
    }

    /// <summary>Turns a window of observations into an offset and the spread around it.</summary>
    /// <remarks>
    /// The point estimate is the <b>minimum</b>, not the mean, and that is the whole of what these
    /// one-way messages allow. Each observation is <c>offset + transit</c> for some transit that
    /// cannot be negative, so every one of them overstates the offset by however long that message
    /// took. The smallest is the least overstated, and the mean — which is what the EMA here used
    /// to compute — is worse by exactly the average transit. NTP filters on the minimum for this
    /// reason and the reasoning does not depend on having a round trip.
    /// <para>
    /// The spread is taken over the <b>lowest quarter</b> of the window rather than all of it, and
    /// that is not a refinement — <c>max - min</c> was wrong. An observation is
    /// <c>offset + transit</c>, and a sample that sat in a buffer through a network partition
    /// arrives with that whole holding time inside its transit: four hours of it. One such sample
    /// puts <c>max</c> at fourteen thousand seconds and the error bar swallows everything, so
    /// nothing can ever be called late again — the backfill hides itself, and it hides it in the
    /// one statistic that was supposed to reveal it.
    /// </para>
    /// <para>
    /// The minimum was never vulnerable to that, because a held sample is large. Restricting the
    /// spread to the observations nearest the minimum applies the same reasoning to it: those are
    /// the transit-dominated ones, and they are what the link's own timing variability looks like.
    /// It degrades honestly, too. If most of a window really is backfill, the quartile is made of
    /// held samples and the uncertainty goes up — which on a link that is mostly backfill is the
    /// truth rather than a failure.
    /// </para>
    /// <para>
    /// Below two observations there is no spread, and it is reported as absent rather than as
    /// zero. One measurement genuinely says nothing about its own precision, and a zero error bar
    /// is the strongest claim this type can make — which would make a single sample the most
    /// confident state in the system.
    /// </para>
    /// </remarks>
    private static ClockOffsetEstimate Estimate(Queue<double> observations)
    {
        int count = observations.Count;
        if (count == 0) return ClockOffsetEstimate.Unmeasured;
        if (count == 1) return new ClockOffsetEstimate(observations.Peek(), null, 1);

        double[] sorted = observations.ToArray();
        Array.Sort(sorted);

        // At least two, so a spread exists at all; a quarter of the window once there is enough of
        // one for the choice to matter.
        int nearest = Math.Max(2, count / 4);
        return new ClockOffsetEstimate(sorted[0], sorted[nearest - 1] - sorted[0], count);
    }
}
