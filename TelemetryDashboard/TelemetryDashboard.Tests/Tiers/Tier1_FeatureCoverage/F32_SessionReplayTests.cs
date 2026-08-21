using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Infrastructure.Replay;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

/// <summary>
/// Session replay and incident windows, against the classes that ship.
/// </summary>
/// <remarks>
/// This file used to test two doubles declared at the bottom of it.
/// <list type="bullet">
/// <item><c>SessionReplayPlayerState</c> had a <c>LoadSession(file)</c> that returned true and set
/// <c>TotalPackets = 100</c> without opening the file — so "load a recording" passed for a path
/// that did not exist, and every assertion after it was about a counter the stub set itself.</item>
/// <item><c>FailureSnapshotExtractorHelper</c> reimplemented the extractor with a symmetric window,
/// where the real one is deliberately asymmetric: ten seconds before the failure and two after,
/// because what happened <em>before</em> the fault is what explains it.</item>
/// </list>
/// A test double that stands in for the thing under test can only confirm itself. Both are gone and
/// the assertions run against <see cref="SessionReplayPlayer"/> and
/// <see cref="FailureSnapshotExtractor"/>.
/// </remarks>
public class F32_SessionReplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tdf32_" + Guid.NewGuid().ToString("N")[..8]);

    public F32_SessionReplayTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a recording in the recorder's own layout.</summary>
    private string Recording(int rows, double secondsApart = 1.0)
    {
        string path = Path.Combine(_dir, "session.csv");
        var lines = new List<string>
        {
            "Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,"
            + "Predicted_Value,Predicted_Horizon_Sec,Status"
        };

        double t = 1_000_000.0;
        for (int i = 0; i < rows; i++)
        {
            lines.Add($"2026-01-01T00:00:00.000Z,{t.ToString("F3", CultureInfo.InvariantCulture)}," +
                      $"MCU_1,TEMP,{(40.0 + i).ToString("F4", CultureInfo.InvariantCulture)},0.00,FALSE,0.0000,0,OK");
            t += secondsApart;
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LoadingARecordingReadsTheRowsThatAreInIt()
    {
        var player = new SessionReplayPlayer();

        player.LoadSession(Recording(rows: 30));

        player.Frames.Should().HaveCount(30, "the count comes from the file, not from a constant");
        player.TotalDurationSeconds.Should().BeApproximately(29.0, 0.001);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFileThatIsNotThereIsRefusedRatherThanReportedAsLoaded()
    {
        // The stub this replaces returned true for any path at all, so a typo in a filename and a
        // successful load were the same outcome.
        var player = new SessionReplayPlayer();

        Action missing = () => player.LoadSession(Path.Combine(_dir, "no_such_recording.csv"));

        missing.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SeekingIsClampedToTheRecordingThatIsLoaded()
    {
        var player = new SessionReplayPlayer();
        player.LoadSession(Recording(rows: 20));

        player.Seek(5.0);
        player.CurrentPositionSeconds.Should().Be(5.0);

        player.Seek(9999.0);
        player.CurrentPositionSeconds.Should().Be(player.TotalDurationSeconds,
            "a cursor past the end of the session points at nothing");

        player.Seek(-10.0);
        player.CurrentPositionSeconds.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PlaybackSpeedIsWhatWasSet()
    {
        var player = new SessionReplayPlayer();

        player.SetSpeed(2.0);
        player.PlaybackSpeed.Should().Be(2.0);

        player.SetSpeed(0.5);
        player.PlaybackSpeed.Should().Be(0.5);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnIncidentWindowLooksMostlyBackwards()
    {
        // The property the helper this replaces did not have. Ten seconds before the failure and
        // two after: the lead-up is what explains a fault, and the tail shows how the system
        // responded to it.
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<TelemetryPacket>();
        for (int i = -60; i <= 60; i++)
        {
            history.Add(new TelemetryPacket("MCU_1", "TEMP", 40.0 + i, "C", now.AddSeconds(i)));
        }

        IReadOnlyList<TelemetryPacket> snapshot =
            new FailureSnapshotExtractor().Extract10sFailureSnapshot(history, now);

        snapshot.Should().NotBeEmpty();
        snapshot.Should().OnlyContain(p => p.Timestamp >= now.AddSeconds(-10));
        snapshot.Should().OnlyContain(p => p.Timestamp <= now.AddSeconds(2));

        snapshot.Count(p => p.Timestamp < now).Should().Be(10);
        snapshot.Count(p => p.Timestamp > now).Should().Be(2,
            "the trailing margin is deliberately short; the fault is explained by what came before");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFailureWithNoSurroundingDataYieldsAnEmptyWindowRatherThanTheNearestRows()
    {
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<TelemetryPacket>
        {
            new("MCU_1", "TEMP", 40.0, "C", now.AddHours(-3))
        };

        new FailureSnapshotExtractor()
            .Extract10sFailureSnapshot(history, now)
            .Should().BeEmpty("a reading from three hours earlier does not describe this instant");
    }
}
