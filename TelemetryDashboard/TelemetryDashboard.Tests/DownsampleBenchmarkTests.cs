using System.Diagnostics;
using System.Text.Json;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Ingest;
using Xunit.Abstractions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Measures the reduction rather than asserting it is fast: points in, points out, wall clock,
/// bytes allocated, and the bytes that leave the machine.
/// </summary>
/// <remarks>
/// The thresholds are deliberately loose — an order of magnitude above what the code does on a
/// developer machine — because this suite runs on shared CI agents where a tight bound would fail
/// for reasons that have nothing to do with the algorithm. The numbers themselves are written to
/// the test output, which is what the claims in the report are drawn from.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public class DownsampleBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public DownsampleBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>One second of a 1 MHz channel: the scale this work exists for.</summary>
    private const int SamplesPerSecond = 1_000_000;

    private static SeriesPoint[] Signal(int count, double spikeFraction = 0.37, double spikeValue = 9_999.0)
    {
        var points = new SeriesPoint[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new SeriesPoint(i / (double)SamplesPerSecond, Math.Sin(i * 0.0001) * 12.0 + 240.0);
        }

        int spike = (int)(count * spikeFraction);
        points[spike] = new SeriesPoint(points[spike].TimestampSec, spikeValue);
        return points;
    }

    [Theory]
    [Trait("Category", "Downsample")]
    [InlineData(1_000_000, 2_000)]
    [InlineData(1_000_000, 500)]
    [InlineData(100_000, 2_000)]
    [InlineData(10_000, 2_000)]
    public void Benchmark_MinMaxReduction(int count, int budget)
    {
        SeriesPoint[] source = Signal(count);
        var destination = new SeriesPoint[budget];

        // Warmed more than once, and the allocation taken as the minimum of several runs -- the
        // same treatment Benchmark_LttbReduction below has carried for a while, and which this
        // twin was simply never given.
        //
        // A single warm-up call leaves the method at tier-0. The measured call can then be
        // re-jitted mid-measurement, and the re-JIT allocates on this thread -- so the reduction
        // was charged for bytes it never asked for. In isolation that almost never happened; inside
        // the full suite, where the machine is busy enough to shift the tiering, it produced a
        // failure roughly one run in three and never reproduced when the test was run alone.
        //
        // Minimum-of-N is the right estimator here rather than a widened bound: the true allocation
        // is zero, every source of noise can only add, and a run that allocates nothing proves the
        // claim outright. Widening the bound would have made the test pass without making it true.
        const int Runs = 5;
        for (int i = 0; i < Runs; i++) MinMaxDownsampler.Reduce(source, budget, destination);

        var clock = new Stopwatch();
        long allocated = long.MaxValue;
        double fastestMs = double.MaxValue;
        int written = 0;

        for (int i = 0; i < Runs; i++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            clock.Restart();
            written = MinMaxDownsampler.Reduce(source, budget, destination);
            clock.Stop();

            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            fastestMs = Math.Min(fastestMs, clock.Elapsed.TotalMilliseconds);
        }

        _output.WriteLine(
            $"min/max  in={count,9:N0}  out={written,5:N0}  ratio={count / (double)written,8:N1}:1  " +
            $"wall={fastestMs,7:F3} ms  alloc={allocated,6:N0} B  (best of {Runs})");

        written.Should().BeLessThanOrEqualTo(budget);
        allocated.Should().Be(0, "the reduction writes into the caller's buffer and allocates nothing");
        destination.Take(written).Select(p => p.Value).Should().Contain(9_999.0,
            "the injected spike must survive at every ratio");
    }

    [Theory]
    [Trait("Category", "Downsample")]
    [InlineData(1_000_000, 2_000)]
    [InlineData(100_000, 2_000)]
    public void Benchmark_LttbReduction(int count, int budget)
    {
        SeriesPoint[] source = Signal(count);
        var destination = new SeriesPoint[budget];

        LttbDownsampler.Reduce(source, budget, destination);

        // The lowest of several runs, not a single one. A single measured call occasionally reports
        // a few kilobytes that the reduction did not ask for: the runtime re-compiles a hot method
        // at a higher tier while it is running, and that work is charged to whichever thread
        // triggered it. It happens once, unpredictably, and only under the load of a full suite —
        // this assertion passed alone and failed at 8,160 bytes among 879 other tests.
        //
        // Taking the minimum keeps the strong claim intact. If the steady state genuinely allocates
        // nothing, at least one run of five will show zero; if the reduction starts allocating per
        // call, every run shows it and the floor rises. Loosening the bound instead would have hidden
        // exactly the regression this test exists to catch.
        const int Runs = 5;
        var clock = new Stopwatch();
        long allocated = long.MaxValue;
        double fastestMs = double.MaxValue;
        int written = 0;

        for (int attempt = 0; attempt < Runs; attempt++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            clock.Restart();
            written = LttbDownsampler.Reduce(source, budget, destination);
            clock.Stop();
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            fastestMs = Math.Min(fastestMs, clock.Elapsed.TotalMilliseconds);
        }

        _output.WriteLine(
            $"lttb     in={count,9:N0}  out={written,5:N0}  ratio={count / (double)written,8:N1}:1  " +
            $"wall={fastestMs,7:F3} ms  alloc={allocated,6:N0} B  (best of {Runs})");

        written.Should().Be(budget);
        allocated.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Benchmark_EndToEndQueryIncludingItsOwnAllocations()
    {
        var store = new SeriesStore(samplesPerChannel: SamplesPerSecond);
        for (int i = 0; i < SamplesPerSecond; i++)
        {
            store.Append("PLANT_1.bus_voltage", Math.Sin(i * 0.0001) * 12.0 + 240.0, i / (double)SamplesPerSecond);
        }

        var service = new SeriesQueryService(store);
        var request = new SeriesQueryRequest(new[] { "PLANT_1.bus_voltage" }, 0.0, 1.0, 2_000);

        service.Execute(request);

        var clock = new Stopwatch();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        clock.Restart();
        SeriesQueryResult result = service.Execute(request);
        clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        _output.WriteLine(
            $"query    in={result.SourceSampleCount,9:N0}  out={result.ReturnedPointCount,5:N0}  " +
            $"ratio={result.CompressionRatio,8:N1}:1  wall={clock.Elapsed.TotalMilliseconds,7:F3} ms  " +
            $"alloc={allocated,7:N0} B");

        result.ReturnedPointCount.Should().BeLessThanOrEqualTo(2_000);

        // Allocation, not wall clock. The elapsed time is still printed above, because knowing a
        // million-sample query takes a few hundred milliseconds is worth having — but asserting on
        // it inside a suite xUnit runs in parallel means the verdict is decided by whatever else
        // happens to be running. This failed at 523 ms against a 500 ms budget in the full suite and
        // passed comfortably on its own, which measures the machine's load rather than this code.
        //
        // Allocated bytes per source sample is the figure that belongs to the query and nothing
        // else. A reduction that starts materialising intermediate lists shows up here immediately,
        // and it shows up the same way on every machine.
        double bytesPerSample = allocated / (double)result.SourceSampleCount;
        bytesPerSample.Should().BeLessThan(4,
            "the reduction streams over the source; it must not build a copy of it");
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Benchmark_WireBytesRawFanOutAgainstOneReducedFrame()
    {
        // The frame the streaming server actually broadcasts today, one per sample.
        var sample = new TelemetryFrame
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Source = "REAL_HARDWARE",
            Simulated = false,
            Port = "COM7",
            NodeId = "PLANT_1",
            Variable = "bus_voltage",
            Value = 241.8734,
            Unit = "V",
            AnomalyScore = 1.42,
            IsAnomaly = false,
            Predicted60s = 242.1,
            AnalyzerId = "rolling-zscore/128"
        };

        int rawFrameBytes = JsonSerializer.SerializeToUtf8Bytes(sample).Length;
        long rawPerSecond = (long)rawFrameBytes * SamplesPerSecond;

        var store = new SeriesStore(samplesPerChannel: SamplesPerSecond);
        for (int i = 0; i < SamplesPerSecond; i++)
        {
            store.Append("PLANT_1.bus_voltage", Math.Sin(i * 0.0001) * 12.0 + 240.0, i / (double)SamplesPerSecond);
        }

        var service = new SeriesQueryService(store);
        SeriesQueryResult result = service.Execute(
            new SeriesQueryRequest(new[] { "PLANT_1.bus_voltage" }, 0.0, 1.0, 2_000));

        int reducedFrameBytes = SeriesFrameWriter.Write(result, 1.0).Length;
        long reducedPerSecond = (long)reducedFrameBytes * 10;  // a 10 Hz subscriber

        _output.WriteLine($"raw      frame={rawFrameBytes,6:N0} B  x 1,000,000/s = {rawPerSecond / 1_048_576.0,9:N1} MB/s per subscriber");
        _output.WriteLine($"reduced  frame={reducedFrameBytes,6:N0} B  x        10/s = {reducedPerSecond / 1_048_576.0,9:N3} MB/s per subscriber");
        _output.WriteLine($"wire reduction factor = {rawPerSecond / (double)reducedPerSecond,9:N0}x");

        reducedPerSecond.Should().BeLessThan(rawPerSecond / 100,
            "the display path exists to make a viewer cost orders of magnitude less than ingest");
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Benchmark_SpikePreservationAcrossEveryRatio()
    {
        // The claim under test: min/max cannot lose an excursion, whatever the ratio. LTTB can,
        // and is measured beside it so the difference is a number rather than an assurance.
        int[] budgets = { 4, 10, 50, 200, 1_000, 2_000, 8_000 };
        SeriesPoint[] source = Signal(SamplesPerSecond);

        int minMaxKept = 0;
        int lttbKept = 0;

        foreach (int budget in budgets)
        {
            var destination = new SeriesPoint[budget];

            int written = MinMaxDownsampler.Reduce(source, budget, destination);
            bool minMaxHasSpike = destination.Take(written).Any(p => p.Value == 9_999.0);
            if (minMaxHasSpike) minMaxKept++;

            written = LttbDownsampler.Reduce(source, budget, destination);
            bool lttbHasSpike = destination.Take(written).Any(p => p.Value == 9_999.0);
            if (lttbHasSpike) lttbKept++;

            _output.WriteLine($"budget={budget,6:N0}  minmax kept spike={minMaxHasSpike,-5}  lttb kept spike={lttbHasSpike}");
        }

        minMaxKept.Should().Be(budgets.Length,
            "a single-sample excursion in a million samples survives min/max at every ratio tested");
        _output.WriteLine($"lone spike: minmax kept it {minMaxKept}/{budgets.Length}, lttb kept it {lttbKept}/{budgets.Length}");
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Benchmark_TwinExcursionInOneBucketIsWhereLttbFails()
    {
        // A lone outlier flatters LTTB: it has the largest triangle in its bucket, so it survives.
        // The honest test is a trough and a peak close enough to share a bucket. LTTB emits one
        // point per bucket, so one of the two must be discarded — and which one is discarded is
        // not something the reader of the chart can know.
        SeriesPoint[] source = Signal(SamplesPerSecond, spikeFraction: 0.37, spikeValue: 9_999.0);
        int trough = (int)(SamplesPerSecond * 0.37) + 3;
        source[trough] = new SeriesPoint(source[trough].TimestampSec, -9_999.0);

        int[] budgets = { 200, 1_000, 2_000 };
        int minMaxKeptBoth = 0;
        int lttbKeptBoth = 0;

        foreach (int budget in budgets)
        {
            var destination = new SeriesPoint[budget];

            int written = MinMaxDownsampler.Reduce(source, budget, destination);
            bool minMaxBoth = HasBoth(destination.Take(written));
            if (minMaxBoth) minMaxKeptBoth++;

            written = LttbDownsampler.Reduce(source, budget, destination);
            bool lttbBoth = HasBoth(destination.Take(written));
            if (lttbBoth) lttbKeptBoth++;

            _output.WriteLine($"budget={budget,6:N0}  minmax kept both={minMaxBoth,-5}  lttb kept both={lttbBoth}");
        }

        _output.WriteLine($"twin excursion: minmax {minMaxKeptBoth}/{budgets.Length}, lttb {lttbKeptBoth}/{budgets.Length}");

        minMaxKeptBoth.Should().Be(budgets.Length,
            "both extremes of a bucket are emitted, so a paired trough and peak both survive");
        lttbKeptBoth.Should().Be(0,
            "one point per bucket cannot carry two excursions; this is what preservesExtremes:false means");
    }

    private static bool HasBoth(IEnumerable<SeriesPoint> points)
    {
        double[] values = points.Select(p => p.Value).ToArray();
        return values.Contains(9_999.0) && values.Contains(-9_999.0);
    }
}
