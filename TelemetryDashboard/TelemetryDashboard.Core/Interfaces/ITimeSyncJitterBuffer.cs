using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

public interface ITimeSyncJitterBuffer
{
    void EnqueueSample(string nodeId, double timestamp, double value);
    /// <summary>The node's value at <paramref name="masterTimestamp"/>, and how it was obtained.</summary>
    /// <remarks>
    /// Replaces a bare double that returned 0.0 for "this node has sent nothing" -- which is also a
    /// perfectly ordinary reading -- and silently clamped to the nearest sample for any instant
    /// outside the buffer, so a request an hour past the last sample came back as that sample with
    /// nothing to say it was stale.
    /// </remarks>
    AlignedSample GetAligned(string nodeId, double masterTimestamp);
    void SyncNodeClock(string nodeId, double masterTime, double nodeTime);

    /// <summary>How far this node's clock is from ours, and how well that is known.</summary>
    /// <remarks>
    /// Was a bare double, with the same defect <see cref="GetAligned"/> was already fixed for: it
    /// answered 0.0 for a node nobody had ever compared clocks with, which is indistinguishable
    /// from two clocks agreeing perfectly. It also carried no error bar, and ARCHITECTURE §3 is
    /// entirely about the error bar — an offset places a sample, an uncertainty is what says
    /// whether two samples can be ordered at all.
    /// </remarks>
    ClockOffsetEstimate GetClockOffset(string nodeId);
    void ClearBuffer(string nodeId);
}
