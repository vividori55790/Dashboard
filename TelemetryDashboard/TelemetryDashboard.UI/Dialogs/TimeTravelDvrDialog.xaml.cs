using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Replay;
using TelemetryDashboard.UI.Diagnostics;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>
/// Time-travel DVR: scrubs the recorded telemetry timeline and renders an incident report for the
/// window under the playhead.
/// </summary>
/// <remarks>
/// The dialog no longer originates any telemetry of its own. The demonstration timeline it falls
/// back to on an empty player lives in <see cref="DemonstrationPayloads"/>, which marks every
/// channel and stamps every frame with a scripted analyzer id, and a window with no frames renders
/// as a window with no frames.
/// </remarks>
public partial class TimeTravelDvrDialog : Window
{
    /// <summary>Width of the snapshot window, in seconds, centred on the playhead.</summary>
    private const double SnapshotWindowSec = 5.0;

    /// <summary>Width of the window the incident report summarises, in seconds.</summary>
    private const double ReportWindowSec = 60.0;

    private readonly TimeTravelDvrPlayer _dvrPlayer;
    private readonly IncidentReportGenerator _reportGen = new();
    private readonly DispatcherTimer _playbackTimer;
    private bool _isPlaying = false;
    private double _playbackSpeed = 1.0;

    /// <summary>Opens the replay dialog over <paramref name="dvrPlayer"/>, or over a private player.</summary>
    /// <remarks>
    /// The demonstration timeline is seeded only into a player this dialog owns. Seeding a caller's
    /// player wrote scripted frames into the application's live ring buffer, where the web console's
    /// <c>/api/dvr/report</c> route counted them alongside real telemetry — the marking made their
    /// origin legible, but the frames were still in the shared recording. Demonstration data must
    /// never enter the live path at all.
    /// </remarks>
    public TimeTravelDvrDialog(TimeTravelDvrPlayer? dvrPlayer = null)
    {
        InitializeComponent();

        bool ownsPlayer = dvrPlayer is null;
        _dvrPlayer = dvrPlayer ?? new TimeTravelDvrPlayer();

        if (ownsPlayer)
        {
            DemonstrationPayloads.SeedDvrTimeline(_dvrPlayer);
        }

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        UpdateSnapshotView(0.0);
        GenerateIncidentReport();
    }

    /// <summary>Reports where the playhead is: live, or a signed offset into the past.</summary>
    /// <remarks>
    /// The two colours are tokens now. They were literal greens and cyans, and "live" is a state
    /// the player is genuinely in, so it takes the success token; anywhere else in the timeline is
    /// not a status at all and simply reads in the ordinary text colour.
    /// </remarks>
    private void SliderTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtTimelineCurrent == null) return;

        double offsetSec = SliderTimeline.Value;
        bool live = offsetSec == 0.0;

        TxtTimelineCurrent.Text = live ? "0.0s (실시간)" : $"{offsetSec:F1}s";
        TxtTimelineCurrent.Foreground =
            (System.Windows.Media.Brush)FindResource(live ? "SuccessBrush" : "TextPrimaryBrush");

        UpdateSnapshotView(offsetSec);
    }

    /// <summary>
    /// Renders the frames recorded around <paramref name="offsetSec"/>, or states that there are none.
    /// </summary>
    /// <remarks>
    /// The status line counts frames rather than rows: the empty window is represented by one row
    /// that carries no measurement, and reporting it as a channel would restore in the summary the
    /// same claim the grid stopped making.
    /// </remarks>
    private void UpdateSnapshotView(double offsetSec)
    {
        double targetTimestamp = (DateTime.UtcNow.Ticks / 10_000_000.0) + offsetSec;
        List<DvrFrame> frames = _dvrPlayer.ExtractSnapshot(targetTimestamp, SnapshotWindowSec);

        DgReplaySnapshot.ItemsSource = ReplaySnapshotRows.Build(frames);
        TxtStatus.Text = frames.Count == 0
            ? $"DVR Replay: no frames recorded at offset {offsetSec:F1}s | Speed: {_playbackSpeed:F1}x"
            : $"DVR Replay: {frames.Count} frames at offset {offsetSec:F1}s | Speed: {_playbackSpeed:F1}x";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _playbackTimer.Stop();
        Close();
    }
}
