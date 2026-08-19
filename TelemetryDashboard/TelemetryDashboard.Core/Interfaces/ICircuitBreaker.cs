using System;

namespace TelemetryDashboard.Core.Interfaces;

public interface ICircuitBreaker
{
    int MaxAllowedRatePerSec { get; set; }
    bool IsUiResourceClamped { get; }
    int SubsampleRatio { get; }

    event EventHandler<string>? ChannelIsolated;
    event EventHandler<string>? ChannelRestored;

    bool AllowPacketProcessing(string channelId);
    void ReportPacketRate(string channelId, int packetsPerSecond);
    void RecordPacket(string channelId);
    bool IsChannelIsolated(string channelId);
}
