using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Ingest;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Playing a recording back through the pipeline that recorded it.
/// </summary>
/// <remarks>
/// <c>SessionReplayPlayer</c> could load a recording from M2 and was constructed by nothing, so a
/// recording could be written and never read by the program that wrote it. Attaching it as a source
/// means routing, the analytics engine, the console and the DVR all work on recorded data, because
/// from their side nothing is different.
/// <para>
/// The round trip is the assertion that matters: what was recorded has to come back out. The frames
/// are re-encoded and re-parsed on the way, so anything that mangles a name or a value shows up
/// here rather than in a post-mortem six months from now.
/// </para>
/// </remarks>
public class ReplaySourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tdreplay_" + Guid.NewGuid().ToString("N")[..8]);

    public ReplaySourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a recording in the recorder's own layout.</summary>
    private string Recording(params (string Node, string Channel, double Value)[] rows)
    {
        string path = Path.Combine(_dir, "rec.csv");
        var lines = new List<string>
        {
            "Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,"
            + "Predicted_Value,Predicted_Horizon_Sec,Status"
        };

        double t = 1_000_000.0;
        foreach ((string node, string channel, double value) in rows)
        {
            lines.Add($"2026-01-01T00:00:00.000Z,{t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{node},{channel},{value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                      "0.00,FALSE,0.0000,0,OK");
            t += 0.01;
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>Drains the source through the production router, exactly as the host does.</summary>
    private static async Task<List<TelemetryPacket>> DrainAsync(ReplayTelemetrySource source)
    {
        var router = new DataRouter();
        foreach (RoutingRule rule in DefaultRoutingRules.Create()) router.RegisterRule(rule);

        var packets = new List<TelemetryPacket>();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await foreach (RawPacket raw in source.ReadAsync(cancel.Token))
        {
            packets.AddRange(router.Route(raw));
        }

        return packets;
    }

    [Fact]
    public async Task WhatWasRecordedComesBackOut()
    {
        string path = Recording(
            ("SIM:COM3", "grid.voltage", 384.0),
            ("SIM:COM3", "dab.bus_voltage", 400.5),
            ("SIM:COM3", "psfb.output_voltage", 48.09),
            ("SIM:COM3", "server.load", 82.4));

        var source = new ReplayTelemetrySource(path, speed: 1000);
        source.Load().Should().BeTrue();

        List<TelemetryPacket> packets = await DrainAsync(source);

        packets.Should().HaveCount(4, "every recorded row has to survive re-encoding and re-parsing");
        packets.Select(p => p.Variable).Should().BeEquivalentTo(
            "grid.voltage", "dab.bus_voltage", "psfb.output_voltage", "server.load");
        packets.Select(p => p.Value).Should().BeEquivalentTo(new[] { 384.0, 400.5, 48.09, 82.4 });
    }

    [Fact]
    public async Task TwoNodesRecordingTheSameChannelStaySeparate()
    {
        // The defect this guards: the row parser used the Channel column alone as the channel name
        // and dropped NodeId, so two devices reporting "temp" collapsed into one series on replay
        // and overwrote each other.
        string path = Recording(
            ("COM3", "temp", 25.0),
            ("COM4", "temp", 71.0));

        var source = new ReplayTelemetrySource(path, speed: 1000);
        source.Load().Should().BeTrue();

        List<TelemetryPacket> packets = await DrainAsync(source);

        packets.Should().HaveCount(2);
        packets.Select(p => p.NodeId).Should().BeEquivalentTo("COM3", "COM4");
        packets.Select(p => p.Value).Should().BeEquivalentTo(new[] { 25.0, 71.0 });
    }

    [Fact]
    public async Task AChannelNameContainingDotsSurvivesTheRoundTrip()
    {
        // The channel key is node.variable and the variable itself contains dots, so the split has
        // to be on the first one. Splitting on the last would turn ambient.temperature into a node
        // called "SIM:COM3.ambient".
        string path = Recording(("SIM:COM3", "ambient.temperature", 25.4));

        var source = new ReplayTelemetrySource(path, speed: 1000);
        source.Load().Should().BeTrue();

        TelemetryPacket packet = (await DrainAsync(source)).Single();

        packet.NodeId.Should().Be("SIM:COM3");
        packet.Variable.Should().Be("ambient.temperature");
    }

    [Fact]
    public void ARecordingIsNotMarkedSimulatedButIsMarkedAsAReplay()
    {
        string path = Recording(("COM3", "temp", 25.0));
        var source = new ReplayTelemetrySource(path, speed: 1000);

        source.Origin.Should().Be("REPLAY",
            "a recording played back is not a live reading, and a console that could not tell them "
            + "apart would show last week's incident as though it were happening now");

        source.IsSimulated.Should().BeFalse(
            "simulated means the data was invented; a recording of real hardware was not");
    }

    [Fact]
    public void AFileWithNothingPlayableSaysSoRatherThanRunningEmpty()
    {
        string path = Path.Combine(_dir, "headeronly.csv");
        File.WriteAllText(path,
            "Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,"
            + "Predicted_Value,Predicted_Horizon_Sec,Status\n");

        new ReplayTelemetrySource(path, speed: 1000).Load().Should().BeFalse();
    }

    [Fact]
    public async Task ATornFinalLineDoesNotDiscardTheRecording()
    {
        // Recordings are frequently cut short by the event they were capturing. Refusing to open
        // the file would throw away the evidence at exactly the moment it is wanted.
        string path = Recording(("COM3", "temp", 25.0), ("COM3", "temp", 26.0));
        File.AppendAllText(path, "2026-01-01T00:00:00.000Z,1000000.02,COM3,te");

        var source = new ReplayTelemetrySource(path, speed: 1000);
        source.Load().Should().BeTrue();

        (await DrainAsync(source)).Should().HaveCount(2, "the two whole rows still play");
    }

    [Fact]
    public void ANonPositiveSpeedIsRefusedRatherThanStallingForever()
    {
        string path = Recording(("COM3", "temp", 25.0));

        Action zero = () => new ReplayTelemetrySource(path, speed: 0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }
}
