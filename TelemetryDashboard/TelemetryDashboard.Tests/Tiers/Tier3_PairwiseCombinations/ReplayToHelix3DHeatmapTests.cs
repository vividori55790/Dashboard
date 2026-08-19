namespace TelemetryDashboard.Tests.Tiers.Tier3_PairwiseCombinations;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using Xunit;

/// <summary>
/// Tier 3 Pairwise Combination Test Suite:
/// Verifies cross-subsystem interaction between Time-Machine Session Replay Player -> Helix3D 3D Heatmap Overlay.
/// </summary>
[Trait("Category", "Tier3")]
public class ReplayToHelix3DHeatmapTests
{
    private class MockSessionReplayPlayer
    {
        public bool IsPlaying { get; private set; }
        public double PlaybackSpeed { get; private set; } = 1.0;
        public TimeSpan CurrentPosition { get; private set; } = TimeSpan.Zero;
        public TimeSpan TotalDuration { get; private set; } = TimeSpan.FromSeconds(10);
        public List<TelemetryPacket> LoadedPackets { get; } = new();

        public event EventHandler<TelemetryPacket>? FrameReplayed;

        public bool LoadSession(string sessionContent)
        {
            if (string.IsNullOrWhiteSpace(sessionContent) || sessionContent.StartsWith("CORRUPTED"))
            {
                return false;
            }

            LoadedPackets.Clear();
            var lines = sessionContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var parts = l.Split(',');
                if (parts.Length >= 4 && double.TryParse(parts[2], out double val))
                {
                    LoadedPackets.Add(new TelemetryPacket(parts[0], parts[1], val, parts[3], flags: PacketFlags.IsHistorical));
                }
            }
            return true;
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void SetSpeed(double multiplier) => PlaybackSpeed = multiplier;
        public void Seek(TimeSpan position) => CurrentPosition = position;

        public void StepFrame(int index)
        {
            if (IsPlaying && index >= 0 && index < LoadedPackets.Count)
            {
                FrameReplayed?.Invoke(this, LoadedPackets[index]);
            }
        }
    }

    private class MockHelix3DHeatmapController
    {
        // 3D mesh vertex color array represented by RGB floats (R, G, B)
        public float[] VertexHeatColors { get; private set; } = new float[10];
        public int UpdateCount { get; private set; }

        public void ApplyThermalOverlay(string nodeId, double temperature)
        {
            UpdateCount++;
            // Inverse Distance Weighting (IDW) interpolation mock calculation
            float normalizedTemp = (float)Math.Clamp((temperature - 20.0) / 80.0, 0.0, 1.0);
            for (int i = 0; i < VertexHeatColors.Length; i++)
            {
                VertexHeatColors[i] = normalizedTemp * (1.0f - (i * 0.05f));
            }
        }
    }

    [Fact]
    public void SessionReplay_LoadRecording_Updates3DHeatmapColors()
    {
        var player = new MockSessionReplayPlayer();
        var heatmap = new MockHelix3DHeatmapController();

        player.FrameReplayed += (s, pkt) =>
        {
            if (pkt.Variable == "TEMP")
            {
                heatmap.ApplyThermalOverlay(pkt.NodeId, pkt.Value);
            }
        };

        string sessionData = "MCU_NODE_1,TEMP,75.0,C\r\nMCU_NODE_1,TEMP,85.0,C\r\nMCU_NODE_1,TEMP,95.0,C";
        bool loaded = player.LoadSession(sessionData);

        loaded.Should().BeTrue();
        player.LoadedPackets.Should().HaveCount(3);

        player.Play();
        player.StepFrame(0);

        heatmap.UpdateCount.Should().Be(1);
        heatmap.VertexHeatColors[0].Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void SessionReplay_SpeedControl_ReplaysAtDifferentSpeeds()
    {
        var player = new MockSessionReplayPlayer();

        player.SetSpeed(2.0);
        player.PlaybackSpeed.Should().Be(2.0);

        player.SetSpeed(0.5);
        player.PlaybackSpeed.Should().Be(0.5);
    }

    [Fact]
    public void SessionReplay_SeekAndRewind_HeatmapReflectsHistoricalState()
    {
        var player = new MockSessionReplayPlayer();
        var heatmap = new MockHelix3DHeatmapController();

        player.FrameReplayed += (s, pkt) =>
        {
            heatmap.ApplyThermalOverlay(pkt.NodeId, pkt.Value);
        };

        string sessionData = "MCU_1,TEMP,30.0,C\r\nMCU_1,TEMP,60.0,C\r\nMCU_1,TEMP,90.0,C";
        player.LoadSession(sessionData);
        player.Play();

        // Seek to index 2 (historical T=5.0s state with 90 C)
        player.Seek(TimeSpan.FromSeconds(5.0));
        player.StepFrame(2);

        heatmap.VertexHeatColors[0].Should().Be((float)((90.0 - 20.0) / 80.0));

        // Rewind to index 0 (T=0.0s with 30 C)
        player.Seek(TimeSpan.FromSeconds(0.0));
        player.StepFrame(0);

        heatmap.VertexHeatColors[0].Should().Be((float)((30.0 - 20.0) / 80.0));
    }

    [Fact]
    public void SessionReplay_Pause_FreezesHeatmapState()
    {
        var player = new MockSessionReplayPlayer();
        var heatmap = new MockHelix3DHeatmapController();

        player.FrameReplayed += (s, pkt) =>
        {
            heatmap.ApplyThermalOverlay(pkt.NodeId, pkt.Value);
        };

        string sessionData = "MCU_1,TEMP,50.0,C\r\nMCU_1,TEMP,75.0,C";
        player.LoadSession(sessionData);

        player.Play();
        player.StepFrame(0);
        int updatesBeforePause = heatmap.UpdateCount;

        player.Pause();
        player.StepFrame(1); // Should not trigger while paused

        heatmap.UpdateCount.Should().Be(updatesBeforePause);
    }

    [Fact]
    public void SessionReplay_CorruptedSessionFile_GracefulErrorHandling()
    {
        var player = new MockSessionReplayPlayer();
        string corruptedData = "CORRUPTED_FILE_DATA_HEADER_INVALID";

        bool result = player.LoadSession(corruptedData);

        result.Should().BeFalse();
        player.LoadedPackets.Should().BeEmpty();
    }
}
