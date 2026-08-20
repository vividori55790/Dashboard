using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The query path over the tiers: which tier answers, whether the caller can tell, and what
/// happens to a window nobody measured.
/// </summary>
public sealed class TieredStorageQueryTests
{
    private static readonly DateTime Origin = new(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ChannelKey Channel = new("node-7", "bus_voltage");

    private static List<TelemetryPacket> Samples(int count, DateTime from, double start = 400.0) =>
        Enumerable.Range(0, count)
            .Select(i => new TelemetryPacket(
                Channel.NodeId, Channel.Variable, start + Math.Sin(i / 60.0), "V", from.AddSeconds(i)))
            .ToList();

    [Fact]
    public async Task RawTierAnswersWhenNoResolutionIsRequested()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-raw.db"));
        await store.WriteBatchAsync(Samples(300, Origin));

        TieredQueryResult result = await store.QueryTieredAsync(
            new TieredQueryRequest(Channel, Origin, Origin.AddSeconds(299)));

        result.Tier.Should().Be(TelemetryTier.Raw);
        result.IsRaw.Should().BeTrue();
        result.Points.Should().HaveCount(300);
        result.Points.Should().OnlyContain(p => p.IsSingleSample);
        result.Points[0].StartUtc.Should().Be(result.Points[0].EndUtc, "a raw point is an instant, not a span");
        result.Describe().Should().Contain("Raw");
    }

    [Theory]
    [InlineData(1, TelemetryTier.Second)]
    [InlineData(60, TelemetryTier.Minute)]
    [InlineData(90, TelemetryTier.Minute)]
    [InlineData(3_600, TelemetryTier.Hour)]
    [InlineData(7_200, TelemetryTier.Hour)]
    public async Task TheCoarsestTierMeetingTheRequestedResolutionAnswers(int seconds, TelemetryTier expected)
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File($"tier-{seconds}.db"));
        await store.WriteBatchAsync(Samples(3_600, Origin));

        TieredQueryResult result = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Origin, Origin.AddHours(1), TimeSpan.FromSeconds(seconds)));

        result.Tier.Should().Be(expected);
        result.Resolution.Should().Be(expected.Resolution());
        result.IsRaw.Should().BeFalse();
    }

    [Fact]
    public async Task AnHourAverageIsDistinguishableFromARawSample()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-distinguish.db"));
        await store.WriteBatchAsync(Samples(3_600, Origin));

        TieredQueryResult raw = await store.QueryTieredAsync(
            new TieredQueryRequest(Channel, Origin, Origin.AddSeconds(10)));
        TieredQueryResult hourly = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Origin, Origin.AddHours(1), TimeSpan.FromHours(1)));

        raw.Tier.Should().Be(TelemetryTier.Raw);
        hourly.Tier.Should().Be(TelemetryTier.Hour);
        hourly.Points.Should().HaveCount(1);
        hourly.Points[0].Count.Should().Be(3_600, "the point states how many samples stand behind it");
        hourly.Points[0].EndUtc.Should().Be(hourly.Points[0].StartUtc.AddHours(1));
        hourly.Describe().Should().Contain("Hour");
    }

    [Fact]
    public async Task AWindowNobodyMeasuredHasNoPoint()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-gap.db"));

        await store.WriteBatchAsync(Samples(180, Origin));                       // minutes 0,1,2
        await store.WriteBatchAsync(Samples(180, Origin.AddMinutes(5), 402.0));  // minutes 5,6,7

        TieredQueryResult result = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Origin, Origin.AddMinutes(8), TimeSpan.FromMinutes(1)));

        result.Tier.Should().Be(TelemetryTier.Minute);
        result.Points.Should().HaveCount(6, "the three silent minutes have no window at all");
        result.Points.Select(p => p.StartUtc.Minute).Should().Equal(0, 1, 2, 5, 6, 7);
        result.Points.Should().OnlyContain(p => p.Count > 0);
    }

    [Fact]
    public async Task ANaNSampleStaysNaNAtTheRawTierAndIsCountedNowhereElse()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-nan.db"));

        var packets = new List<TelemetryPacket>
        {
            new(Channel.NodeId, Channel.Variable, 10.0, "V", Origin),
            new(Channel.NodeId, Channel.Variable, double.NaN, "V", Origin.AddSeconds(1)),
            new(Channel.NodeId, Channel.Variable, 20.0, "V", Origin.AddSeconds(2))
        };
        await store.WriteBatchAsync(packets);

        TieredQueryResult raw = await store.QueryTieredAsync(
            new TieredQueryRequest(Channel, Origin, Origin.AddSeconds(2)));
        raw.Points.Should().HaveCount(3);
        double.IsNaN(raw.Points[1].Mean).Should().BeTrue("a failed reading is recorded, not erased");

        TieredQueryResult minute = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Origin, Origin.AddMinutes(1), TimeSpan.FromMinutes(1)));
        minute.Points[0].Count.Should().Be(2, "the NaN is not a measurement");
        minute.Points[0].Mean.Should().Be(15.0);
        store.NoReadingCount.Should().Be(1);
    }

    [Fact]
    public async Task PacketsRoundTripThroughTheDataLoggerContract()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-contract.db"));

        var written = new List<TelemetryPacket>
        {
            new(Channel.NodeId, Channel.Variable, 401.25, "V", Origin, PacketFlags.Simulated),
            new(Channel.NodeId, Channel.Variable, double.NaN, "V", Origin.AddSeconds(1), PacketFlags.ChecksumFailed),
            new(Channel.NodeId, Channel.Variable, -0.0, "V", Origin.AddSeconds(2))
        };
        await store.WriteBatchAsync(written);

        List<TelemetryPacket> read = (await store.QueryAsync(new QueryFilter(Limit: 10))).ToList();

        read.Should().HaveCount(3);
        read.Select(p => p.Flags).Should().Equal(
            PacketFlags.Simulated, PacketFlags.ChecksumFailed, PacketFlags.None);
        read.Select(p => p.Unit).Should().OnlyContain(u => u == "V");
        double.IsNaN(read[1].Value).Should().BeTrue();
        BitConverter.DoubleToInt64Bits(read[2].Value).Should().Be(BitConverter.DoubleToInt64Bits(-0.0));
        read[0].Timestamp.Should().Be(Origin);
        read[0].Timestamp.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task AnUnspecifiedTimestampIsStoredAsTheSameInstantAsUtc()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-kind.db"));

        var unspecified = new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Unspecified);
        await store.WriteBatchAsync(new List<TelemetryPacket>
        {
            new(Channel.NodeId, Channel.Variable, 42.0, "V", unspecified)
        });

        List<TelemetryPacket> read = (await store.QueryAsync(new QueryFilter(Limit: 10))).ToList();
        read.Should().ContainSingle();
        read[0].Timestamp.Should().Be(new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ABatchThatFailsLeavesNothingBehind()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-atomic.db"));
        await store.WriteBatchAsync(Samples(10, Origin));

        var withNull = new List<TelemetryPacket?>(Samples(10, Origin.AddMinutes(1))) { null };
        Func<Task> write = () => store.WriteBatchAsync(withNull!);
        await write.Should().ThrowAsync<ArgumentException>();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Func<Task> cancelledWrite = () => store.WriteBatchAsync(Samples(10, Origin.AddMinutes(2)), cancelled.Token);
        await cancelledWrite.Should().ThrowAsync<OperationCanceledException>();

        List<TelemetryPacket> read = (await store.QueryAsync(new QueryFilter(Limit: 1_000))).ToList();
        read.Should().HaveCount(10, "only the batch that committed is in the store");
        store.WrittenSampleCount.Should().Be(10);

        TieredQueryResult minutes = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Origin, Origin.AddMinutes(5), TimeSpan.FromMinutes(1)));
        minutes.Points.Should().ContainSingle("a rolled-back batch must not leave a rollup behind either");
    }

    [Fact]
    public async Task AnOverlongAnswerIsTruncatedRatherThanSilentlyClipped()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("tier-truncate.db"));
        await store.WriteBatchAsync(Samples(600, Origin));

        TieredQueryResult result = await store.QueryTieredAsync(
            new TieredQueryRequest(Channel, Origin, Origin.AddSeconds(599), Resolution: null, MaxPoints: 100));

        result.Points.Should().HaveCount(100);
        result.Truncated.Should().BeTrue();
        result.Describe().Should().Contain("truncated");
    }
}
