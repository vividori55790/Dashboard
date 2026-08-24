using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Asking the device whether the rules file is right, instead of asking the file.
/// </summary>
/// <remarks>
/// <c>sniff</c> could draft a mapping and could not confirm one. Its own help suggested
/// <c>--rules</c> for the second job, and following that advice wrote a rules.json nobody asked
/// for — or refused outright, when one was already there — and then exited 0 whether every
/// declared channel was fed or none of them were.
/// <para>
/// The report was already correct; only the side effect and the verdict were missing. That is
/// worth stating because the obvious change here was to write a second reporting path, and a
/// second answer to "is this reading that channel" would have been free to disagree with the
/// first.
/// </para>
/// </remarks>
public class SniffVerificationTests
{
    private static MonitoringProfile Profile() => new()
    {
        Id = "bench",
        DisplayName = "Bench rig",
        Channels =
        [
            new ProfileChannel { Id = "psfb.output_voltage", Label = "Vout", Unit = "V", Nominal = 48 },
            new ProfileChannel { Id = "psfb.output_current", Label = "Iout", Unit = "A", Nominal = 12 },
            new ProfileChannel { Id = "dab.input_voltage",   Label = "Vin",  Unit = "V", Nominal = 400 }
        ]
    };

    /// <summary>Feeds the survey the packets a routed line would have produced.</summary>
    private static WireSurvey SurveyOf(params string[] routedChannelNames)
    {
        var survey = new WireSurvey();

        foreach (string name in routedChannelNames)
        {
            survey.Observe(
                new RawPacket("COM-TEST", $"$TELE,PSFB-01,{name},48.2,V*7F", DateTime.UtcNow),
                [new TelemetryPacket("PSFB-01", name, 48.2, "V", DateTime.UtcNow)]);
        }

        return survey;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelNobodyIsFeedingIsNamed()
    {
        (string[] fed, string[] silent) =
            SniffVerification.Coverage(SurveyOf("psfb.output_voltage"), Profile());

        fed.Should().ContainSingle().Which.Should().Be("psfb.output_voltage");
        silent.Should().BeEquivalentTo(["psfb.output_current", "dab.input_voltage"]);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheExitCodeIsWhatAScriptCanActOn()
    {
        MonitoringProfile profile = Profile();

        SniffVerification.ExitCode(SurveyOf("psfb.output_voltage"), profile)
            .Should().Be(1, "a run where two of three channels are silent has not commissioned the rig");

        SniffVerification
            .ExitCode(SurveyOf("psfb.output_voltage", "psfb.output_current", "dab.input_voltage"), profile)
            .Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HearingNothingIsNotSuccess()
    {
        // The case that matters most, and the one a chart cannot distinguish from a calm rig.
        SniffVerification.ExitCode(new WireSurvey(), Profile()).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void WithoutAProfileThereIsNoClaimToCheck()
    {
        // Reached through the API rather than the command line, and deliberately so: the host
        // falls back to generic-machine when --profile is absent, so no invocation produces a null
        // profile. This pins the guard's behaviour -- a verification with nothing to verify is not
        // a pass -- without pretending an operator can get here.
        var survey = SurveyOf("psfb.output_voltage");

        SniffVerification.ExitCode(survey, profile: null).Should().Be(1);
        string.Join(" ", SniffVerification.Render(survey, null, ruleCount: 1))
            .Should().Contain("no claim to check");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheVerdictSaysWhichChannelsAreSilentAndWhyThatMatters()
    {
        string rendered = string.Join("\n", SniffVerification.Render(
            SurveyOf("psfb.output_voltage"), Profile(), ruleCount: 1));

        rendered.Should().Contain("1 of 3 declared channel(s)");
        rendered.Should().Contain("psfb.output_current");
        rendered.Should().Contain("dab.input_voltage");

        // The consequence, not just the list: a band naming a silent channel never fires, and a
        // rule that never fires looks exactly like a machine that is behaving.
        rendered.Should().Contain("matches nothing");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void RunningWithNoRulesAtAllSaysSoRatherThanImplyingOne()
    {
        string rendered = string.Join(" ", SniffVerification.Render(
            SurveyOf("psfb.output_voltage"), Profile(), ruleCount: 0));

        rendered.Should().Contain("built-in framing");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void VerifyIsItsOwnFlagAndDoesNotReachTheHostsParser()
    {
        // A real file, because the host's parser refuses a --rules path that does not exist and
        // does it at parse time. That is the right place for it -- an operator who mistyped the
        // name learns before fifteen seconds of listening rather than after -- so the test works
        // with the behaviour instead of around it.
        string rules = Path.Combine(Path.GetTempPath(), "sniff-verify-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(rules, "{\"name\":\"probe\",\"rules\":[]}");

        try
        {
            SniffCommandLine command = SniffCommandLine.Parse(
                ["sniff", "--serial", "COM3", "--rules", rules, "--verify"]);

            command.Error.Should().BeNull("--verify is this command's own flag, not the serving host's");
            command.Verify.Should().BeTrue();
            command.Source.SerialPort.Should().Be("COM3");
            command.Source.RulesPath.Should().Be(rules);
        }
        finally
        {
            File.Delete(rules);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DraftingStaysTheDefault()
    {
        SniffCommandLine.Parse(["sniff", "--serial", "COM3"]).Verify.Should().BeFalse();
    }
}
