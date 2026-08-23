using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Writing down what a device actually says, before anyone has to configure it.
/// </summary>
/// <remarks>
/// Configuring a real MCU began from the answer: <c>--rules</c> renames the device's channels into
/// the profile's terms, and writing that file required already knowing the names, the units and the
/// frame tag — none of which is recorded anywhere except inside the device.
/// <para>
/// Driven on a live host against an SSE source emitting a bench MCU's own spelling. Six seconds of
/// listening reported four channels — Vout in mV at 47600..48800, Iout in A, Tmcu in degC, Vaux in
/// V — and named the ten channels the profile declares that nothing had arrived for.
/// </para>
/// </remarks>
public class WireSurveyTests
{
    private static RawPacket Line(string body) => new("COM-TEST", "$" + body + "*7F", DateTime.UtcNow);

    private static TelemetryPacket Reading(string channel, double value, string unit = "V") =>
        new("PSFB-01", channel, value, unit, DateTime.UtcNow);

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelIsRecordedWithTheUnitAndRangeTheDeviceSent()
    {
        // The unit and the range are the whole value of the survey: a name says nothing about which
        // declared channel it is, and 48200 mV says a great deal.
        var survey = new WireSurvey();

        survey.Observe(Line("TELE,PSFB-01,Vout,48200,mV"), [Reading("Vout", 48200, "mV")]);
        survey.Observe(Line("TELE,PSFB-01,Vout,47600,mV"), [Reading("Vout", 47600, "mV")]);

        WireChannel channel = survey.Channels.Should().ContainSingle().Subject;
        channel.Name.Should().Be("Vout");
        channel.Unit.Should().Be("mV");
        channel.Samples.Should().Be(2);
        channel.Minimum.Should().Be(47600);
        channel.Maximum.Should().Be(48200);
        channel.Range.Should().Be("47600..48200");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ALineNoRuleClaimedIsCountedUnderTheTagItCarried()
    {
        // The case this exists for. A device framing as $DATA against rules expecting $TELE produces
        // no channels at all, and "nothing arrived" and "everything arrived and nothing claimed it"
        // are indistinguishable from a chart -- one is a cable, the other is a one-line fix.
        var survey = new WireSurvey();

        survey.Observe(Line("DATA,PSFB-01,Vout,48200,mV"), []);
        survey.Observe(Line("DATA,PSFB-01,Iout,3.2,A"), []);
        survey.Observe(Line("TELE,PSFB-01,Vaux,12.0,V"), [Reading("Vaux", 12.0)]);

        survey.Lines.Should().Be(3);
        survey.UnreadableLines.Should().Be(2);
        survey.UnclaimedTags.Should().ContainKey("DATA").WhoseValue.Should().Be(2);
        survey.Tags.Should().Equal(["TELE"]);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheSameNameUnderTwoTagsIsTwoChannels()
    {
        // One device can send several frame formats, and a rules file has one entry per tag. Merging
        // them here would draft a file that maps a name under a tag it never arrives on.
        var survey = new WireSurvey();

        survey.Observe(Line("TELE,PSFB-01,V,48.0,V"), [Reading("V", 48.0)]);
        survey.Observe(Line("FAST,PSFB-01,V,48.1,V"), [Reading("V", 48.1)]);

        survey.Channels.Should().HaveCount(2);
        survey.Channels.Select(c => c.Tag).Should().BeEquivalentTo(["TELE", "FAST"]);
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("$TELE,node,ch,1,V*7F", "TELE")]
    [InlineData("$DATA*00", "DATA")]
    [InlineData("$TELE", "TELE")]
    [InlineData("{\"nodeId\":\"a\"}", "")]
    [InlineData("", "")]
    [InlineData("$", "")]
    [InlineData("$,x", "")]
    public void TheFrameTagIsReadFromWhateverArrived(string line, string expected)
    {
        WireSurvey.TagOf(line).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AChannelThatNeverMovesStillReportsAValue()
    {
        // A rail sitting exactly on its setpoint is the normal case on a bench, and a range printed
        // as "48..48" reads as a fault in the tool rather than a steady reading.
        var survey = new WireSurvey();

        survey.Observe(Line("TELE,PSFB-01,Vout,48,V"), [Reading("Vout", 48.0)]);

        survey.Channels[0].Range.Should().Be("48");
    }
}
