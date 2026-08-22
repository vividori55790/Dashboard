using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Watching for the failure that has no value: a channel that has stopped reporting.
/// </summary>
/// <remarks>
/// A dead sensor looks exactly like a steady one. Every chart in this product draws the last value
/// it was given, so a converter whose link drops holds its final reading on screen, inside its
/// limits, with a z-score of zero because the distribution stopped moving too. The absence of
/// values is the whole failure and no value-watching alarm can see it.
/// <para>
/// Measured on a live host replaying a file where one channel stops two seconds in: at the six
/// second mark, "dying.interval[s] &lt; 2" read Breached at 2.94 s while "alive.interval[s] &lt; 2"
/// read Watching at 0.107 s. The dead channel raised the alarm and the live one did not.
/// </para>
/// </remarks>
public class ChannelIntervalTests
{
    private static DataRecord Sample(string stream, string key, DateTimeOffset at, string source = "COM3") => new()
    {
        Key = new DataKey(stream, key),
        Timestamp = at,
        Value = new DataValue.Numeric(1.0, "V"),
        Source = source
    };

    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    // ---- measuring the gap ----------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelsFirstSightingHasNoIntervalBecauseThereIsNoPreviousOne()
    {
        // Null, not zero. Zero is a measurement -- "these two arrived together" -- and seeding
        // every channel with one would put a false floor under any limit watching for silence.
        var projection = new ChannelIntervalProjection();

        projection.Measure(Sample("RIG", "temp", T0)).Should().BeNull();
        projection.Measure(Sample("RIG", "temp", T0.AddSeconds(0.25))).Should().Be(0.25);
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData(0.0, "two records sharing a timestamp")]
    [InlineData(-3.0, "a clock correction stamping one earlier than its predecessor")]
    public void ANonPositiveGapIsNotAnIntervalThatElapsed(double offset, string why)
    {
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "temp", T0));

        projection.Measure(Sample("RIG", "temp", T0.AddSeconds(offset))).Should().BeNull(why);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ChannelsAreTimedSeparatelyEvenWhenTheyShareANode()
    {
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "a", T0));
        projection.Measure(Sample("RIG", "b", T0));

        projection.Measure(Sample("RIG", "a", T0.AddSeconds(1))).Should().Be(1.0);
        projection.Measure(Sample("RIG", "b", T0.AddSeconds(4))).Should().Be(4.0);
        projection.TrackedChannels.Should().Be(2);
    }

    // ---- the sweep, which is the half that matters ----------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelRunningOnTimeIsNotSweptUp()
    {
        // A projection only speaks when a record arrives, so the sweep is what covers silence --
        // and a sweep that spoke about healthy channels would double the volume for nothing.
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "temp", T0));
        projection.Measure(Sample("RIG", "temp", T0.AddSeconds(1)));

        projection.Sweep(T0.AddSeconds(1.5)).Should().BeEmpty("half a second late is not late");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelThatHasStoppedIsReportedAndKeepsClimbing()
    {
        // Without this the feature cannot do the thing it exists for, and it fails quietly: no
        // record arrives, so no interval is published, so the last gap sits there inside whatever
        // limit was declared and the alarm never fires.
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "temp", T0));
        projection.Measure(Sample("RIG", "temp", T0.AddSeconds(1)));

        DataRecord first = projection.Sweep(T0.AddSeconds(4)).Should().ContainSingle().Subject;
        DataRecord later = projection.Sweep(T0.AddSeconds(9)).Should().ContainSingle().Subject;

        first.Key.Key.Should().Be("temp" + ChannelIntervalProjection.KeySuffix);
        ((DataValue.Numeric)first.Value).Value.Should().BeApproximately(3.0, 1e-9);
        ((DataValue.Numeric)later.Value).Value.Should().BeApproximately(8.0, 1e-9,
            "the series has to grow while the link is down, or one breach is all anyone ever sees");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASweptRecordSaysItWasDerivedAndWhoReportedTheChannel()
    {
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "temp", T0, source: "/dev/ttyUSB0"));
        projection.Measure(Sample("RIG", "temp", T0.AddSeconds(1), source: "/dev/ttyUSB0"));

        DataRecord swept = projection.Sweep(T0.AddSeconds(5)).Single();

        swept.IsDerived.Should().BeTrue();
        swept.DerivedFrom.Should().Be(ChannelIntervalProjection.ProjectionName);
        swept.Source.Should().Be("/dev/ttyUSB0", "a multi-port rig has to know which cable went quiet");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSilentChannelIsAttributedToItsOwnPortAndNotToWhicheverSpokeLast()
    {
        // The defect this replaced, and the one a single-port test can never see. The sweep took
        // one source from its caller -- "whichever port reported most recently" -- and stamped it
        // on every record it produced. On a two-port rig that is wrong exactly when it matters:
        // COM4's cable comes out, COM3 keeps reporting, and every record saying COM4 has gone quiet
        // names COM3. Which cable went quiet is the one fact this feature exists to supply.
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG_A", "temp", T0, source: "COM3"));
        projection.Measure(Sample("RIG_A", "temp", T0.AddSeconds(1), source: "COM3"));
        projection.Measure(Sample("RIG_B", "temp", T0, source: "COM4"));
        projection.Measure(Sample("RIG_B", "temp", T0.AddSeconds(1), source: "COM4"));

        // COM3 keeps talking; COM4 does not.
        projection.Measure(Sample("RIG_A", "temp", T0.AddSeconds(9), source: "COM3"));

        DataRecord silent = projection.Sweep(T0.AddSeconds(10)).Should().ContainSingle().Subject;

        silent.Key.Stream.Should().Be("RIG_B");
        silent.Source.Should().Be("COM4",
            "COM3 was the last port to speak, and it is not the one that went quiet");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ReconnectingStartsAFreshSeriesRatherThanReportingTheOutage()
    {
        // Carrying a timestamp across a reconnect reports the length of the outage as one interval
        // on the first sample back -- true, and the one reading guaranteed to breach every limit,
        // at the moment the link recovered.
        var projection = new ChannelIntervalProjection();
        projection.Measure(Sample("RIG", "temp", T0));

        projection.Reset();

        projection.TrackedChannels.Should().Be(0);
        projection.Measure(Sample("RIG", "temp", T0.AddHours(2))).Should().BeNull();
    }

    // ---- through the live record path -----------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task WithoutTheFlagNothingDerivedAppearsAtAll()
    {
        var published = new List<string>();
        var path = new IngestRecordPath(
            (packet, _, _) => { published.Add(packet.Variable); return ValueTask.CompletedTask; },
            isSimulated: false);

        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 41.9, "C"), "COM3");
        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 42.0, "C"), "COM3");

        published.Should().Equal(new[] { "temp", "temp" });
        path.Intervals.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task TheProjectionIsNotFedItsOwnOutput()
    {
        // The defect that made this class unusable in the arrangement it was written for. Its emit
        // target is the pipeline, and the pipeline offers every record to every stage matching the
        // value *shape* -- which a derived numeric record does. So the first record produced a
        // derivative, the derivative was offered straight back, and the key grew a suffix per turn
        // until the stack ended the process. Nothing had noticed, because nothing had ever
        // registered one of these in a live pipeline.
        var published = new List<string>();
        var path = new IngestRecordPath(
            (packet, _, _) => { published.Add(packet.Variable); return ValueTask.CompletedTask; },
            isSimulated: false,
            watchIntervals: true);

        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 41.9, "C"), "COM3");
        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 42.0, "C"), "COM3");

        published.Should().Equal(new[] { "temp", "temp", "temp.interval" });
        published.Should().NotContain(v => v.Contains("interval.interval", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ADerivedChannelIsAttributedToThePortItsSourceArrivedOn()
    {
        // Measured live before this was fixed: every derived channel published with an empty port
        // beside a measured one reading "SIM", so on a multi-port rig nothing said which cable a
        // '.interval' channel belonged to.
        var ports = new List<(string Variable, string Port)>();
        var path = new IngestRecordPath(
            (packet, port, _) => { ports.Add((packet.Variable, port)); return ValueTask.CompletedTask; },
            isSimulated: false,
            watchIntervals: true);

        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 41.9, "C"), "/dev/ttyUSB0");
        await path.OfferPacketAsync(new TelemetryPacket("MCU_A", "temp", 42.0, "C"), "/dev/ttyUSB0");

        ports.Should().OnlyContain(p => p.Port == "/dev/ttyUSB0");
    }
}
