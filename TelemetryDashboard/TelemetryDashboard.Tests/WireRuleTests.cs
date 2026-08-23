using System.IO;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Saying what the device on this bench actually sends.
/// </summary>
/// <remarks>
/// The only rules any front end registered were the built-in defaults, which recognise the framing
/// this product's own generated firmware emits -- the framing a real installation does not have. A
/// bench STM32 calls the rail <c>Vout</c> and reports millivolts; the profile declares
/// <c>psfb.output_voltage</c> in volts and states a band in volts. The readings arrived, charted
/// themselves under the device's name, and every band, computed channel and twin placement matched
/// nothing at all.
/// <para>
/// Driven on a live host against an SSE source emitting a real MCU's own spelling. Without a rule
/// file the channels arrived as PSFB-01.Vout and PSFB-01.Iout and all seven declared bands read
/// "Evaluated: 0, Never". With one: PSFB-01.psfb.output_voltage at 47.79 V from 48259.9 mV, the
/// band evaluated 353 times, the profile's computed psfb.p_out alive because its inputs finally
/// existed, and the unmapped PSFB-01.Vaux still arriving under its own name.
/// </para>
/// </remarks>
public class WireRuleTests
{
    private static RawPacket Frame(string body) =>
        new("COM-TEST", "$" + body + "*" + Checksum(body), DateTime.UtcNow);

    private static string Checksum(string body) => XorChecksum.Calculate(body.AsSpan()).ToString("X2");

    private static RoutingRule Rule(params (string Wire, ChannelAlias Alias)[] aliases)
    {
        var rule = new RoutingRule { RuleType = RuleType.Prefix, Tag = "TELE", Port = "*" };
        foreach ((string wire, ChannelAlias alias) in aliases) rule.NameMap[wire] = alias;
        return rule;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADeviceNameBecomesTheChannelTheProfileDeclares()
    {
        RoutingRule rule = Rule(("Vout", new ChannelAlias("psfb.output_voltage", "V", 0.001)));

        PrefixParser.TryParse(Frame("TELE,PSFB-01,Vout,48259.9,mV"), rule, out List<TelemetryPacket> packets)
            .Should().BeTrue();

        packets.Should().ContainSingle();
        packets[0].Variable.Should().Be("psfb.output_voltage");
        packets[0].Value.Should().BeApproximately(48.2599, 1e-6, "millivolts were asked to become volts");
        packets[0].Unit.Should().Be("V", "a band in V refuses to judge a channel still calling itself mV");
        packets[0].NodeId.Should().Be("PSFB-01");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANameNoRuleMapsStillArrives()
    {
        // Dropping it would turn a missing mapping into missing data, and the operator would be
        // debugging the device instead of the file.
        RoutingRule rule = Rule(("Vout", new ChannelAlias("psfb.output_voltage", "V")));

        PrefixParser.TryParse(Frame("TELE,PSFB-01,Vaux,12.1,V"), rule, out List<TelemetryPacket> packets)
            .Should().BeTrue();

        packets.Should().ContainSingle();
        packets[0].Variable.Should().Be("Vaux");
        packets[0].Value.Should().Be(12.1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnAliasWithNoUnitKeepsWhateverTheDeviceSent()
    {
        RoutingRule rule = Rule(("Iout", new ChannelAlias("psfb.output_current")));

        PrefixParser.TryParse(Frame("TELE,PSFB-01,Iout,3.2,A"), rule, out List<TelemetryPacket> packets);

        packets[0].Unit.Should().Be("A");
        packets[0].Value.Should().Be(3.2, "a gain of one leaves the reading alone");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheShippedExampleIsAFileAnOperatorCanStartFrom()
    {
        // It is what somebody copies. A sample that does not load teaches them the feature is
        // broken before they have written a line of their own.
        string path = Path.Combine(
            AppContext.BaseDirectory, "samples", "rules.example.json");
        File.Exists(path).Should().BeTrue("the sample ships beside the executable");

        RoutingRuleReader.Result read = RoutingRuleReader.Load(path);

        read.Warnings.Should().BeEmpty();
        read.Rules.Should().ContainSingle();
        read.Rules[0].NameMap.Should().ContainKey("Vbus_mV");
        read.Rules[0].NameMap["Vbus_mV"].Gain.Should().Be(0.001);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFileNamingNoRulesIsRefusedRatherThanAcceptedAsEmpty()
    {
        // It changes nothing while looking like configuration, and whoever wrote it believes their
        // device is mapped.
        Action parse = () => RoutingRuleReader.Parse("{ \"rules\": [] }");

        parse.Should().Throw<InvalidDataException>().WithMessage("*declares no rules*");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TwoRulesClaimingTheSameFramesAreRefusedRatherThanRaced()
    {
        // The router holds its rules in a dictionary and iterates it in whatever order it likes, so
        // which of the two applied could differ between two runs of the same build.
        RoutingRuleReader.Result read = RoutingRuleReader.Parse(
            "{ \"rules\": [" +
            "{ \"type\": \"prefix\", \"tag\": \"TELE\", \"channels\": { \"A\": { \"channel\": \"x\" } } }," +
            "{ \"type\": \"prefix\", \"tag\": \"TELE\", \"channels\": { \"B\": { \"channel\": \"y\" } } } ] }");

        read.Rules.Should().ContainSingle();
        read.Warnings.Should().ContainSingle().Which.Should().Contain("already declared");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AClauseThatCannotBeReadIsReportedAndTheRestSurvive()
    {
        RoutingRuleReader.Result read = RoutingRuleReader.Parse(
            "{ \"rules\": [" +
            "{ \"type\": \"smoke-signals\", \"tag\": \"X\" }," +
            "{ \"type\": \"prefix\", \"channels\": { \"A\": { \"channel\": \"x\" } } }," +
            "{ \"type\": \"prefix\", \"tag\": \"TELE\", \"channels\": " +
            "{ \"A\": { \"channel\": \"x\" }, \"B\": { } } } ] }");

        read.Rules.Should().ContainSingle("only the third names both a type and a tag");
        read.Warnings.Should().HaveCount(3);
        read.Warnings.Should().Contain(w => w.Contains("smoke-signals"));
        read.Warnings.Should().Contain(w => w.Contains("needs a tag"));
        read.Warnings.Should().Contain(w => w.Contains("'B' names no channel"));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMappingTheProfileDoesNotDeclareIsCalledOutAtStartUp()
    {
        MonitoringProfile profile = MonitoringProfileLibrary.PowerConverterUps;
        RoutingRule rule = Rule(("Tmcu", new ChannelAlias("mcu.temperature", "degC")));

        IReadOnlyList<string> findings = RoutingRuleAudit.Check([rule], profile);

        findings.Should().ContainSingle().Which.Should().Contain("mcu.temperature")
            .And.Contain("does not declare");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMappingInTheWrongUnitIsCalledOutBecauseItsBandWillNeverFire()
    {
        MonitoringProfile profile = MonitoringProfileLibrary.PowerConverterUps;
        RoutingRule rule = Rule(("Vout", new ChannelAlias("psfb.output_voltage", "mV")));

        IReadOnlyList<string> findings = RoutingRuleAudit.Check([rule], profile);

        findings.Should().ContainSingle().Which.Should().Contain("refuse to judge");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void DeclaredChannelsNothingMapsOntoAreNamed()
    {
        // The question an operator asks first: which of the things this rig should report is
        // nothing arriving for?
        MonitoringProfile profile = MonitoringProfileLibrary.PowerConverterUps;
        RoutingRule rule = Rule(("Vout", new ChannelAlias("psfb.output_voltage", "V")));

        IReadOnlyList<string> silent = RoutingRuleAudit.Unmapped([rule], profile);

        silent.Should().NotContain("psfb.output_voltage");
        silent.Should().Contain("grid.voltage");
    }
}
