using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the simulator that produces whatever a profile describes.
/// </summary>
/// <remarks>
/// The engine this replaces was one customer's rig written into code, so selecting a different
/// profile changed the labels and left the data underneath unchanged. These assert the property
/// that fixes: what comes out is the profile's channels, in the profile's units, inside the
/// profile's ranges — and two profiles produce genuinely different streams.
/// </remarks>
public class ProfileSimulatorTests
{
    private static MonitoringProfile Profile(params ProfileChannel[] channels) => new()
    {
        Id = "test-rig",
        DisplayName = "Test rig",
        Nodes = [new ProfileNode { Id = "RIG_1", Label = "Rig" }],
        Channels = channels
    };

    private static ProfileChannel Channel(
        string id, double min, double max, double nominal, string unit = "", int decimals = 2) => new()
    {
        Id = id,
        Label = id,
        Unit = unit,
        Minimum = min,
        Maximum = max,
        Nominal = nominal,
        Decimals = decimals
    };

    /// <summary>Runs the engine briefly and returns the frames it produced, parsed.</summary>
    private static async Task<List<TelemetryPacket>> RunAsync(
        ProfileSimulatorEngine engine, int wantFrames = 40)
    {
        var router = new DataRouter();
        router.RegisterRule(new RoutingRule
        {
            Id = "test", RuleType = RuleType.Prefix, Tag = "TELE", Port = "*", TargetNodeId = string.Empty
        });

        var packets = new List<TelemetryPacket>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        engine.StartSimulation();
        try
        {
            await foreach (RawPacket raw in engine.StreamSimulatedPackets(cancellation.Token))
            {
                packets.AddRange(router.Route(raw));
                if (packets.Count >= wantFrames) break;
            }
        }
        catch (OperationCanceledException)
        {
            // The assertions report the shortfall better than an exception would.
        }
        finally
        {
            await engine.DisposeAsync();
        }

        return packets;
    }

    [Fact]
    public async Task TheChannelsProducedAreTheOnesTheProfileDeclares()
    {
        var profile = Profile(
            Channel("kiln.zone3.temperature", 200, 900, 640, "C", 1),
            Channel("kiln.burner.pressure", 0, 5, 2.4, "bar", 2));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().NotBeEmpty();
        packets.Select(p => p.Variable).Distinct().Should()
            .BeEquivalentTo("kiln.zone3.temperature", "kiln.burner.pressure");
        packets.Should().OnlyContain(p => p.NodeId == "RIG_1");
    }

    [Fact]
    public async Task EveryValueStaysInsideTheRangeTheProfileDeclared()
    {
        var profile = Profile(Channel("bus.voltage", 350, 450, 400, "V", 1));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().NotBeEmpty();
        packets.Should().OnlyContain(p => p.Value >= 350 && p.Value <= 450);

        // And it must actually move, or the test above would pass on a constant.
        packets.Select(p => p.Value).Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task TheUnitTravelsWithTheValue()
    {
        var profile = Profile(Channel("vibration", 0, 1, 0.2, "g", 3));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().OnlyContain(p => p.Unit == "g");
    }

    [Fact]
    public async Task MovingASetpointMovesTheSignal()
    {
        var profile = Profile(Channel("load", 0, 100, 20, "%", 0));
        var engine = new ProfileSimulatorEngine(profile, sampleRateHz: 50);

        engine.SetSetpoint("load", 80).Should().Be(80);
        List<TelemetryPacket> packets = await RunAsync(engine);

        // Around 80 rather than around 20, allowing for the drift the engine adds.
        packets.Average(p => p.Value).Should().BeInRange(65, 95);
    }

    [Fact]
    public void ASetpointOutsideTheDeclaredRangeIsClampedAndSaysSo()
    {
        var engine = new ProfileSimulatorEngine(Profile(Channel("bus.voltage", 350, 450, 400)));

        engine.SetSetpoint("bus.voltage", 10_000).Should().Be(450);
        engine.SetSetpoint("bus.voltage", -5).Should().Be(350);

        // A channel the profile never declared returns NaN rather than silently creating one.
        double unknown = engine.SetSetpoint("does.not.exist", 1);
        double.IsNaN(unknown).Should().BeTrue();
    }

    [Fact]
    public void AScenarioReportsTheChannelIdsItNamedThatDoNotExist()
    {
        var profile = new MonitoringProfile
        {
            Id = "rig", DisplayName = "Rig",
            Channels = [Channel("load", 0, 100, 20)],
            Scenarios =
            [
                new ProfileScenario
                {
                    Id = "surge",
                    Label = "Surge",
                    Setpoints = new Dictionary<string, double> { ["load"] = 95, ["ghost"] = 1 }
                }
            ]
        };

        var engine = new ProfileSimulatorEngine(profile);

        // The one that exists lands; the one that does not is named rather than ignored, because a
        // scenario that silently sets nothing looks exactly like one that worked.
        engine.ApplyScenario("surge").Should().BeEquivalentTo("ghost");
        engine.GetSetpoint("load").Should().Be(95);
    }

    [Fact]
    public void AnUnknownScenarioIsReportedRatherThanTreatedAsANoOp()
    {
        var engine = new ProfileSimulatorEngine(Profile(Channel("load", 0, 100, 20)));

        engine.ApplyScenario("nope").Should().BeEquivalentTo("nope");
    }

    [Fact]
    public async Task FramesAreRealFramesThatTheProductionParserAccepts()
    {
        // The frames go through DataRouter and its checksum check in RunAsync, so anything the
        // parser rejects simply never appears. Emitting real frames keeps the parsing path
        // exercised by the only source most installations ever run.
        var profile = Profile(Channel("temp", 0, 100, 25, "C", 2));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().NotBeEmpty("a bad checksum or a malformed frame would leave this empty");
    }

    [Fact]
    public async Task AChannelIdContainingAFrameDelimiterCannotBreakTheFrame()
    {
        // A profile is user-supplied data. A comma or a star in an id would otherwise split the
        // frame or corrupt its checksum, and the symptom would be a channel that silently vanished.
        var profile = Profile(Channel("odd,name*here", 0, 10, 5, "u", 1));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().NotBeEmpty();
        packets.Should().OnlyContain(p => !p.Variable.Contains(',') && !p.Variable.Contains('*'));
    }

    [Fact]
    public async Task AFlatChannelIsGeneratedFlatRatherThanGivenInventedMovement()
    {
        var profile = Profile(Channel("fixed", 42, 42, 42, "u", 0));

        List<TelemetryPacket> packets = await RunAsync(new ProfileSimulatorEngine(profile, sampleRateHz: 50));

        packets.Should().NotBeEmpty();
        packets.Should().OnlyContain(p => p.Value == 42,
            "a channel with no declared range has no basis for movement, and adding some would be invention");
    }

    [Fact]
    public async Task TwoProfilesProduceGenuinelyDifferentStreams()
    {
        // The property the whole change exists for. Before it, selecting a profile changed the
        // captions and left one customer's channels underneath.
        List<TelemetryPacket> generic = await RunAsync(
            new ProfileSimulatorEngine(MonitoringProfileLibrary.Generic, sampleRateHz: 50));
        List<TelemetryPacket> power = await RunAsync(
            new ProfileSimulatorEngine(MonitoringProfileLibrary.PowerConverterUps, sampleRateHz: 50));

        generic.Should().NotBeEmpty();
        power.Should().NotBeEmpty();

        generic.Select(p => p.Variable).Distinct()
            .Should().NotIntersectWith(power.Select(p => p.Variable).Distinct());
    }

    [Fact]
    public void ResetReturnsEveryChannelToNominal()
    {
        var engine = new ProfileSimulatorEngine(Profile(
            Channel("a", 0, 100, 20), Channel("b", 0, 10, 7)));

        engine.SetSetpoint("a", 90);
        engine.SetSetpoint("b", 1);
        engine.Reset();

        engine.GetSetpoint("a").Should().Be(20);
        engine.GetSetpoint("b").Should().Be(7);
    }

    [Fact]
    public void AProfileWithNoNodesPublishesUnderItsOwnName()
    {
        var profile = new MonitoringProfile
        {
            Id = "kiln-line-2", DisplayName = "Kiln line 2",
            Channels = [Channel("temp", 0, 100, 25)]
        };

        new ProfileSimulatorEngine(profile).NodeId.Should().Be("kiln-line-2");
    }
}
