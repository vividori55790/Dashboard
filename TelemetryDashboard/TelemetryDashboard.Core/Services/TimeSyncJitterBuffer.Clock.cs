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
    /// The spread is <c>max - min</c>: how much transit varied across the window. It is a floor
    /// under the real uncertainty rather than the whole of it, because the fastest message still
    /// took some unmeasured time, and <see cref="ClockOffsetEstimate"/> says so where a caller
    /// will read it.
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

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (double observed in observations)
        {
            if (observed < min) min = observed;
            if (observed > max) max = observed;
        }

        return new ClockOffsetEstimate(min, count >= 2 ? max - min : null, count);
    }
}
