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
    private void BtnTogglePlay_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            BtnTogglePlay.Content = "⏸️ 일시정지 (Pause)";
            BtnTogglePlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00));
            _playbackTimer.Start();
        }
        else
        {
            BtnTogglePlay.Content = "▶️ 재생 (Play)";
            BtnTogglePlay.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x9D));
            _playbackTimer.Stop();
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        double step = 0.1 * _playbackSpeed;
        double nextVal = SliderTimeline.Value + step;
        if (nextVal >= 0.0)
        {
            SliderTimeline.Value = 0.0;
            _isPlaying = false;
            BtnTogglePlay.Content = "▶️ 재생 (Play)";
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
        BtnTogglePlay.Content = "▶️ 재생 (Play)";
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
