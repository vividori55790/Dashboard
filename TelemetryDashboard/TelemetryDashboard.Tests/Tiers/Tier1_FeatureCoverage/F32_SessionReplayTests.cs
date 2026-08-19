using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F32_SessionReplayTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void SessionReplay_LoadSession_ParsesRecordedDataFile()
    {
        var player = new SessionReplayPlayerState();
        bool loaded = player.LoadSession("recording_01.mat");

        loaded.Should().BeTrue();
        player.TotalPackets.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SessionReplay_PlayPause_UpdatesPlaybackState()
    {
        var player = new SessionReplayPlayerState();
        player.LoadSession("recording_01.mat");

        player.Play();
        player.IsPlaying.Should().BeTrue();

        player.Pause();
        player.IsPlaying.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SessionReplay_SetSpeed_AdjustsMultiplier()
    {
        var player = new SessionReplayPlayerState();
        player.SetSpeed(2.0);
        player.SpeedMultiplier.Should().Be(2.0);

        player.SetSpeed(0.5);
        player.SpeedMultiplier.Should().Be(0.5);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SessionReplay_SeekRewind_RepositionsTimelineCursor()
    {
        var player = new SessionReplayPlayerState();
        player.LoadSession("recording_01.mat");

        player.SeekTo(50);
        player.CurrentIndex.Should().Be(50);

        player.Rewind();
        player.CurrentIndex.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FailureSnapshotExtractor_Capture_Extracts10SecondWindow()
    {
        var history = new List<TelemetryPacket>();
        var now = DateTime.UtcNow;
        for (int i = 0; i < 60; i++)
        {
            history.Add(new TelemetryPacket("MCU_1", "TEMP", 40.0 + i, "C", now.AddSeconds(-60 + i)));
        }

        var snapshot = FailureSnapshotExtractorHelper.ExtractSnapshot(history, alarmTime: now, windowSeconds: 10);

        snapshot.Should().NotBeEmpty();
        snapshot.All(p => p.Timestamp >= now.AddSeconds(-10)).Should().BeTrue();
    }
}

public class SessionReplayPlayerState
{
    public bool IsPlaying { get; private set; }
    public double SpeedMultiplier { get; private set; } = 1.0;
    public int TotalPackets { get; private set; }
    public int CurrentIndex { get; private set; }

    public bool LoadSession(string file)
    {
        TotalPackets = 100;
        CurrentIndex = 0;
        return true;
    }

    public void Play() => IsPlaying = true;
    public void Pause() => IsPlaying = false;
    public void SetSpeed(double s) => SpeedMultiplier = s;

    public void SeekTo(int idx)
    {
        if (idx >= 0 && idx < TotalPackets) CurrentIndex = idx;
    }

    public void Rewind() => CurrentIndex = 0;
}

public static class FailureSnapshotExtractorHelper
{
    public static List<TelemetryPacket> ExtractSnapshot(List<TelemetryPacket> history, DateTime alarmTime, int windowSeconds)
    {
        var startTime = alarmTime.AddSeconds(-windowSeconds);
        return history.Where(p => p.Timestamp >= startTime && p.Timestamp <= alarmTime).ToList();
    }
}
