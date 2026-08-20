using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The reduction contract, checked against expectations worked out by hand.
/// </summary>
/// <remarks>
/// A chart is the only thing most operators ever see, so a reduction that silently deletes an
/// excursion is indistinguishable from a plant that never had one. These tests exist to pin the
/// difference between the two reductions: min/max cannot lose a spike, LTTB can, and the API has
/// to say which one it used.
/// </remarks>
public class DownsampleTests
{
    private static SeriesPoint[] Series(params double[] values)
    {
        var points = new SeriesPoint[values.Length];
        for (int i = 0; i < values.Length; i++) points[i] = new SeriesPoint(i, values[i]);
        return points;
    }

    // -----------------------------------------------------------------
    // Min/max per bucket
    // -----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_EmitsTheExtremesOfEachBucketAtTheirOwnTimestamps()
    {
        // t = 0..7, eight samples, budget 4 => 2 buckets, width (7-0)/2 = 3.5s.
        // Bucket 0 covers t in [0, 3.5): values 1,5,2,9 -> min 1 at t=0, max 9 at t=3.
        // Bucket 1 covers t in [3.5, 7]: values 3,0,4,6 -> min 0 at t=5, max 6 at t=7.
        SeriesPoint[] source = Series(1, 5, 2, 9, 3, 0, 4, 6);
        var destination = new SeriesPoint[4];

        int written = MinMaxDownsampler.Reduce(source, 4, destination);

        written.Should().Be(4);
        destination.Should().Equal(
            new SeriesPoint(0, 1),
            new SeriesPoint(3, 9),
            new SeriesPoint(5, 0),
            new SeriesPoint(7, 6));
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_EmitsExtremesInChronologicalOrder()
    {
        // Bucket 0's maximum precedes its minimum. Emitting min-then-max would draw a line that
        // runs backwards in time.
        SeriesPoint[] source = Series(9, 1, 2, 3, 4, 5, 6, 7);
        var destination = new SeriesPoint[4];

        MinMaxDownsampler.Reduce(source, 4, destination);

        destination[0].TimestampSec.Should().BeLessThan(destination[1].TimestampSec);
        destination[0].Should().Be(new SeriesPoint(0, 9));
        destination[1].Should().Be(new SeriesPoint(1, 1));
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_EmitsAFlatBucketOnce()
    {
        // When one sample is both the minimum and the maximum, emitting it twice would inflate
        // the point count with a value the data produced once.
        SeriesPoint[] source = Series(5, 5, 5, 5, 5, 5, 5, 5);
        var destination = new SeriesPoint[4];

        int written = MinMaxDownsampler.Reduce(source, 4, destination);

        written.Should().Be(2, "each bucket is flat, so each contributes a single real sample");
    }

    [Theory]
    [Trait("Category", "Downsample")]
    [InlineData(1_000, 100)]
    [InlineData(10_000, 2_000)]
    [InlineData(1_000_000, 2_000)]
    [InlineData(1_000_000, 7)]
    public void MinMax_NeverExceedsTheBudgetAndAlwaysKeepsBothGlobalExtremes(int count, int budget)
    {
        var source = new SeriesPoint[count];
        for (int i = 0; i < count; i++)
        {
            source[i] = new SeriesPoint(i * 0.001, Math.Sin(i * 0.01) * 10.0);
        }

        // A one-sample excursion, the thing an operator is actually looking for.
        source[count / 3] = new SeriesPoint(source[count / 3].TimestampSec, 9_999.0);
        source[(count * 2) / 3] = new SeriesPoint(source[(count * 2) / 3].TimestampSec, -9_999.0);

        var destination = new SeriesPoint[budget];
        int written = MinMaxDownsampler.Reduce(source, budget, destination);

        written.Should().BeLessThanOrEqualTo(budget);
        destination.Take(written).Select(p => p.Value).Should().Contain(9_999.0);
        destination.Take(written).Select(p => p.Value).Should().Contain(-9_999.0);
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_ReturnsTheWindowUntouchedWhenItAlreadyFits()
    {
        SeriesPoint[] source = Series(1, 2, 3);
        var destination = new SeriesPoint[8];

        int written = MinMaxDownsampler.Reduce(source, 8, destination);

        written.Should().Be(3);
        destination.Take(3).Should().Equal(source);
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_RefusesABudgetTooSmallToHoldOneBucket()
    {
        Action reduce = () => MinMaxDownsampler.Reduce(Series(1, 2, 3), 1, new SeriesPoint[1]);

        reduce.Should().Throw<ArgumentOutOfRangeException>(
            "a budget of one cannot carry both extremes, and silently dropping one would hide a spike");
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void MinMax_KeepsExtremesWhenEverySampleShareOneTimestamp()
    {
        // A burst written with one clock reading has no time span to bucket. Losing the extremes
        // to that degenerate case would be the same failure by another route.
        var source = new SeriesPoint[100];
        for (int i = 0; i < source.Length; i++) source[i] = new SeriesPoint(42.0, i);

        var destination = new SeriesPoint[10];
        int written = MinMaxDownsampler.Reduce(source, 10, destination);

        destination.Take(written).Select(p => p.Value).Should().Contain(0.0).And.Contain(99.0);
    }

    // -----------------------------------------------------------------
    // Largest-Triangle-Three-Buckets
    // -----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Downsample")]
    public void Lttb_SelectsTheHandComputedPoints()
    {
        // n = 10, budget 4 => bucketSize (10-2)/(4-2) = 4.
        // First and last are always kept. Bucket 0 scores candidates t=1..4 against the centroid
        // of t=5..8 (6.5, 6.5): area = 6.5*|t-v|, maximal at t=3 where v = 100.
        // Bucket 1 scores t=5..8 against the single next point (9, 9) from the anchor (3, 100):
        // area = |873 - 6v - 91t|, maximal at t=5.
        SeriesPoint[] source = Series(0, 1, 2, 100, 4, 5, 6, 7, 8, 9);
        var destination = new SeriesPoint[4];

        int written = LttbDownsampler.Reduce(source, 4, destination);

        written.Should().Be(4);
        destination.Should().Equal(
            new SeriesPoint(0, 0),
            new SeriesPoint(3, 100),
            new SeriesPoint(5, 5),
            new SeriesPoint(9, 9));
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Lttb_EmitsOnlyRealSamplesNeverTheBucketAverageItComputes()
    {
        var source = new SeriesPoint[500];
        for (int i = 0; i < source.Length; i++) source[i] = new SeriesPoint(i, i % 7);

        var destination = new SeriesPoint[50];
        int written = LttbDownsampler.Reduce(source, 50, destination);

        // The averages the algorithm computes internally are not whole numbers here, so any
        // fabricated point would be visible as a value the source never contained.
        foreach (SeriesPoint point in destination.Take(written))
        {
            source.Should().Contain(point, "every emitted point must be a sample that was measured");
        }
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Lttb_DropsAnExcursionThatMinMaxKeeps()
    {
        // The same ten samples and the same budget through both reductions. Bucket 1 holds a
        // -500 trough and a +900 peak; LTTB may keep one point per bucket, so one of them must
        // go. This is the reason the API labels LTTB preservesExtremes: false.
        SeriesPoint[] source = Series(0, 1, 2, 3, 4, -500, 900, 7, 8, 9);

        var viaLttb = new SeriesPoint[4];
        int lttbWritten = LttbDownsampler.Reduce(source, 4, viaLttb);

        var viaMinMax = new SeriesPoint[4];
        int minMaxWritten = MinMaxDownsampler.Reduce(source, 4, viaMinMax);

        viaLttb.Take(lttbWritten).Should().Equal(
            new SeriesPoint(0, 0),
            new SeriesPoint(4, 4),
            new SeriesPoint(6, 900),
            new SeriesPoint(9, 9));
        viaLttb.Take(lttbWritten).Select(p => p.Value).Should().NotContain(-500.0,
            "LTTB keeps the point with the largest triangle area, not the extremes");

        viaMinMax.Take(minMaxWritten).Select(p => p.Value)
            .Should().Contain(-500.0).And.Contain(900.0,
                "min/max cannot lose either extreme of a bucket, whatever else is in it");
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Lttb_AlwaysKeepsTheFirstAndLastSample()
    {
        var source = new SeriesPoint[1_000];
        for (int i = 0; i < source.Length; i++) source[i] = new SeriesPoint(i * 0.01, Math.Cos(i * 0.05));

        var destination = new SeriesPoint[100];
        int written = LttbDownsampler.Reduce(source, 100, destination);

        written.Should().Be(100, "LTTB fills its budget exactly");
        destination[0].Should().Be(source[0]);
        destination[written - 1].Should().Be(source[^1]);
    }

    [Fact]
    [Trait("Category", "Downsample")]
    public void Lttb_RefusesABudgetBelowThree()
    {
        Action reduce = () => LttbDownsampler.Reduce(Series(1, 2, 3, 4), 2, new SeriesPoint[2]);

        reduce.Should().Throw<ArgumentOutOfRangeException>();
    }
}
