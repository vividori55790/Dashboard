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
    double GetClockOffset(string nodeId);
    void ClearBuffer(string nodeId);
}
