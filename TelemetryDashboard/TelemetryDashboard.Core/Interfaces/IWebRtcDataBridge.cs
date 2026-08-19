using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Interfaces;

public class WebRtcSessionDescription
{
    public string Type { get; set; } = "offer"; // offer, answer
    public string Sdp { get; set; } = string.Empty;
}

public class WebRtcIceCandidate
{
    public string Candidate { get; set; } = string.Empty;
    public string SdpMid { get; set; } = string.Empty;
    public int SdpMLineIndex { get; set; } = 0;
}

/// <summary>
/// Interface for Ultra-Low Latency WebRTC Data Channel Streaming.
/// Enables P2P low-latency telemetry transmission directly to mobile and browser clients.
/// </summary>
public interface IWebRtcDataBridge
{
    bool IsSignalingActive { get; }
    int ActiveDataChannelCount { get; }

    Task<WebRtcSessionDescription> CreateOfferAsync(string clientId);
    Task ProcessAnswerAsync(string clientId, WebRtcSessionDescription answer);
    Task AddIceCandidateAsync(string clientId, WebRtcIceCandidate candidate);
    Task BroadcastDataChannelAsync(string channelLabel, object data);
}
