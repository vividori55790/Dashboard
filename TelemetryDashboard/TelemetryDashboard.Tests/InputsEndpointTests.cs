using System.Text.Json;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/inputs</c>, which answers the question asked before any chart is useful.
/// </summary>
/// <remarks>
/// Every other endpoint is organised by channel and so can only be queried by somebody who already
/// knows the channel names. A rig being commissioned is exactly the case where nobody does.
/// </remarks>
public class InputsEndpointTests
{
    private static JsonElement Query(InputInventory? inventory, DateTimeOffset? now = null) =>
        JsonSerializer.SerializeToElement(
            InputsEndpoint.Query(inventory, now ?? DateTimeOffset.UtcNow));

    private static InputInventory Inventory(params (string Port, string Channel)[] inputs)
    {
        var inventory = new InputInventory();
        foreach ((string port, string channel) in inputs)
        {
            inventory.Observe(
                new RawPacket(port, "$TELE,x*7F", DateTime.UtcNow),
                new TelemetryPacket("PSFB-01", channel, 48.2, "V", DateTime.UtcNow));
        }

        return inventory;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void NobodyLookingReadsDifferentlyFromNothingArriving()
    {
        // The distinction this whole product is organised around, at the smallest scale it occurs.
        // An empty table rendered for both tells an operator their rig is silent when the truth is
        // that nothing was ever asked.
        JsonElement absent = Query(inventory: null);
        absent.GetProperty("tracking").GetBoolean().Should().BeFalse();
        absent.GetProperty("reason").GetString().Should().Contain("not keeping an input inventory");

        JsonElement present = Query(new InputInventory());
        present.GetProperty("tracking").GetBoolean().Should().BeTrue();
        present.GetProperty("ports").GetArrayLength().Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void InputsAreGroupedByThePortRatherThanListedFlat()
    {
        JsonElement reply = Query(Inventory(
            ("COM3", "psfb.output_voltage"),
            ("COM3", "psfb.output_current"),
            ("COM4", "dab.input_voltage")));

        JsonElement ports = reply.GetProperty("ports");
        ports.GetArrayLength().Should().Be(2);

        JsonElement com3 = ports.EnumerateArray().Single(p => p.GetProperty("port").GetString() == "COM3");
        com3.GetProperty("channels").GetArrayLength().Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ACadenceNobodyCouldHaveMeasuredIsNullRatherThanZero()
    {
        JsonElement reply = Query(Inventory(("COM3", "psfb.output_voltage")));

        JsonElement channel = reply.GetProperty("ports")[0].GetProperty("channels")[0];
        channel.GetProperty("meanIntervalSec").ValueKind.Should().Be(JsonValueKind.Null,
            "one sighting establishes that the channel exists and nothing about how often it speaks");
        channel.GetProperty("samples").GetInt64().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void SilenceIsReportedSoAViewCanGreyARowRatherThanDrawItAsCurrent()
    {
        var inventory = new InputInventory();
        DateTime at = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        inventory.Observe(
            new RawPacket("COM3", "$TELE,x*7F", at),
            new TelemetryPacket("PSFB-01", "psfb.output_voltage", 48.2, "V", at));

        JsonElement reply = Query(inventory, new DateTimeOffset(at.AddSeconds(90), TimeSpan.Zero));

        reply.GetProperty("ports")[0].GetProperty("channels")[0]
            .GetProperty("silenceSec").GetDouble().Should().BeApproximately(90, 0.001);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ACappedListSaysItWasCapped()
    {
        var inventory = new InputInventory(capacity: 32);
        for (int i = 0; i < 200; i++)
        {
            inventory.Observe(
                new RawPacket("COM3", "$TELE,x*7F", DateTime.UtcNow),
                new TelemetryPacket("PSFB-01", $"runaway_{i}", i, "V", DateTime.UtcNow));
        }

        JsonElement reply = Query(inventory);
        reply.GetProperty("evicted").GetInt64().Should().BeGreaterThan(0,
            "a list showing a subset of the rig without saying so is the coverage failure in miniature");
    }

    /// <summary>An inventory holding one channel with a chosen unit and a chosen range.</summary>
    private static InputInventory Rig(string channel, string unit, params double[] values)
    {
        var inventory = new InputInventory();
        foreach (double value in values)
        {
            inventory.Observe(
                new RawPacket("COM3", "$TELE,x*7F", DateTime.UtcNow),
                new TelemetryPacket("PSFB-01", channel, value, unit, DateTime.UtcNow));
        }

        return inventory;
    }

    private static JsonElement FirstClassification(InputInventory inventory) =>
        Query(inventory).GetProperty("ports")[0].GetProperty("channels")[0].GetProperty("classification");

    [Fact]
    [Trait("Category", "Tier1")]
    public void AKindNeverTravelsWithoutTheConfidenceThatQualifiesIt()
    {
        // The wire is where a taxonomy stops being careful, because a consumer that reads 'kind'
        // and nothing else will pick an axis and an alarm band from a guess. Every field that
        // qualifies the kind is sent with it rather than left to be derived.
        JsonElement declared = FirstClassification(Rig("dab.bus_voltage", "V", 401.0, 402.5));

        declared.GetProperty("kind").GetString().Should().Be("electricPotential");
        declared.GetProperty("ucumUnit").GetString().Should().Be("V");
        declared.GetProperty("subsystem").GetString().Should().Be("dab");
        declared.GetProperty("confidence").GetString().Should().Be("high");
        declared.GetProperty("proposal").GetBoolean().Should().BeFalse();
        declared.GetProperty("disputed").GetBoolean().Should().BeFalse();
        declared.GetProperty("evidence").GetArrayLength().Should().BeGreaterThan(0);
        declared.GetProperty("why").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ARigOfPositionallyNamedChannelsSaysNobodyKnowsWhatTheyAreRatherThanGuessing()
    {
        // The state every rig is in before a rules file exists. There is no proposal to offer for
        // field1 and inventing one is the defect the whole taxonomy exists to prevent, so the
        // summary has to make the size of the gap visible instead.
        var inventory = new InputInventory();
        for (int i = 1; i <= 4; i++)
        {
            inventory.Observe(
                new RawPacket("COM3", "$TELE,x*7F", DateTime.UtcNow),
                new TelemetryPacket("MCU_1", $"field{i}", 20.5 + i, string.Empty, DateTime.UtcNow));
        }

        JsonElement taxonomy = Query(inventory).GetProperty("taxonomy");
        taxonomy.GetProperty("unclassified").GetInt32().Should().Be(4);
        taxonomy.GetProperty("classified").GetInt32().Should().Be(0);
        taxonomy.GetProperty("proposed").GetInt32().Should().Be(0);
        taxonomy.GetProperty("subsystems").GetArrayLength().Should().Be(0,
            "a rig whose names declare no hierarchy has no subsystems, not one called default");

        FirstClassification(inventory).GetProperty("why").GetString()
            .Should().Contain("routing rule", "an operator needs a next step, not a dead end");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APlantedMislabelIsReportedAsDisputedOnTheWire()
    {
        JsonElement mislabelled = FirstClassification(Rig("psfb.bus_voltage", "A", 3.1, 3.4));

        mislabelled.GetProperty("disputed").GetBoolean().Should().BeTrue();
        mislabelled.GetProperty("proposal").GetBoolean().Should().BeTrue();
        Query(Rig("psfb.bus_voltage", "A", 3.1)).GetProperty("taxonomy")
            .GetProperty("disputed").GetInt32().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheRangeThatCanVetoAClassificationIsPublishedBesideIt()
    {
        // A veto whose input nobody can see is not auditable, and this is the one input to a
        // classification that changes after the channel has been running for a while.
        JsonElement channel = Query(Rig("core.temperature", "Cel", 21.0, -400.0, 22.0))
            .GetProperty("ports")[0].GetProperty("channels")[0];

        channel.GetProperty("observedMin").GetDouble().Should().Be(-400.0);
        channel.GetProperty("observedMax").GetDouble().Should().Be(22.0);
        channel.GetProperty("classification").GetProperty("disputed").GetBoolean().Should().BeTrue();
    }
}
