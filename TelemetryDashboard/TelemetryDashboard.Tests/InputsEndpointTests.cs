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
}
