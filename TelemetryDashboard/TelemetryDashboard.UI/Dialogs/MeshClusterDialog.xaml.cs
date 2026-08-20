using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Infrastructure.Network;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>One discovered peer hub, as the mesh transport actually knows it.</summary>
/// <remarks>
/// The previous model carried a <c>Latency</c> string, which nothing in the mesh measures — the
/// dialog filled it with "0.8 ms", "1.4 ms" and "2.1 ms" for three hubs that did not exist. There
/// is no round-trip timing on this transport, so there is no latency column: every property here
/// is copied from a <see cref="PeerNodeInfo"/> the listener built from a received datagram.
/// </remarks>
public class PeerHubDisplayModel
{
    public string PeerId { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;

    /// <summary>Source address and port of the datagram that last announced this peer.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Age of the last authenticated datagram from this peer.</summary>
    public string LastSeen { get; set; } = string.Empty;

    /// <summary>Liveness as the transport defines it, not a synchronisation claim.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Peer discovery and WebRTC signalling for the local cluster mesh.
/// </summary>
/// <remarks>
/// The dialog owns a <see cref="P2PMeshClusterSync"/> that was never started, and populated its grid
/// from a literal list of three hubs while the footer reported "3 Peers Connected". Nothing on the
/// screen came from the network. Discovery is now an explicit action whose result the operator can
/// see: the grid holds the listener's <see cref="P2PMeshClusterSync.KnownPeers"/> and nothing else,
/// and it is empty until a peer answers.
/// <para>
/// The security banner reports <see cref="P2PMeshClusterSync.SecurityMode"/> rather than the word
/// "zero-trust". Without a cluster passphrase the codec runs in <see cref="MeshSecurityMode.Unsecured"/>
/// and any host on the segment can read the traffic and inject forged peers; the passphrase field is
/// what turns that claim into something enforced, and it has to be set before the listener starts.
/// </para>
/// </remarks>
public partial class MeshClusterDialog : Window
{
    /// <summary>How often the peer table is re-read while discovery is running.</summary>
    private static readonly TimeSpan PeerRefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>Segoe Fluent Icons checkmark, written as an escape so the source stays ASCII.</summary>
    private const string CheckmarkGlyph = "\uE73E";

    /// <summary>Segoe Fluent Icons warning triangle.</summary>
    private const string WarningGlyph = "\uE7BA";

    private readonly P2PMeshClusterSync _meshSync = new();
    private readonly WebRtcTelemetryBridge _webRtcBridge = new();
    private readonly DispatcherTimer _peerRefreshTimer;

    private bool _discoveryRunning;

    public MeshClusterDialog()
    {
        InitializeComponent();

        _peerRefreshTimer = new DispatcherTimer { Interval = PeerRefreshInterval };
        _peerRefreshTimer.Tick += (_, _) => LoadPeers();

        TxtListenPort.Text = _meshSync.ListenPort.ToString(CultureInfo.InvariantCulture);
        ShowSecurityMode();
        LoadPeers();
    }

    /// <summary>Starts or stops the UDP listener, reporting whatever actually happened.</summary>
    private async void BtnToggleDiscovery_Click(object sender, RoutedEventArgs e)
    {
        if (_discoveryRunning)
        {
            await _meshSync.StopAsync();
            _peerRefreshTimer.Stop();
            _discoveryRunning = false;
            SetDiscoveryChrome();
            LoadPeers();
            return;
        }

        if (!int.TryParse(TxtListenPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            || port < 1 || port > 65535)
        {
            TxtMeshStatus.Text = "수신 포트는 1–65535 사이의 숫자여야 합니다.";
            return;
        }

        // The passphrase has to be applied before the socket opens; afterwards the codec is fixed
        // for the lifetime of the listener.
        string passphrase = TxtClusterPassphrase.Text.Trim();
        if (passphrase.Length > 0)
        {
            _meshSync.UseClusterPassphrase(passphrase);
        }

        try
        {
            await _meshSync.StartAsync(port);
            _discoveryRunning = _meshSync.IsRunning;
        }
        catch (Exception ex)
        {
            // A port already held by another process is the common case, and it must not be
            // reported as a running mesh.
            _discoveryRunning = false;
            TxtMeshStatus.Text = $"포트 {port} 수신 실패: {ex.Message}";
            SetDiscoveryChrome();
            return;
        }

        ShowSecurityMode();
        SetDiscoveryChrome();
        _peerRefreshTimer.Start();
        LoadPeers();
    }

    /// <summary>Reflects the listener's state in the controls that depend on it.</summary>
    private void SetDiscoveryChrome()
    {
        BtnToggleDiscovery.Content = _discoveryRunning ? "탐색 중지" : "탐색 시작";
        BtnBroadcast.IsEnabled = _discoveryRunning;
        TxtListenPort.IsEnabled = !_discoveryRunning;
        TxtClusterPassphrase.IsEnabled = !_discoveryRunning;
    }

    /// <summary>
    /// States whether mesh frames are authenticated, using the codec's own mode.
    /// </summary>
    private void ShowSecurityMode()
    {
        bool encrypted = _meshSync.SecurityMode == MeshSecurityMode.Encrypted;

        SolidColorBrush accent = (SolidColorBrush)FindResource(encrypted ? "SuccessBrush" : "WarningBrush");

        SecurityBanner.BorderBrush = accent;
        SecurityBanner.Background = (Brush)FindResource(encrypted ? "SuccessSubtleBrush" : "WarningSubtleBrush");
        SecurityIcon.Foreground = accent;
        SecurityIcon.Text = encrypted ? CheckmarkGlyph : WarningGlyph;
        TxtSecurityState.Foreground = accent;
        TxtSecurityState.Text = encrypted
            ? "클러스터 암호구절 적용됨 — 프레임이 AES-256-GCM으로 암호화되고 서명 검증을 거칩니다."
            : "암호구절 없음 — 메시 프레임이 암호화·인증되지 않습니다. 같은 구간의 어떤 호스트도 내용을 읽고 피어를 위조할 수 있습니다.";
    }

    /// <summary>
    /// Fills the grid from the listener's peer table.
    /// </summary>
    /// <remarks>
    /// An empty table is left empty. It previously stood in for "not yet discovered" with three
    /// scripted hubs, so a mesh that had never sent a packet looked identical to a healthy cluster.
    /// </remarks>
    private void LoadPeers()
    {
        IReadOnlyCollection<PeerNodeInfo> peers = _meshSync.KnownPeers;

        List<PeerHubDisplayModel> rows = peers
            .OrderByDescending(p => p.LastSeen)
            .Select(Describe)
            .ToList();

        DgPeers.ItemsSource = rows;

        int live = peers.Count(p => p.IsActive);

        TxtPeerSummary.Text = rows.Count == 0
            ? (_discoveryRunning
                ? "아직 응답한 피어가 없습니다. 같은 포트로 수신 중인 허브만 발견됩니다."
                : "탐색이 중지된 상태입니다. 표시할 피어 정보가 없습니다.")
            : $"피어 {rows.Count}개 · 최근 30초 내 수신 {live}개";

        TxtMeshStatus.Text = _discoveryRunning
            ? $"로컬 피어 {_meshSync.LocalPeerId} · UDP {_meshSync.ListenPort} 수신 중 · " +
              $"거부된 프레임 {_meshSync.RejectedFrameCount}개"
            : $"로컬 피어 {_meshSync.LocalPeerId} · 탐색이 시작되지 않았습니다.";
    }

    /// <summary>Projects a peer record onto its row, stating age rather than a sync rate.</summary>
    private static PeerHubDisplayModel Describe(PeerNodeInfo peer)
    {
        TimeSpan age = DateTime.UtcNow - peer.LastSeen;

        return new PeerHubDisplayModel
        {
            PeerId = peer.PeerId,
            HubName = string.IsNullOrWhiteSpace(peer.HubName) ? "—" : peer.HubName,
            Endpoint = $"{peer.IpAddress}:{peer.Port}",
            LastSeen = age.TotalSeconds < 1 ? "방금" : $"{age.TotalSeconds:F0}초 전",
            Status = peer.IsActive ? "수신 중" : "30초 이상 무응답"
        };
    }

    /// <summary>
    /// Sends one heartbeat datagram to the broadcast address.
    /// </summary>
    /// <remarks>
    /// The dialog used to show "Broadcast Heartbeat sent across local subnet" unconditionally.
    /// <see cref="P2PMeshClusterSync.BroadcastSyncPacketAsync"/> returns without doing anything when
    /// the listener is not running, and swallows socket errors when it is, so the message was
    /// asserting a transmission the code had not confirmed. Handing the datagram to the socket is
    /// all this can honestly claim, and delivery is only visible through a peer appearing above.
    /// </remarks>
    private async void BtnBroadcast_Click(object sender, RoutedEventArgs e)
    {
        if (!_meshSync.IsRunning)
        {
            TxtMeshStatus.Text = "탐색이 실행 중일 때만 브로드캐스트할 수 있습니다.";
            return;
        }

        await _meshSync.BroadcastSyncPacketAsync("HEARTBEAT", new
        {
            status = "ONLINE",
            peerCount = _meshSync.KnownPeers.Count
        });

        TxtMeshStatus.Text =
            $"{DateTime.Now:HH:mm:ss} 하트비트를 브로드캐스트 주소로 전송했습니다 (UDP {_meshSync.ListenPort}). " +
            "수신 여부는 위 목록에 피어가 나타나는 것으로만 확인됩니다.";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadPeers();
    }

    /// <summary>Creates a peer connection and copies its SDP offer.</summary>
    private async void BtnCopyWebRtcOffer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WebRtcSessionDescription offer = await _webRtcBridge.CreateOfferAsync("local_client");
            Clipboard.SetText(offer.Sdp);

            // Negotiating an offer is not a connection. The open-channel count is the only figure
            // that says whether anything is actually carrying traffic.
            TxtMeshStatus.Text =
                $"SDP 오퍼를 클립보드에 복사했습니다. 열린 데이터 채널 {_webRtcBridge.ActiveDataChannelCount}개 / " +
                $"생성된 피어 연결 {_webRtcBridge.RegisteredPeerCount}개.";
        }
        catch (Exception ex)
        {
            TxtMeshStatus.Text = $"SDP 오퍼 생성 실패: {ex.Message}";
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _peerRefreshTimer.Stop();
        _meshSync.Dispose();

        // Any peer connection opened by the offer button belongs to this window; closing it here
        // stops the ICE agent rather than leaving it running for the life of the process.
        _ = _webRtcBridge.DisposeAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
