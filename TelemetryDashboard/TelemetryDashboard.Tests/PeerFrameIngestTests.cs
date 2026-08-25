using System.Text.Json;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What happens when this product's own output arrives back as its input.
/// </summary>
/// <remarks>
/// Measured on a live pair of hosts before any of this was written: host A running
/// <c>--simulate</c>, host B started with <c>--sse http://127.0.0.1:PORT/stream</c>. Nothing
/// recognised A's frames, so they fell through to <see cref="RawPayloadParser"/> — whose contract
/// is one channel per numeric property of an unknown object — and B ended up holding:
/// <list type="bullet">
/// <item><description>
/// <c>value</c>, one series carrying every channel A had. Its points alternated between vibration
/// in g and a figure near 1000 rpm: ARCHITECTURE §2's two datasets interleaved, and §2 is right
/// that nothing in the numbers reveals it.
/// </description></item>
/// <item><description><c>anomalyScore</c> with 1,292 samples and <c>predicted</c> with 783 — A's
/// judgements ingested as measurements, which B then scored, publishing an anomaly score of an
/// anomaly score.</description></item>
/// <item><description>every unit dropped: <c>°C</c>, <c>%</c> and <c>g</c> all arrived empty.</description></item>
/// <item><description><c>port</c>, holding 8074, from the stream's opening connection event.</description></item>
/// <item><description><c>"simulated": false</c> on everything B republished, while A had marked
/// every frame <c>true</c>. Synthetic data laundered into measured data in one hop — the exact
/// failure <c>ITelemetrySource</c>'s own summary describes.</description></item>
/// </list>
/// </remarks>
public class PeerFrameIngestTests
{
    private static RawPacket Arriving(string payload, DateTime? receivedUtc = null) =>
        new("127.0.0.1", payload, receivedUtc ?? new DateTime(2026, 8, 25, 2, 46, 0, DateTimeKind.Utc));

    /// <summary>A frame exactly as the outbound path builds it, so reader and writer stay married.</summary>
    private static string FrameFor(TelemetryPacket packet, bool simulated) =>
        JsonSerializer.Serialize(TelemetryFrame.Create(
            packet,
            new AnomalyResult { AnalyzerId = "zscore-rolling/w50/t2.5/n5", ZScore = 2.7, IsAnomaly = true },
            "SIMULATED", simulated, "SIM"));

    [Fact]
    [Trait("Category", "Tier1")]
    public void AFrameThisProductWroteParsesBackToTheSampleItDescribes()
    {
        // The round trip is the guarantee. A rule matching field names would pass the day somebody
        // renamed one on both sides and broke neither, and fail the day the writer gained a field
        // the reader does not need. This fails only when the two genuinely stop agreeing.
        var original = new TelemetryPacket("PSFB-01", "dab.bus_voltage", 401.25, "V",
            new DateTime(2026, 8, 25, 2, 40, 0, DateTimeKind.Utc));

        List<TelemetryPacket> parsed = RawPayloadParser.Parse(Arriving(FrameFor(original, simulated: false)));

        parsed.Should().ContainSingle(
            "one frame describes one reading; the last-resort parser emitted five, one per numeric "
            + "property, and called them value, anomalyScore, predicted and predictedHorizonSec");
        parsed[0].NodeId.Should().Be("PSFB-01");
        parsed[0].Variable.Should().Be("dab.bus_voltage");
        parsed[0].Value.Should().Be(401.25);
        parsed[0].Unit.Should().Be("V", "a reading without its unit is a number, not a measurement");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThePeersVerdictsDoNotBecomeThisHostsMeasurements()
    {
        // An anomaly score is a claim about the baseline the sending host holds, and a limit breach
        // is measured against limits it was configured with. Neither travels with the number.
        string frame = JsonSerializer.Serialize(new
        {
            timestamp = "2026-08-25T02:40:00.0000000Z",
            nodeId = "PSFB-01",
            variable = "dab.bus_voltage",
            value = 401.25,
            unit = "V",
            anomalyScore = 2.7,
            isAnomaly = true,
            predicted = 512.0,
            predictedHorizonSec = 2.0
        });

        List<TelemetryPacket> parsed = RawPayloadParser.Parse(Arriving(frame));

        parsed.Should().ContainSingle();
        parsed.Select(p => p.Variable).Should().NotContain(["anomalyScore", "predicted", "predictedHorizonSec"]);
        parsed[0].Flags.HasFlag(PacketFlags.AlarmExceeded).Should().BeFalse(
            "adopting the peer's breach would let its configuration decide what this host alarms on");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSyntheticMarkSurvivesTheHop()
    {
        var original = new TelemetryPacket("generic-machine", "ambient.temperature", 22.3, "°C");

        List<TelemetryPacket> parsed = RawPayloadParser.Parse(Arriving(FrameFor(original, simulated: true)));

        parsed.Should().ContainSingle();
        parsed[0].Flags.HasFlag(PacketFlags.Simulated).Should().BeTrue(
            "the sending host marked it where it knew the answer; losing it here is how a "
            + "simulator's output gets republished as a measurement");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheMarkAlsoSurvivesTheRecordBoundaryItUsedToDieAt()
    {
        // Where it was actually lost. TelemetryPacket carried the flag in, DataRecord had nowhere
        // to put it, and NumericPacketStage rebuilds the packet from the record -- so the mark
        // reached the projection and no further.
        var packet = new TelemetryPacket("SIM:rig", "bus", 400.0, "V")
        {
            Flags = PacketFlags.Simulated,
            ObservedAt = new DateTime(2026, 8, 25, 2, 40, 0, DateTimeKind.Utc)
        };

        DataRecord record = TelemetryPacketProjection.ToRecord(packet);
        record.Synthetic.Should().BeTrue();
        record.ObservedAt.Should().NotBeNull();

        TelemetryPacketProjection.TryToPacket(record, out TelemetryPacket restored).Should().BeTrue();
        restored.Flags.HasFlag(PacketFlags.Simulated).Should().BeTrue();
        restored.ObservedAt.Should().Be(packet.ObservedAt);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSendersClockIsKeptBesideTheReceiptTimeRatherThanInsteadOfIt()
    {
        DateTime received = new(2026, 8, 25, 2, 46, 0, DateTimeKind.Utc);
        var original = new TelemetryPacket("PSFB-01", "bus", 400.0, "V",
            new DateTime(2026, 8, 25, 2, 40, 0, DateTimeKind.Utc));

        TelemetryPacket parsed = RawPayloadParser.Parse(
            Arriving(FrameFor(original, simulated: false), received)).Single();

        parsed.Timestamp.Should().Be(received,
            "placing a remote sample on this host's timeline needs the offset between the clocks, "
            + "and until that is measured a peer three hours out would scatter its data across the "
            + "chart with nothing saying why");
        parsed.ObservedAt.Should().Be(new DateTime(2026, 8, 25, 2, 40, 0, DateTimeKind.Utc));
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("1970-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("not a timestamp")]
    [InlineData("")]
    public void AnUnusableSenderClockIsRefusedRatherThanClamped(string sent)
    {
        // §7: input from the network is not more trustworthy than input from a serial cable, which
        // this codebase already drops rather than scrapes. A clamped timestamp is a number nobody
        // reported, and it would go on to be differenced against this host's clock and published as
        // a clock offset.
        string frame = JsonSerializer.Serialize(new
        {
            timestamp = sent, nodeId = "PSFB-01", variable = "bus", value = 400.0, unit = "V"
        });

        TelemetryPacket parsed = RawPayloadParser.Parse(Arriving(frame)).Single();

        parsed.ObservedAt.Should().BeNull();
        parsed.Value.Should().Be(400.0, "the reading is still good; only the clock was unreadable");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AClockThatIsMerelyWrongIsAcceptedBecauseThatIsTheThingWorthMeasuring()
    {
        // A peer fourteen hours out has a timezone bug, and refusing it would discard exactly the
        // observation that reveals the fault. The window excludes values that are not clock
        // readings at all, not values that are clock readings this host disagrees with.
        DateTime received = new(2026, 8, 25, 2, 46, 0, DateTimeKind.Utc);
        string frame = JsonSerializer.Serialize(new
        {
            timestamp = received.AddHours(-14).ToString("o"),
            nodeId = "PSFB-01", variable = "bus", value = 400.0, unit = "V"
        });

        RawPayloadParser.Parse(Arriving(frame, received)).Single()
            .ObservedAt.Should().Be(received.AddHours(-14));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void StreamHousekeepingIsNotATelemetryChannel()
    {
        // The stream opens with this, and it produced a channel called 'port' holding 8074.
        RawPayloadParser.Parse(Arriving("{\"event\":\"connected\",\"port\":8074}"))
            .Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnObjectThatIsNotAPeerFrameStillGoesToTheLastResortParser()
    {
        // The recogniser must not swallow the case it was carved out of. An unconfigured device
        // streaming JSON still gets positional names, which is the honest answer for a payload
        // nobody has written a routing rule for.
        List<TelemetryPacket> parsed = RawPayloadParser.Parse(Arriving("{\"a\":1.5,\"b\":2.5}"));

        parsed.Should().HaveCount(2);
        parsed.Select(p => p.Variable).Should().BeEquivalentTo(["a", "b"]);
    }
}
