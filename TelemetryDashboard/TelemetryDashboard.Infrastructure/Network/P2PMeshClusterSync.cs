using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Infrastructure.Network;

/// <summary>
/// Multi-Hub P2P Distributed Cluster Mesh Synchronization Engine.
/// Automatically discovers peer TelemetryDashboard instances over local network/VPN,
/// maintains peer heartbeat liveness, and synchronizes telemetry frames, node metadata, and anomaly alerts.
/// </summary>
public class P2PMeshClusterSync : IP2PMeshSync, IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, PeerNodeInfo> _peers = new();
    private CancellationTokenSource? _cts;
    private UdpClient? _udpClient;
    private MeshPacketCodec _codec = new();

    public string LocalPeerId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string LocalHubName { get; set; } = "Factory-Hub-1";
    public int ListenPort { get; private set; } = 9090;
    public bool IsRunning { get; private set; } = false;

    public IReadOnlyCollection<PeerNodeInfo> KnownPeers => _peers.Values.ToList();

    /// <summary>Whether mesh traffic is encrypted and authenticated.</summary>
    public MeshSecurityMode SecurityMode => _codec.Mode;

    /// <summary>Frames rejected because they failed authentication or replay checks.</summary>
    public long RejectedFrameCount => Interlocked.Read(ref _rejectedFrames);

    private long _rejectedFrames;

    /// <summary>
    /// Enables encrypted mesh operation using a pre-shared cluster passphrase.
    /// Must be called before <see cref="StartAsync"/>; every hub in the cluster needs the same phrase.
    /// </summary>
    public void UseClusterPassphrase(string passphrase, string clusterName = "TelemetryDashboard")
    {
        _codec = new MeshPacketCodec(MeshPacketCodec.DeriveClusterKey(passphrase, clusterName));
    }

    public event EventHandler<MeshSyncPacket>? PacketReceived;
    public event EventHandler<PeerNodeInfo>? PeerDiscovered;

    public Task StartAsync(int listenPort = 9090, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        ListenPort = listenPort;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, ListenPort));
        _udpClient.EnableBroadcast = true;
        IsRunning = true;

        // Background UDP listener
        _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

        // Background Heartbeat beacon
        _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();
        _udpClient?.Dispose();
        _udpClient = null;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Broadcasts to every hub on the local network segment that listens on the same port.
    /// </summary>
    /// <remarks>
    /// The datagram goes to <see cref="ListenPort"/> — this hub's own port — so **every member of
    /// a cluster must be configured with the same port**. That is the normal shape for UDP
    /// broadcast discovery, but it is an easy constraint to miss: two hubs started on 9091 and
    /// 9092 will both report <see cref="IsRunning"/> and neither will ever hear the other, with no
    /// error anywhere. Use <see cref="SendToPeerAsync"/> when addressing a known peer directly.
    /// </remarks>
    public async Task BroadcastSyncPacketAsync(string packetType, object payload)
    {
        if (!IsRunning || _udpClient == null) return;

        var packet = new MeshSyncPacket
        {
            SourcePeerId = LocalPeerId,
            HubName = LocalHubName,
            PacketType = packetType,
            TimestampSec = DateTime.UtcNow.Ticks / 10_000_000.0,
            PayloadJson = JsonSerializer.Serialize(payload)
        };

        byte[] bytes = _codec.Encode(packet);

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, ListenPort);
        try
        {
            await _udpClient.SendAsync(bytes, bytes.Length, broadcastEndpoint);
        }
        catch { }
    }

    public async Task SendToPeerAsync(string peerId, string packetType, object payload)
    {
        if (!IsRunning || _udpClient == null) return;

        if (_peers.TryGetValue(peerId, out var peer))
        {
            var packet = new MeshSyncPacket
            {
                SourcePeerId = LocalPeerId,
                HubName = LocalHubName,
                PacketType = packetType,
                TimestampSec = DateTime.UtcNow.Ticks / 10_000_000.0,
                PayloadJson = JsonSerializer.Serialize(payload)
            };

            byte[] bytes = _codec.Encode(packet);

            var endpoint = new IPEndPoint(IPAddress.Parse(peer.IpAddress), peer.Port);
            try
            {
                await _udpClient.SendAsync(bytes, bytes.Length, endpoint);
            }
            catch { }
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token);

                // Authenticate before the datagram is allowed to create a peer or raise an event.
                if (!_codec.TryDecode(result.Buffer, out MeshSyncPacket? packet, out _) || packet is null)
                {
                    Interlocked.Increment(ref _rejectedFrames);
                    continue;
                }

                if (packet.SourcePeerId == LocalPeerId)
                {
                    continue; // Ignore loopback packets
                }

                // Update / Discover peer
                bool isNew = !_peers.ContainsKey(packet.SourcePeerId);
                var peer = _peers.GetOrAdd(packet.SourcePeerId, id => new PeerNodeInfo
                {
                    PeerId = id,
                    HubName = packet.HubName,
                    IpAddress = result.RemoteEndPoint.Address.ToString(),
                    Port = result.RemoteEndPoint.Port,
                    LastSeen = DateTime.UtcNow
                });

                peer.LastSeen = DateTime.UtcNow;
                peer.HubName = packet.HubName;

                if (isNew)
                {
                    PeerDiscovered?.Invoke(this, peer);
                }

                PacketReceived?.Invoke(this, packet);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Report only what is actually measured; the previous beacon published a
                // constant "cpu = 12.5" that no peer could distinguish from a real reading.
                await BroadcastSyncPacketAsync("HEARTBEAT", new
                {
                    status = "ONLINE",
                    peerCount = _peers.Count,
                    workingSetMb = Environment.WorkingSet / 1024.0 / 1024.0,
                    timestamp = DateTime.UtcNow
                });
                await Task.Delay(5000, token);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
