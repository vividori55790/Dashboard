using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Interfaces;

public class PeerNodeInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9090;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsActive => (DateTime.UtcNow - LastSeen).TotalSeconds < 30.0;
}

public class MeshSyncPacket
{
    public string SourcePeerId { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;
    public string PacketType { get; set; } = "HEARTBEAT"; // HEARTBEAT, TELEMETRY_SYNC, ANOMALY_BROADCAST
    public double TimestampSec { get; set; } = DateTime.UtcNow.Ticks / 10_000_000.0;
    public string PayloadJson { get; set; } = string.Empty;
}

/// <summary>
/// Interface for Multi-Hub P2P Mesh Cluster Synchronization.
/// Enables multiple factory telemetry hubs to synchronize telemetry data, node status, and anomaly records.
/// </summary>
public interface IP2PMeshSync
{
    string LocalPeerId { get; }
    string LocalHubName { get; }
    int ListenPort { get; }
    bool IsRunning { get; }
    IReadOnlyCollection<PeerNodeInfo> KnownPeers { get; }

    event EventHandler<MeshSyncPacket>? PacketReceived;
    event EventHandler<PeerNodeInfo>? PeerDiscovered;

    Task StartAsync(int listenPort = 9090, CancellationToken cancellationToken = default);
    Task StopAsync();
    Task BroadcastSyncPacketAsync(string packetType, object payload);
    Task SendToPeerAsync(string peerId, string packetType, object payload);
}
