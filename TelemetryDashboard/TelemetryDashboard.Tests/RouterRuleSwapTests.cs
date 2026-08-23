using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Changing what the wire means, on a router that is already carrying frames.
/// </summary>
/// <remarks>
/// The desktop shell can now be handed a rules file describing the device on the bench, and it is
/// handed one while the port is open. That makes the swap a real operation rather than start-up
/// configuration: registering the new rules and then removing the old ones leaves a window in which
/// both apply, and clearing first leaves one in which none do — and a frame arriving in that second
/// window falls through to the positional parser, which names its numbers Field_1 and charts them.
/// <para>
/// Driven on the running window: a file was put in force mid-session and the shell reported
/// "1 rule(s) from edited.json are now in force, replacing the built-in framing", followed by the
/// two findings the audit had against the active profile.
/// </para>
/// </remarks>
public class RouterRuleSwapTests
{
    private static RawPacket Frame(string body) =>
        new("COM-TEST", "$" + body + "*" + XorChecksum.Calculate(body.AsSpan()).ToString("X2"), DateTime.UtcNow);

    private static RoutingRule Rule(string id, string tag, params (string Wire, string Channel)[] aliases)
    {
        var rule = new RoutingRule { Id = id, RuleType = RuleType.Prefix, Tag = tag, Port = "*" };
        foreach ((string wire, string channel) in aliases) rule.NameMap[wire] = new ChannelAlias(channel);
        return rule;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AReplacedRuleSetIsTheOnlyOneInForce()
    {
        // A file describing this bench replaces the built-ins rather than joining them. Two rules
        // claiming one frame is not two configurations, it is one ambiguity.
        var router = new DataRouter();
        router.RegisterRule(Rule("built-in", "TELE"));

        router.ReplaceRules([Rule("file-1", "TELE", ("Vout", "psfb.output_voltage"))]);

        router.Rules.Should().ContainSingle().Which.Id.Should().Be("file-1");
        List<TelemetryPacket> routed = router.Route(Frame("TELE,PSFB-01,Vout,48.2,V")).ToList();
        routed.Should().ContainSingle().Which.Variable.Should().Be("psfb.output_voltage");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReplacingWithNothingLeavesNothingRatherThanTheOldRules()
    {
        var router = new DataRouter();
        router.RegisterRule(Rule("built-in", "TELE"));

        router.ReplaceRules([]);

        router.Rules.Should().BeEmpty();
        router.Route(Frame("TELE,PSFB-01,Vout,48.2,V")).Should().BeEmpty(
            "a router with no rules recognises nothing, which the caller has to be able to see");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFrameArrivingDuringTheSwapSeesOneRuleSetOrTheOther()
    {
        // The reason the swap is one assignment. Routing reads a single reference; the writer
        // builds the next set and publishes it whole, so there is no instant at which a frame can
        // find the dictionary half-emptied.
        var router = new DataRouter();
        router.RegisterRule(Rule("built-in", "TELE"));

        int missed = 0;
        var stop = new CancellationTokenSource();
        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (!router.Route(Frame("TELE,PSFB-01,Vout,48.2,V")).Any()) missed++;
            }
        });

        for (int i = 0; i < 200; i++)
        {
            router.ReplaceRules([Rule($"file-{i}", "TELE", ("Vout", "psfb.output_voltage"))]);
        }

        stop.Cancel();
        reader.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        missed.Should().Be(0, "a frame that matched no rule would be parsed positionally as Field_1");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void RegisteringAndUnregisteringStillBehaveAfterTheSwapToCopyOnWrite()
    {
        var router = new DataRouter();

        router.RegisterRule(Rule("a", "TELE")).Should().BeTrue();
        router.RegisterRule(Rule("b", "FAST")).Should().BeTrue();
        router.Rules.Should().HaveCount(2);

        router.UnregisterRule("a").Should().BeTrue();
        router.UnregisterRule("a").Should().BeFalse("it is already gone");
        router.Rules.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheRulesADraftedFileDescribesCanBePutStraightIntoARouter()
    {
        // The whole chain in one line: listen, draft, read back, apply. If any link renamed
        // something on the way, this is where it shows.
        var survey = new WireSurvey();
        survey.Observe(Frame("TELE,PSFB-01,ambient.temperature,21.5,degC"),
            [new TelemetryPacket("PSFB-01", "ambient.temperature", 21.5, "degC", DateTime.UtcNow)]);

        string draft = RuleDraft.Render(survey, MonitoringProfileLibrary.Generic);
        var router = new DataRouter();
        router.ReplaceRules(RoutingRuleReader.Parse(draft, "draft").Rules);

        router.Route(Frame("TELE,PSFB-01,ambient.temperature,21.5,degC"))
            .Should().ContainSingle().Which.Variable.Should().Be("ambient.temperature");
    }
}
