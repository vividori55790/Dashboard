using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>Playback transport controls for the time-travel DVR dialog.</summary>
/// <remarks>
/// Kept apart from the snapshot rendering so the controls that only move the playhead cannot
/// accidentally take on the job of deciding what a frame means.
/// </remarks>
public partial class TimeTravelDvrDialog
{
    /// <summary>Starts or stops playback, and says which state the transport is now in.</summary>
    /// <remarks>
    /// The glyph and the caption are separate elements, so the icon font is never asked to render a
    /// caption and the caption is never asked to render an icon. Nothing here assigns a colour:
    /// this button used to be repainted amber while playing and green while stopped, two literal
    /// values that spent status colours on a transport control which is neither alarming nor
    /// reporting anything a machine measured.
    /// </remarks>
    private void BtnTogglePlay_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;

        if (_isPlaying)
        {
            ShowTransport(playing: true);
            _playbackTimer.Start();
        }
        else
        {
            ShowTransport(playing: false);
            _playbackTimer.Stop();
        }
    }

    /// <summary>Pause when it is running, play when it is not.</summary>
    private void ShowTransport(bool playing)
    {
        PlayGlyph.Text = playing ? "" : "";
        PlayLabel.Text = playing ? "일시정지" : "재생";
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        double step = 0.1 * _playbackSpeed;
        double nextVal = SliderTimeline.Value + step;
        if (nextVal >= 0.0)
        {
            SliderTimeline.Value = 0.0;
            _isPlaying = false;
            ShowTransport(playing: false);
            _playbackTimer.Stop();
        }
        else
        {
            SliderTimeline.Value = nextVal;
        }
    }

    private void BtnJumpLive_Click(object sender, RoutedEventArgs e)
    {
        SliderTimeline.Value = 0.0;
        _isPlaying = false;
        _playbackTimer.Stop();
        ShowTransport(playing: false);
    }

    /// <summary>Moves the playhead to the most recent frame an analyzer actually flagged.</summary>
    /// <remarks>
    /// The offset was fixed at -18s, which is where the demonstration timeline happens to place its
    /// scripted excursion and means nothing for a real recording: the button asserted where an
    /// anomaly was without consulting the timeline. Frames without a verdict are not candidates —
    /// nobody examined them, so nobody flagged them — and a timeline containing no flagged frame
    /// leaves the playhead alone and says so rather than moving to an arbitrary moment.
    /// </remarks>
    private void BtnJumpAnomaly_Click(object sender, RoutedEventArgs e)
    {
        double now = DateTime.UtcNow.Ticks / 10_000_000.0;
        DvrFrame? flagged = _dvrPlayer
            .ExtractSnapshot(now - ReportWindowSec / 2.0, ReportWindowSec)
            .LastOrDefault(f => f.HasVerdict && f.IsAnomaly);

        if (flagged is null)
        {
            TxtStatus.Text = "DVR Replay: no flagged frame in the retained timeline";
            return;
        }

        double offsetSec = Math.Clamp(flagged.TimestampSec - now, SliderTimeline.Minimum, SliderTimeline.Maximum);
        SliderTimeline.Value = offsetSec;
        UpdateSnapshotView(offsetSec);
    }

    private void SpeedBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && double.TryParse(btn.Tag?.ToString(), out double speed))
        {
            _playbackSpeed = speed;
            TxtStatus.Text = $"DVR Replay: Speed set to {speed:F1}x";
        }
    }
}
