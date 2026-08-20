using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The query API's obligation: return no more points than the caller can draw, and state plainly
/// what those points are.
/// </summary>
/// <remarks>
/// A consumer handed 2,000 points cannot tell 2,000 samples from two million reduced 1000:1. Every
/// test here is about making that distinction impossible to miss.
/// </remarks>
public class SeriesQueryTests
{
    private const string Channel = "NODE_7.temp";

    private static SeriesStore StoreWith(int samples, double startSec = 1_000.0, double stepSec = 0.001)
    {
        var store = new SeriesStore(samplesPerChannel: Math.Max(samples, 2));
        for (int i = 0; i < samples; i++)
        {
            store.Append(Channel, Math.Sin(i * 0.01) * 5.0 + 20.0, startSec + (i * stepSec));
        }
        return store;
    }

    /// <summary>Twenty thousand flat samples with one excursion, in timestamp order.</summary>
    private static SeriesStore FlatStoreWithSpikeAt(int index, double spike)
    {
        var store = new SeriesStore(samplesPerChannel: 20_000);
        for (int i = 0; i < 20_000; i++)
        {
            store.Append(Channel, i == index ? spike : 10.0, 1_000.0 + (i * 0.001));
        }
        return store;
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_ReturnsNoMorePointsThanTheCallerCanDraw()
    {
        var service = new SeriesQueryService(StoreWith(500_000));

        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 0.0, double.MaxValue, 2_000));

        result.Series.Should().HaveCount(1);
        result.Series[0].Points.Length.Should().BeLessThanOrEqualTo(2_000);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_ReportsTheTrueSampleCountBehindTheReduction()
    {
        var service = new SeriesQueryService(StoreWith(500_000));

        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 0.0, double.MaxValue, 2_000));

        ReductionMetadata metadata = result.Series[0].Metadata;
        metadata.SourceSampleCount.Should().Be(500_000, "the reply must say how much data is behind it");
        metadata.ReturnedPointCount.Should().Be(result.Series[0].Points.Length);
        metadata.DiscardedSampleCount.Should().Be(500_000 - metadata.ReturnedPointCount);
        metadata.CompressionRatio.Should().BeApproximately(500_000.0 / metadata.ReturnedPointCount, 0.001);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_NamesTheReductionAndTheBucketWidth()
    {
        // 100,000 samples one millisecond apart is a 99.999 second window. A budget of 2,000
        // points buys 1,000 buckets, so one bucket is 0.099999 seconds wide.
        var service = new SeriesQueryService(StoreWith(100_000));

        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 0.0, double.MaxValue, 2_000, ReductionMethod.MinMax));

        ReductionMetadata metadata = result.Series[0].Metadata;
        metadata.Method.Should().Be(ReductionMethod.MinMax);
        metadata.PreservesExtremes.Should().BeTrue();
        metadata.BucketWidthSec.Should().BeApproximately(99.999 / 1_000.0, 1e-9);
        metadata.WindowStartSec.Should().Be(1_000.0);
        metadata.WindowEndSec.Should().BeApproximately(1_099.999, 1e-6);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_DoesNotClaimAReductionWhenTheWindowAlreadyFits()
    {
        var service = new SeriesQueryService(StoreWith(100));

        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 0.0, double.MaxValue, 2_000));

        result.IsReduced.Should().BeFalse();
        result.Series[0].Metadata.Method.Should().Be(ReductionMethod.None);
        result.Series[0].Metadata.DiscardedSampleCount.Should().Be(0);
        result.Series[0].Points.Length.Should().Be(100);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_CarriesTheTrueExtremesEvenWhenTheReductionCannotPreserveThem()
    {
        var service = new SeriesQueryService(FlatStoreWithSpikeAt(5_000, 987.5));

        SeriesQueryResult lttb = service.Execute(new SeriesQueryRequest(
            new[] { Channel }, 0.0, double.MaxValue, 500, ReductionMethod.LargestTriangleThreeBuckets));

        // The reduction may or may not have kept the spike. What it may never do is deny it
        // happened: the true maximum is reported whether or not it survived into the points.
        lttb.Series[0].Metadata.SourceMaximum.Should().Be(987.5);
        lttb.Series[0].Metadata.PreservesExtremes.Should().BeFalse();
        lttb.Series[0].Metadata.DiscardedDescription.Should().Contain("NOT preserved");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_KeepsTheSpikeWhenTheReductionPromisesTo()
    {
        var service = new SeriesQueryService(FlatStoreWithSpikeAt(5_000, 987.5));

        SeriesQueryResult minMax = service.Execute(new SeriesQueryRequest(
            new[] { Channel }, 0.0, double.MaxValue, 500, ReductionMethod.MinMax));

        minMax.Series[0].Points.Select(p => p.Value).Should().Contain(987.5);
        minMax.Series[0].Metadata.PreservesExtremes.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_ReturnsASilentChannelAsEmptyRatherThanOmittingIt()
    {
        var service = new SeriesQueryService(StoreWith(10));

        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel, "NODE_9.pressure" }, 0.0, double.MaxValue, 2_000));

        // A channel dropped from the reply reads exactly like a channel the caller forgot to ask
        // for. A dead sensor must be visibly dead.
        result.Series.Should().HaveCount(2);
        result.Series[1].Channel.Should().Be("NODE_9.pressure");
        result.Series[1].Points.Should().BeEmpty();
        result.Series[1].Metadata.SourceSampleCount.Should().Be(0);
        double.IsNaN(result.Series[1].Metadata.SourceMinimum).Should().BeTrue(
            "an empty channel has no minimum, and reporting 0 would put a number on the axis that no sensor produced");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_SaysWhenRetentionRatherThanSilenceTruncatedTheWindow()
    {
        // The ring holds 1,000 samples; 5,000 were written. The oldest 4,000 seconds of the
        // requested window are gone because of retention, not because the sensor was quiet.
        var store = new SeriesStore(samplesPerChannel: 1_000);
        for (int i = 0; i < 5_000; i++) store.Append(Channel, i, 1_000.0 + i);

        var service = new SeriesQueryService(store);
        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 1_000.0, 10_000.0, 100));

        result.Series[0].Metadata.WindowTruncatedByRetention.Should().BeTrue();
        result.Series[0].Metadata.WindowStartSec.Should().Be(5_000.0);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Query_RestrictsItselfToTheRequestedWindow()
    {
        var store = new SeriesStore(samplesPerChannel: 1_000);
        for (int i = 0; i < 1_000; i++) store.Append(Channel, i, 1_000.0 + i);

        var service = new SeriesQueryService(store);
        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { Channel }, 1_100.0, 1_199.0, 2_000));

        result.Series[0].Metadata.SourceSampleCount.Should().Be(100);
        result.Series[0].Points.First().TimestampSec.Should().Be(1_100.0);
        result.Series[0].Points.Last().TimestampSec.Should().Be(1_199.0);
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Store_RefusesNewChannelsPastItsCeilingAndCountsWhatItRefused()
    {
        var store = new SeriesStore(samplesPerChannel: 4, maxChannels: 3);

        for (int i = 0; i < 10; i++) store.Append($"channel_{i}", i, 1_000.0 + i);

        store.ChannelCount.Should().Be(3);
        store.SamplesRefused.Should().Be(7,
            "a channel that was never admitted plots as blank, and the operator must be able to find out why");
    }

    [Fact]
    [Trait("Category", "SeriesQuery")]
    public void Request_RefusesABudgetTooSmallForTheChosenReduction()
    {
        Action build = () => new SeriesQueryRequest(new[] { Channel }, 0, 1, 1, ReductionMethod.MinMax);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }
}
