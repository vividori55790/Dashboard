using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The rules file written from what a device was heard saying.
/// </summary>
/// <remarks>
/// The design in one sentence: fill in what the numbers decide, refuse to fill in what they do not,
/// and put the evidence beside every blank. A drafted file that guessed would look configured and
/// be wrong, which is the exact state the whole feature exists to end.
/// <para>
/// Driven end to end on a live host. <c>sniff</c> heard the bench MCU and wrote the file; the one
/// edit it asks for is deleting a <c>//</c>; the host then started with <c>--rules</c> on that file
/// and reported <c>psfb.output_voltage[V] in 45..51</c> as Watching, evaluated 96 times, last value
/// 48.0954 V — converted from 48095.4 mV by the gain the draft derived.
/// </para>
/// </remarks>
public class RuleDraftTests
{
    private static readonly MonitoringProfile Profile = MonitoringProfileLibrary.PowerConverterUps;

    private static RawPacket Line(string tag) => new("COM-TEST", "$" + tag + ",x*7F", DateTime.UtcNow);

    private static WireSurvey Heard(params (string Name, double Value, string Unit)[] readings)
    {
        var survey = new WireSurvey();
        foreach ((string name, double value, string unit) in readings)
        {
            survey.Observe(Line("TELE"), [new TelemetryPacket("PSFB-01", name, value, unit, DateTime.UtcNow)]);
        }

        return survey;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WhatItWritesIsAFileTheHostCanRead()
    {
        // The property everything else rests on. A draft the reader rejects, or one that warns on
        // every start, is a worse starting point than a blank file -- it teaches the operator that
        // the feature is broken before they have edited a line.
        string draft = RuleDraft.Render(Heard(("Vout", 48200, "mV"), ("Iout", 3.2, "A")), Profile);

        RoutingRuleReader.Result read = RoutingRuleReader.Parse(draft, "draft");

        read.Warnings.Should().BeEmpty();
        read.Rules.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AReadingThatOnlyOneBandFitsIsWrittenOutReadyToUncomment()
    {
        // 48200 mV against a band of 38..54 V leaves one answer on this rig, and the unit
        // conversion that gets it there is arithmetic rather than a guess. The smallest edit that
        // could accept it is deleting two characters.
        string draft = RuleDraft.Render(Heard(("Vout", 48200, "mV")), Profile);

        draft.Should().Contain(
            "// \"Vout\": { \"channel\": \"psfb.output_voltage\", \"unit\": \"V\", \"gain\": 0.001 },");
        draft.Should().Contain("delete the // below to accept it");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AReadingSeveralBandsFitEquallyIsLeftBlank()
    {
        // 3.2 A is inside four of this profile's current bands. Writing one of them out would be a
        // coin toss the operator cannot tell from a finding.
        string draft = RuleDraft.Render(Heard(("Iout", 3.2, "A")), Profile);

        draft.Should().Contain("// \"Iout\": { \"channel\": \"\" },");
        draft.Should().Contain("none of these fits closely enough");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABandWideEnoughToContainAnythingIsNotEvidence()
    {
        // grid.voltage spans 0..440 V, so a 12 V auxiliary rail is the only thing it "fits". A tool
        // that answered "your aux rail is the mains" because of that is one nobody trusts again.
        string draft = RuleDraft.Render(Heard(("Vaux", 12.0, "V")), Profile);

        draft.Should().Contain("// \"Vaux\": { \"channel\": \"\" },");
        draft.Should().Contain("one declared channel fits: grid.voltage");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ANameTheProfileAlreadyDeclaresIsMappedForReal()
    {
        // Firmware this product generated sends the declared names, and so does a bench somebody
        // has already been through once. Neither should have to be re-approved by hand.
        string draft = RuleDraft.Render(Heard(("psfb.output_voltage", 48.2, "V")), Profile);

        draft.Should().Contain("\"psfb.output_voltage\": { \"channel\": \"psfb.output_voltage\" },");
        RoutingRuleReader.Parse(draft).Rules[0].NameMap.Should().ContainKey("psfb.output_voltage");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AReadingNoDeclaredUnitMatchesSaysSoRatherThanReachingForOne()
    {
        string draft = RuleDraft.Render(Heard(("Tmcu", 41.5, "degC")), Profile);

        draft.Should().Contain("// \"Tmcu\": { \"channel\": \"\" },");
        draft.Should().Contain("nothing this profile declares has a unit and a range these readings would fit");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ATagNothingClaimedGetsARuleThatClaimsIt()
    {
        // The one-line fix for a device that frames as $DATA. Without this the draft would describe
        // a stream that produced nothing and say nothing about why.
        var survey = new WireSurvey();
        survey.Observe(Line("DATA"), []);
        survey.Observe(Line("DATA"), []);

        string draft = RuleDraft.Render(survey, Profile);

        RoutingRuleReader.Result read = RoutingRuleReader.Parse(draft, "draft");
        read.Warnings.Should().BeEmpty();
        read.Rules.Should().ContainSingle().Which.Tag.Should().Be("DATA");
        draft.Should().Contain("Nothing readable arrived under $DATA");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void WithoutAProfileItStillWritesDownEverythingThatArrived()
    {
        // Naming a profile is the useful case, not a requirement. An operator who has not chosen
        // one yet still wants the list of names their device sends.
        string draft = RuleDraft.Render(Heard(("Vout", 48200, "mV")), profile: null);

        draft.Should().Contain("// Vout: mV");
        draft.Should().Contain("no profile was named");
        RoutingRuleReader.Parse(draft, "draft").Warnings.Should().BeEmpty();
    }
}
