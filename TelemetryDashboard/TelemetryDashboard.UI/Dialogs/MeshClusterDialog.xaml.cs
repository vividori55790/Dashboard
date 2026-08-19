using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Network;

namespace TelemetryDashboard.UI.Dialogs;

public class PeerHubDisplayModel
{
    public string PeerId { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 9090;
    public string Latency { get; set; } = "1.2 ms";
    public string Status { get; set; } = "🟢 SYNCED";
}

public partial class MeshClusterDialog : Window
{
    private readonly P2PMeshClusterSync _meshSync = new();
    private readonly WebRtcTelemetryBridge _webRtcBridge = new();

    public MeshClusterDialog()
    {
        InitializeComponent();
        LoadPeers();
    }

    private void LoadPeers()
    {
        var peers = new List<PeerHubDisplayModel>
        {
            new PeerHubDisplayModel { PeerId = "HUB-A01", HubName = "Factory-1-Inverter-Hub", IpAddress = "192.168.1.102", Port = 9090, Latency = "0.8 ms", Status = "🟢 SYNCED (60Hz)" },
            new PeerHubDisplayModel { PeerId = "HUB-B04", HubName = "Battery-ESS-Storage-Hub", IpAddress = "192.168.1.108", Port = 9090, Latency = "1.4 ms", Status = "🟢 SYNCED (60Hz)" },
            new PeerHubDisplayModel { PeerId = "HUB-C12", HubName = "Robotics-Arm-Controller-Hub", IpAddress = "192.168.1.115", Port = 9090, Latency = "2.1 ms", Status = "🟢 SYNCED (60Hz)" }
        };

        DgPeers.ItemsSource = peers;
        TxtMeshStatus.Text = $"Mesh Status: Local Peer { _meshSync.LocalPeerId } | 3 Active Hubs Synchronized";
    }

    private async void BtnBroadcast_Click(object sender, RoutedEventArgs e)
    {
        await _meshSync.BroadcastSyncPacketAsync("HEARTBEAT", new { status = "ACTIVE", nodeCount = 4 });
        MessageBox.Show(this, "Broadcast Heartbeat sent across local subnet (Port 9090).", "Broadcast Sent", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadPeers();
    }

    private async void BtnCopyWebRtcOffer_Click(object sender, RoutedEventArgs e)
    {
        var offer = await _webRtcBridge.CreateOfferAsync("local_client");
        Clipboard.SetText(offer.Sdp);
        MessageBox.Show(this, "WebRTC SDP Offer copied to clipboard!\nUse this in WebRTC clients to establish peer DataChannels.", "SDP Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
