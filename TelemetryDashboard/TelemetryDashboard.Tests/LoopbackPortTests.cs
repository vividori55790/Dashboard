using System.IO;
using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Serial;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A port with no device behind it, and what it is for.
/// </summary>
/// <remarks>
/// The headless host has had one since the emergency interlock needed proving. The shell had none,
/// so everything downstream of a port was unreachable on a desk: the reconnect watchdog, the
/// anomaly edges on the hardware path, the wire-rule draft, and the transmit path the control panel
/// writes through. Four features that could only be checked by somebody who already had the machine.
/// <para>
/// Driven on the running shell: the port list offers COM3 | COM5 | COM4 | loopback, connecting to
/// the last of those flips the indicator to 연결 해제, and the event log fills with readings that
/// arrived through the serial reader — carrying the anomaly edges that path had never logged, under
/// node ids marked SIM: because the frames are generated.
/// </para>
/// </remarks>
public class LoopbackPortTests
{
    private static string Frame(string body) =>
        "$" + body + "*" + XorChecksum.Calculate(body.AsSpan()).ToString("X2");

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AFrameGoesInAsBytesAndComesBackOutAsALine()
    {
        // Through the port's own buffer rather than handed straight to the reader, which is the
        // whole reason this is worth having: the framing and the checksum run on the same input a
        // device would have produced.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync("loopback");

        manager.Deliver("loopback", Frame("TELE,RIG,rail,48.2,V")).Should().BeTrue();

        RawPacket packet = await manager.PacketReader.ReadAsync();
        packet.PortName.Should().Be("loopback");
        packet.RawLine.Should().Contain("TELE,RIG,rail,48.2,V");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task WhatArrivesIsWhatTheRoutingRulesThenParse()
    {
        // The point of putting the shell's generated frames through a port instead of straight into
        // the router: the wire-name mapping an installation configures runs on them too.
        var manager = new LoopbackSerialManager();
        await manager.ConnectPortAsync("loopback");
        manager.Deliver("loopback", Frame("TELE,RIG,Vout,48259.9,mV"));

        var router = new DataRouter();
        var rule = new RoutingRule { Id = "file-1", RuleType = RuleType.Prefix, Tag = "TELE", Port = "*" };
        rule.NameMap["Vout"] = new Core.Ingest.ChannelAlias("psfb.output_voltage", "V", 0.001);
        router.ReplaceRules([rule]);

        RawPacket raw = await manager.PacketReader.ReadAsync();
        TelemetryPacket packet = router.Route(raw).Should().ContainSingle().Subject;

        packet.Variable.Should().Be("psfb.output_voltage");
        packet.Value.Should().BeApproximately(48.2599, 1e-6);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task DeliveringToAPortNobodyOpenedIsRefusedRatherThanDropped()
    {
        // The shell's feed stops on false. A silent drop would leave a connected-looking session
        // with no data and nothing to explain it.
        var manager = new LoopbackSerialManager();

        manager.Deliver("loopback", Frame("TELE,RIG,rail,48.2,V")).Should().BeFalse();

        await manager.ConnectPortAsync("loopback");
        manager.Deliver("loopback", Frame("TELE,RIG,rail,48.2,V")).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AWriteToAClosedPortThrowsRatherThanLookingLikeItLanded()
    {
        // An interlock reporting a dispatch to a port that was never open is the failure this
        // whole class exists to make visible.
        var manager = new LoopbackSerialManager();

        Func<Task> write = () => manager.WriteLineAsync("loopback", "$CMD,STOP\r\n");
        await write.Should().ThrowAsync<InvalidOperationException>();

        await manager.ConnectPortAsync("loopback");
        await manager.WriteLineAsync("loopback", "$CMD,STOP\r\n");

        manager.WriteCount.Should().Be(1);
        manager.Written.Should().ContainSingle().Which.Should().Contain("STOP");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheShellAndTheHostOfferThePortUnderTheSameName()
    {
        // Two spellings of this would mean --serial loopback and the desktop's port entry were
        // different features that happen to look alike, and the docs for one would mislead about
        // the other.
        string host = File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Host", "Ingest", "LoopbackTelemetrySource.cs"));
        string shell = File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.UI", "MainWindow.Loopback.cs"));

        System.Text.RegularExpressions.Match hostToken = Regex.Match(host, @"PortToken = ""([a-z]+)""");
        System.Text.RegularExpressions.Match shellToken = Regex.Match(shell, @"LoopbackPort = ""([a-z]+)""");

        hostToken.Success.Should().BeTrue();
        shellToken.Success.Should().BeTrue();
        shellToken.Groups[1].Value.Should().Be(hostToken.Groups[1].Value);
    }

    private static string SolutionRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelemetryDashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
