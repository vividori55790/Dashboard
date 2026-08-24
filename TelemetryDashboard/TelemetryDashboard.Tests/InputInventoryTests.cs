using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What each port is delivering, kept per port because that is what an operator unplugs.
/// </summary>
/// <remarks>
/// ToDo item 4 asks for the inputs of each system to be visible per system. Nothing here answered
/// that: <c>WireSurvey</c> answers it once for a fixed window and keys on the frame tag, and
/// <c>ChannelSilenceWatch</c> knows a channel's cadence but not its value, its unit or which cable
/// it arrived on. The tag key is the one that matters — two devices on two ports speaking the same
/// tag are one row under it, and a view built on that would show an operator half their rig with
/// no sign that it had.
/// </remarks>
public class InputInventoryTests
{
    private static RawPacket On(string port) => new(port, "$TELE,x*7F", DateTime.UtcNow);

    private static TelemetryPacket Reading(
        string node, string channel, double value, string unit = "V", DateTime? at = null) =>
        new(node, channel, value, unit, at ?? DateTime.UtcNow);

    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoPortsSendingTheSameChannelStayTwoInputs()
    {
        // The defect a tag-keyed survey has, stated as a test: one converter per port, both
        // reporting output_voltage, and an inventory that merged them would show one.
        var inventory = new InputInventory();

        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1));
        inventory.Observe(On("COM4"), Reading("PSFB-02", "psfb.output_voltage", 47.6));

        inventory.Count.Should().Be(2);
        inventory.Ports().Should().BeEquivalentTo(["COM3", "COM4"]);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnInputCarriesItsLastReadingAndItsUnit()
    {
        var inventory = new InputInventory();

        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1));
        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 49.2));

        InputChannel row = inventory.Channels().Should().ContainSingle().Subject;
        row.LastValue.Should().Be(49.2);
        row.Samples.Should().Be(2);
        row.Unit.Should().Be("V");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AUnitThatStopsBeingSentDoesNotEraseTheOneAlreadyKnown()
    {
        // A device that omits the unit on some frames should not make the column flicker between
        // "V" and blank, which reads as a fault in the reading rather than in the frame.
        var inventory = new InputInventory();

        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1, unit: "V"));
        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.3, unit: ""));

        inventory.Channels().Single().Unit.Should().Be("V");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OneSampleGivesNoCadenceRatherThanACadenceOfZero()
    {
        // The rule this project keeps restating: a number nobody could have measured is not
        // reported as a number. One sighting establishes that the channel exists and nothing about
        // how often it speaks, and "0 Hz" beside a live channel is a claim.
        var inventory = new InputInventory();
        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1));

        inventory.Channels().Single().MeanInterval.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheCadenceIsTheGapItHasActuallyBeenShowing()
    {
        var inventory = new InputInventory();
        DateTime start = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 5; i++)
        {
            inventory.Observe(On("COM3"),
                Reading("PSFB-01", "psfb.output_voltage", 48 + i, at: start.AddMilliseconds(i * 250)));
        }

        InputChannel row = inventory.Channels().Single();
        row.Samples.Should().Be(5);
        row.MeanInterval.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void SilenceIsMeasuredFromTheLastThingItSaid()
    {
        var inventory = new InputInventory();
        DateTime at = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1, at: at));

        inventory.Channels().Single()
            .Silence(new DateTimeOffset(at.AddSeconds(30), TimeSpan.Zero))
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AFrameWithNoPortNameIsStillAnInputRatherThanADiscard()
    {
        // A network source has no port. Dropping those rows would make the view claim a rig has
        // fewer inputs than it has, which is the failure this whole panel exists to prevent.
        var inventory = new InputInventory();
        inventory.Observe(new RawPacket("", "$TELE,x*7F", DateTime.UtcNow),
            Reading("SIM:1", "grid.voltage", 400));

        inventory.Channels().Should().ContainSingle().Which.Port.Should().Be("(unnamed)");
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public void ADeviceInventingChannelNamesIsCappedAndSaysSo()
    {
        // The reason this is bounded. Without a ceiling a firmware bug that appends a counter to
        // every channel name grows the view until the process dies; with a silent one, the operator
        // sees a list that is quietly missing the rows that matter.
        var inventory = new InputInventory(capacity: 64);

        for (int i = 0; i < 500; i++)
        {
            inventory.Observe(On("COM3"), Reading("PSFB-01", $"runaway_{i}", i));
        }

        inventory.Count.Should().BeLessThanOrEqualTo(64);
        inventory.Evictions.Should().BeGreaterThan(0, "a capped view has to be able to say it capped");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ReconnectingStartsTheInventoryOverRatherThanShowingTheLastSession()
    {
        var inventory = new InputInventory();
        inventory.Observe(On("COM3"), Reading("PSFB-01", "psfb.output_voltage", 48.1));

        inventory.Clear();

        inventory.Count.Should().Be(0);
        inventory.Channels().Should().BeEmpty();
    }
}
