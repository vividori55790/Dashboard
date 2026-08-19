using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Infrastructure.Network;

/// <summary>
/// WebRTC data-channel transport for ultra-low-latency telemetry delivery.
/// </summary>
/// <remarks>
/// Backed by a real <see cref="RTCPeerConnection"/>: SDP is produced by the ICE/DTLS stack rather
/// than hand-written, and frames traverse an SCTP data channel. The previous bridge emitted a
/// fixed SDP string and its broadcast method serialised each frame and then dropped it on the
/// floor, so a connected client received nothing while every status surface reported success.
/// <para>
/// Peers register with the streaming hub through <see cref="ITelemetrySubscriber"/>, so WebRTC
/// delivery uses the same fan-out path as WebSocket and SSE.
/// </para>
/// </remarks>
public sealed class WebRtcTelemetryBridge : IWebRtcDataBridge, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, WebRtcPeer> _peers = new();
    private readonly TelemetryBroadcastHub? _hub;

    public WebRtcTelemetryBridge(TelemetryBroadcastHub? hub = null)
    {
        _hub = hub;
    }

    /// <summary>STUN servers used for candidate gathering.</summary>
    public IReadOnlyList<string> StunServers { get; init; } = new[] { "stun:stun.l.google.com:19302" };

    public bool IsSignalingActive => true;

    /// <summary>
    /// Data channels that are actually open and carrying traffic.
    /// This stays zero until a real peer completes ICE and DTLS — negotiating an offer is not
    /// a connection, and reporting it as one is what made the previous bridge look functional.
    /// </summary>
    public int ActiveDataChannelCount => _peers.Values.Count(p => p.IsOpen);

    /// <summary>Peer connections created and awaiting or holding a session.</summary>
    public int RegisteredPeerCount => _peers.Count;

    /// <summary>Creates a peer connection with a telemetry data channel and returns its SDP offer.</summary>
    public async Task<WebRtcSessionDescription> CreateOfferAsync(string clientId)
    {
        await ClosePeerAsync(clientId).ConfigureAwait(false);

        var config = new RTCConfiguration
        {
            iceServers = StunServers.Select(url => new RTCIceServer { urls = url }).ToList()
        };

        var connection = new RTCPeerConnection(config);
        RTCDataChannel channel = await connection.createDataChannel("telemetry", new RTCDataChannelInit
        {
            ordered = true
        }).ConfigureAwait(false);

        var peer = new WebRtcPeer(clientId, connection, channel);
        _peers[clientId] = peer;

        connection.onconnectionstatechange += state =>
        {
            if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed
                or RTCPeerConnectionState.disconnected)
            {
                _ = ClosePeerAsync(clientId);
            }
        };

        if (_hub is not null)
        {
            channel.onopen += () => _hub.Add(peer);
            channel.onclose += () => _ = _hub.RemoveAsync(peer.Id);
        }

        RTCSessionDescriptionInit offer = connection.createOffer();
        await connection.setLocalDescription(offer).ConfigureAwait(false);

        return new WebRtcSessionDescription { Type = "offer", Sdp = offer.sdp };
    }

    public Task ProcessAnswerAsync(string clientId, WebRtcSessionDescription answer)
    {
        if (answer is null || !_peers.TryGetValue(clientId, out WebRtcPeer? peer))
        {
            return Task.CompletedTask;
        }

        peer.Connection.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = answer.Sdp
        });

        return Task.CompletedTask;
    }

    public Task AddIceCandidateAsync(string clientId, WebRtcIceCandidate candidate)
    {
        if (candidate is null || !_peers.TryGetValue(clientId, out WebRtcPeer? peer))
        {
            return Task.CompletedTask;
        }

        peer.Connection.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = (ushort)Math.Max(0, candidate.SdpMLineIndex)
        });

        return Task.CompletedTask;
    }

    /// <summary>Sends a frame to every open data channel.</summary>
    public async Task BroadcastDataChannelAsync(string channelLabel, object data)
    {
        if (_peers.IsEmpty) return;

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(data);
        var deliveries = new List<Task>(_peers.Count);

        foreach (WebRtcPeer peer in _peers.Values)
        {
            deliveries.Add(peer.SendAsync(payload, CancellationToken.None));
        }

        await Task.WhenAll(deliveries).ConfigureAwait(false);
    }

    public async Task ClosePeerAsync(string clientId)
    {
        if (!_peers.TryRemove(clientId, out WebRtcPeer? peer)) return;

        if (_hub is not null) await _hub.RemoveAsync(peer.Id).ConfigureAwait(false);
        await peer.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string id in _peers.Keys.ToList())
        {
            await ClosePeerAsync(id).ConfigureAwait(false);
        }
    }

    /// <summary>One connected WebRTC client, exposed to the hub as an ordinary subscriber.</summary>
    private sealed class WebRtcPeer : ITelemetrySubscriber
    {
        private readonly RTCDataChannel _channel;

        public WebRtcPeer(string id, RTCPeerConnection connection, RTCDataChannel channel)
        {
            Id = id;
            Connection = connection;
            _channel = channel;
        }

        public string Id { get; }

        public string Transport => "webrtc";

        public RTCPeerConnection Connection { get; }

        public bool IsOpen => _channel.readyState == RTCDataChannelState.open;

        public bool IsConnected => IsOpen;

        public Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
        {
            if (!IsOpen) return Task.CompletedTask;

            try
            {
                _channel.send(utf8Payload.ToArray());
            }
            catch (Exception ex) when (ex is ApplicationException or InvalidOperationException or ObjectDisposedException)
            {
                // Channel closed underneath us; the hub evicts the subscriber on the next sweep.
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            try { _channel.close(); } catch (InvalidOperationException) { }
            try { Connection.close(); } catch (InvalidOperationException) { }
            return ValueTask.CompletedTask;
        }
    }
}
