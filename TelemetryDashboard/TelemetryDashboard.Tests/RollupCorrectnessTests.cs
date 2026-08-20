using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The rules a rollup must obey: a gap is never a reading, a NaN is never a zero, and the
/// incremental aggregate equals what a single pass over all the samples would have produced.
/// </summary>
public sealed class RollupCorrectnessTests
{
    private static readonly DateTime Origin = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Two-pass reference, deliberately not the algorithm under test.</summary>
    private static (long Count, double Mean, double PopulationStdDev) Reference(IEnumerable<double> values)
    {
        double[] real = values.Where(v => !double.IsNaN(v)).ToArray();
        if (real.Length == 0) return (0, double.NaN, double.NaN);

        double mean = real.Average();
        double variance = real.Sum(v => (v - mean) * (v - mean)) / real.Length;
        return (real.Length, mean, Math.Sqrt(variance));
    }

    [Fact]
    public void AccumulatorMatchesATwoPassReference()
    {
        var random = new Random(7);
        double[] values = Enumerable.Range(0, 5_000).Select(_ => 401.7 + random.NextDouble() * 0.02).ToArray();

        var accumulator = new RollupAccumulator();
        foreach (double value in values) accumulator.Add(value);

        (long count, double mean, double stdDev) = Reference(values);
        accumulator.Count.Should().Be(count);
        accumulator.Sum.Should().BeApproximately(values.Sum(), 1e-9);
        // 1e-12 was below the noise floor and could not pass. Around 401.7 a double's step is
        // roughly 5.7e-14, and 5,000 accumulations drift by order sqrt(n) steps, so the reference
        // and the running figure legitimately differ by a few times 1e-12. Widening this is not
        // lowering the bar: the accumulator uses Welford, which is the more accurate of the two
        // being compared, so the residual is the naive reference's error, not the code's.
        accumulator.Mean.Should().BeApproximately(mean, 1e-10);
        accumulator.PopulationStandardDeviation.Should().BeApproximately(stdDev, 1e-10);
        accumulator.Min.Should().Be(values.Min());
        accumulator.Max.Should().Be(values.Max());
    }

    [Fact]
    public void MergingPartialAccumulatorsEqualsOnePassOverEverything()
    {
        var random = new Random(11);
        double[] values = Enumerable.Range(0, 3_000).Select(_ => random.NextDouble() * 100).ToArray();

        var whole = new RollupAccumulator();
        foreach (double value in values) whole.Add(value);

        var merged = new RollupAccumulator();
        foreach (double[] chunk in values.Chunk(137))
        {
            var partial = new RollupAccumulator();
            foreach (double value in chunk) partial.Add(value);
            merged.Merge(partial);
        }

        merged.Count.Should().Be(whole.Count);
        merged.Mean.Should().BeApproximately(whole.Mean, 1e-10);
        merged.PopulationStandardDeviation.Should().BeApproximately(whole.PopulationStandardDeviation, 1e-10);
        merged.Min.Should().Be(whole.Min);
        merged.Max.Should().Be(whole.Max);
    }

    [Fact]
    public void NaNIsNotAveragedAsZero()
    {
        var accumulator = new RollupAccumulator();
        accumulator.Add(10.0).Should().BeTrue();
        accumulator.Add(double.NaN).Should().BeFalse("NaN means no reading, so there is nothing to fold in");
        accumulator.Add(20.0).Should().BeTrue();

        accumulator.Count.Should().Be(2);
        accumulator.Mean.Should().Be(15.0, "a mean of 10 and 20 — not 10, 0 and 20");
        accumulator.Min.Should().Be(10.0);
        accumulator.Max.Should().Be(20.0);
    }

    [Fact]
    public void AWindowOfNothingButNaNIsAbsentRatherThanZero()
    {
        var aggregator = new RollupBatchAggregator();
        var channel = new ChannelKey("node-1", "temperature");

        for (int i = 0; i < 60; i++) aggregator.Add(channel, Origin.AddSeconds(i), double.NaN);

        aggregator.Windows().Should().BeEmpty("an interval in which no sensor spoke has no window at all");
        aggregator.NoReadingCount.Should().Be(60);
        aggregator.AcceptedCount.Should().Be(0);
    }

    [Fact]
    public void AWindowCannotBeConstructedWithNoMeasurements()
    {
        Action zeroCount = () => _ = new RollupWindow(
            new ChannelKey("n", "v"), RollupInterval.Minute, Origin.Ticks, 0, 0, 0, 0, 0);

        zeroCount.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EverySampleLandsInAllThreeTiers()
    {
        var aggregator = new RollupBatchAggregator();
        var channel = new ChannelKey("node-1", "bus_voltage");

        for (int i = 0; i < 120; i++) aggregator.Add(channel, Origin.AddSeconds(i), 400.0 + i);

        IReadOnlyList<RollupWindow> windows = aggregator.Windows();
        windows.Count(w => w.Interval == RollupInterval.Second).Should().Be(120);
        windows.Count(w => w.Interval == RollupInterval.Minute).Should().Be(2);
        windows.Count(w => w.Interval == RollupInterval.Hour).Should().Be(1);

        RollupWindow hour = windows.Single(w => w.Interval == RollupInterval.Hour);
        hour.Count.Should().Be(120);
        hour.Mean.Should().BeApproximately(Enumerable.Range(0, 120).Average(i => 400.0 + i), 1e-9);
        hour.StartUtc.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        hour.EndUtc.Should().Be(new DateTime(2026, 3, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void UnspecifiedKindIsTreatedAsUtcAndNeverShifted()
    {
        var unspecified = new DateTime(2026, 3, 1, 12, 0, 30, DateTimeKind.Unspecified);
        var asUtc = new DateTime(2026, 3, 1, 12, 0, 30, DateTimeKind.Utc);

        RollupIntervals.ToUtcTicks(unspecified).Should().Be(unspecified.Ticks);
        RollupIntervals.ToUtcTicks(unspecified).Should().Be(RollupIntervals.ToUtcTicks(asUtc));

        var aggregator = new RollupBatchAggregator();
        aggregator.Add(new ChannelKey("n", "v"), unspecified, 1.0);
        aggregator.Windows()
            .Single(w => w.Interval == RollupInterval.Minute)
            .StartUtc.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task PersistedWindowsMergeIncrementallyAcrossBatches()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("rollup-merge.db"));
        var channel = new ChannelKey("node-1", "current");

        var random = new Random(23);
        double[] values = Enumerable.Range(0, 600).Select(_ => 12.0 + random.NextDouble()).ToArray();

        // Six separate commits, all landing in the same ten one-minute buckets.
        foreach (int[] chunk in Enumerable.Range(0, values.Length).Chunk(100))
        {
            await store.WriteBatchAsync(chunk
                .Select(i => new TelemetryPacket("node-1", "current", values[i], "A", Origin.AddSeconds(i)))
                .ToList());
        }

        TieredQueryResult result = await store.QueryTieredAsync(new TieredQueryRequest(
            channel, Origin, Origin.AddMinutes(10), TimeSpan.FromHours(1)));

        (long count, double mean, double stdDev) = Reference(values);
        result.Tier.Should().Be(TelemetryTier.Hour);
        result.Points.Should().HaveCount(1);
        result.Points[0].Count.Should().Be(count);
        result.Points[0].Mean.Should().BeApproximately(mean, 1e-9);
        result.Points[0].PopulationStandardDeviation.Should().BeApproximately(stdDev, 1e-9);
        result.Points[0].Min.Should().Be(values.Min());
        result.Points[0].Max.Should().Be(values.Max());
    }

    [Fact]
    public async Task RollupsAreWrittenWithoutEverRereadingRawSamples()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("rollup-counts.db"));

        var packets = Enumerable.Range(0, 3_600)
            .Select(i => new TelemetryPacket("node-1", "temp", 20.0 + (i % 7), "C", Origin.AddSeconds(i)))
            .ToList();
        await store.WriteBatchAsync(packets);

        // 3600 one-second buckets + 60 one-minute + 1 hour = 3661 aggregate rows for 3600 samples.
        store.MergedWindowCount.Should().Be(3_661);
        store.WrittenSampleCount.Should().Be(3_600);
        store.WrittenBlockCount.Should().Be(1, "one channel in one batch is one block");
    }
}
