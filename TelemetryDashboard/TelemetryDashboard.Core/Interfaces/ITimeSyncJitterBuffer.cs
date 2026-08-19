namespace TelemetryDashboard.Core.Interfaces;

public interface ITimeSyncJitterBuffer
{
    void EnqueueSample(string nodeId, double timestamp, double value);
    double GetAlignedSample(string nodeId, double masterTimestamp);
    void SyncNodeClock(string nodeId, double masterTime, double nodeTime);
    double GetClockOffset(string nodeId);
    void ClearBuffer(string nodeId);
}
