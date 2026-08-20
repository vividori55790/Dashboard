using Xunit.Abstractions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The cardinality sweep. These are measurement runs, not assertions about correctness: they exist
/// to produce numbers for the scale study of per-channel state.
/// </summary>
/// <remarks>
/// They are gated on <see cref="ScaleHarness.EnableVariable"/> because a million-channel run costs
/// minutes and gigabytes, and a shared test tree should not pay that on every build. When the
/// variable is unset each test reports that it did not measure anything, rather than passing
/// silently as though the measurement had been taken and had succeeded.
/// <para>
/// All tests live in one class so xunit runs them sequentially; two of these executing concurrently
/// would each be measuring the other's heap.
/// </para>
/// </remarks>
public class ScaleHarnessTests
{
    private readonly ITestOutputHelper _output;

    public ScaleHarnessTests(ITestOutputHelper output) => _output = output;

    private bool Skipped()
    {
        if (ScaleHarness.Enabled) return false;

        _output.WriteLine(
            $"NOT MEASURED. Set {ScaleHarness.EnableVariable}=1 to run this sweep. "
            + "No memory or throughput figure was produced by this run.");
        return true;
    }

    private void Report(ScaleRow row)
    {
        _output.WriteLine(row.ToReport());
        ScaleHarness.Record(row);

        row.Completed.Should().BeTrue(
            $"the sweep at {row.Cardinality:N0} channels must either produce figures or say why not: {row.Failure}");
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Engine_At_1000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureEngine(1_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Engine_At_10000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureEngine(10_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Engine_At_100000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureEngine(100_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Engine_At_1000000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureEngine(1_000_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Breaker_At_1000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureBreaker(1_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Breaker_At_10000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureBreaker(10_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Breaker_At_100000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureBreaker(100_000));
    }

    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Breaker_At_1000000()
    {
        if (Skipped()) return;
        Report(ScaleHarness.MeasureBreaker(1_000_000));
    }

    /// <summary>
    /// The breaker's second memory axis: packets held in the one-second rate window. Cardinality is
    /// held fixed and the packet count per channel is varied, so any difference is rate, not fan-out.
    /// </summary>
    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Breaker_RateAxis_At_10000_Channels()
    {
        if (Skipped()) return;

        ScaleRow one = ScaleHarness.MeasureBreaker(10_000, packetsPerChannel: 1);
        Report(one);

        ScaleRow twenty = ScaleHarness.MeasureBreaker(10_000, packetsPerChannel: 20);
        Report(twenty);

        _output.WriteLine(
            $"rate axis: {one.ManagedDelta:N0} bytes at 1 packet/channel vs {twenty.ManagedDelta:N0} bytes "
            + $"at 20 packets/channel; difference {twenty.ManagedDelta - one.ManagedDelta:N0} bytes over "
            + $"{19 * 10_000:N0} extra queued packets "
            + $"= {(double)(twenty.ManagedDelta - one.ManagedDelta) / (19 * 10_000):F1} bytes per queued packet");
    }

    /// <summary>
    /// Marginal cost of one channel, measured in a single process by differencing two cardinalities.
    /// Fixed overhead is identical in both terms and cancels, which is why this number is trusted
    /// over the bytes/channel column of any single row.
    /// </summary>
    [Fact]
    [Trait("Category", "ScaleHarness")]
    public void Marginal_BytesPerChannel()
    {
        if (Skipped()) return;

        ScaleRow engineSmall = ScaleHarness.MeasureEngine(100_000);
        Report(engineSmall);
        ScaleRow engineLarge = ScaleHarness.MeasureEngine(200_000);
        Report(engineLarge);

        _output.WriteLine(
            $"ENGINE marginal bytes/channel (200k minus 100k): "
            + $"{ScaleRow.MarginalBytesPerChannel(engineSmall, engineLarge):F1}");

        ScaleRow breakerSmall = ScaleHarness.MeasureBreaker(100_000);
        Report(breakerSmall);
        ScaleRow breakerLarge = ScaleHarness.MeasureBreaker(200_000);
        Report(breakerLarge);

        _output.WriteLine(
            $"BREAKER marginal bytes/channel (200k minus 100k): "
            + $"{ScaleRow.MarginalBytesPerChannel(breakerSmall, breakerLarge):F1}");
    }
}
