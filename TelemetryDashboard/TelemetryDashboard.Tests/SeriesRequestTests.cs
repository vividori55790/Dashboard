using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Telling "you asked for nothing" apart from "there is nothing".
/// </summary>
/// <remarks>
/// <c>/api/series</c> answered a request that named no channel with a well-formed frame holding
/// zero points and no complaint. Nothing shipped calls it that way, which is why it survived — but
/// the first thing anybody does with an endpoint is call it by hand, and both <c>?channel=</c> and
/// <c>?channels=</c> look reasonable from outside.
/// <para>
/// Measured on a running host, and the reason this was found at all: a query for
/// <c>SIM:COM3.psfb.output_voltage</c> came back with zero samples while <c>/api/computed</c> was
/// aligning 292 samples from that exact key at that moment. The store was suspected long before
/// the request was. With the parameter spelled plural the same query returns 137 of 137 points.
/// </para>
/// </remarks>
public class SeriesRequestTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AskingForNothingIsRefusedRatherThanAnsweredWithNothing()
    {
        SeriesRequest.TryChannels(null, out string[] channels, out string? refusal).Should().BeFalse();

        channels.Should().BeEmpty();
        refusal.Should().Contain("channels=a,b,c (plural)");
        refusal.Should().Contain("not the same thing as a host holding nothing");
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" , , ")]
    public void AParameterThatNamesNoChannelIsTheSameAsNoParameter(string raw)
    {
        // A trailing comma is what a caller building the list in a loop produces, and it must not
        // become a channel named "".
        SeriesRequest.TryChannels(raw, out string[] channels, out string? refusal).Should().BeFalse();

        channels.Should().BeEmpty();
        refusal.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NamedChannelsComeBackTrimmedAndInOrder()
    {
        SeriesRequest.TryChannels(" a.b , c.d ", out string[] channels, out string? refusal)
            .Should().BeTrue();

        refusal.Should().BeNull();
        channels.Should().Equal(new[] { "a.b", "c.d" });
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AKeyContainingAColonSurvives()
    {
        // Simulated runs key their channels SIM:COM3.channel, which is the shape most likely to be
        // mangled by a parser that got clever about separators.
        SeriesRequest.TryChannels("SIM:COM3.psfb.output_voltage", out string[] channels, out _)
            .Should().BeTrue();

        channels.Should().ContainSingle().Which.Should().Be("SIM:COM3.psfb.output_voltage");
    }
}
