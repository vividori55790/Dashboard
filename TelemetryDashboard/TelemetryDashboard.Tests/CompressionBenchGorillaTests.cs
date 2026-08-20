using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;
using Xunit.Abstractions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Round-trip proofs for the Gorilla block codec, and the compression ratio it actually achieves on
/// telemetry-shaped data rather than the ratio the paper reports on Facebook's.
/// </summary>
public sealed class CompressionBenchGorillaTests
{
    private readonly ITestOutputHelper _output;

    public CompressionBenchGorillaTests(ITestOutputHelper output) => _output = output;

    private static long[] Regular(int count, long startTicks, long stepTicks) =>
        Enumerable.Range(0, count).Select(i => startTicks + i * stepTicks).ToArray();

    private static long[] Zeros(int count) => new long[count];

    /// <summary>Bit patterns, not values: equality by pattern is what the round trip must preserve.</summary>
    private static void ShouldBeBitIdentical(double[] actual, double[] expected)
    {
        actual.Length.Should().Be(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            BitConverter.DoubleToInt64Bits(actual[i]).Should().Be(
                BitConverter.DoubleToInt64Bits(expected[i]),
                $"sample {i} must survive as the exact bit pattern that was written");
        }
    }

    [Fact]
    public void SpecialValuesSurviveBitExact()
    {
        double[] values =
        {
            double.NaN,
            BitConverter.Int64BitsToDouble(0x7FF8_0000_DEAD_BEEF), // NaN carrying a payload
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFF0_0000_0000_0001)), // signalling NaN pattern
            double.PositiveInfinity,
            double.NegativeInfinity,
            -0.0,
            0.0,
            double.Epsilon,
            -double.Epsilon,
            double.MaxValue,
            double.MinValue,
            5e-324,
            1.0 / 3.0
        };

        byte[] block = GorillaBlockCodec.Encode(
            Regular(values.Length, 638_000_000_000_000_000L, TimeSpan.TicksPerSecond), values, Zeros(values.Length));
        (long[] ticks, double[] decoded, long[] flags) = GorillaBlockCodec.Decode(block);

        ShouldBeBitIdentical(decoded, values);
        ticks.Should().Equal(Regular(values.Length, 638_000_000_000_000_000L, TimeSpan.TicksPerSecond));
        flags.Should().OnlyContain(f => f == 0);

        double.IsNaN(decoded[0]).Should().BeTrue("a NaN must come back as a NaN, not as zero");
        BitConverter.DoubleToInt64Bits(decoded[5]).Should().Be(
            BitConverter.DoubleToInt64Bits(-0.0), "negative zero is a distinct bit pattern from positive zero");
    }

    [Fact]
    public void NegativeZeroAndPositiveZeroStayDistinct()
    {
        double[] values = { 0.0, -0.0, 0.0, -0.0 };
        (_, double[] decoded, _) = GorillaBlockCodec.Decode(
            GorillaBlockCodec.Encode(Regular(4, 1_000, 10), values, Zeros(4)));

        ShouldBeBitIdentical(decoded, values);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(1_000)]
    public void AnyBlockLengthRoundTrips(int count)
    {
        var random = new Random(count);
        double[] values = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 500).ToArray();
        long[] ticks = Regular(count, 638_100_000_000_000_000L, TimeSpan.TicksPerMillisecond);

        (long[] decodedTicks, double[] decoded, _) = GorillaBlockCodec.Decode(
            GorillaBlockCodec.Encode(ticks, values, Zeros(count)));

        decodedTicks.Should().Equal(ticks);
        ShouldBeBitIdentical(decoded, values);
    }

    [Fact]
    public void IrregularAndBackwardTimestampsRoundTrip()
    {
        long[] ticks = { 500, 400, 900, 901, 2_000_000_000, 3, long.MaxValue / 2 };
        double[] values = Enumerable.Range(0, ticks.Length).Select(i => (double)i).ToArray();

        (long[] decoded, _, _) = GorillaBlockCodec.Decode(
            GorillaBlockCodec.Encode(ticks, values, Zeros(ticks.Length)));

        decoded.Should().Equal(ticks);
    }

    [Fact]
    public void FlagsSurviveAndCostNothingWhenUnset()
    {
        long[] ticks = Regular(500, 638_200_000_000_000_000L, TimeSpan.TicksPerSecond);
        double[] values = Enumerable.Range(0, 500).Select(i => 20.0 + i * 0.001).ToArray();

        long[] noFlags = Zeros(500);
        long[] someFlags = Zeros(500);
        someFlags[100] = (long)PacketFlags.ChecksumFailed;
        someFlags[101] = (long)(PacketFlags.ChecksumFailed | PacketFlags.AlarmExceeded);

        byte[] withoutFlags = GorillaBlockCodec.Encode(ticks, values, noFlags);
        byte[] withFlags = GorillaBlockCodec.Encode(ticks, values, someFlags);

        GorillaBlockCodec.Decode(withoutFlags).Flags.Should().Equal(noFlags);
        GorillaBlockCodec.Decode(withFlags).Flags.Should().Equal(someFlags);

        _output.WriteLine(
            $"flags: all-zero block {withoutFlags.Length} B, two flagged samples {withFlags.Length} B " +
            $"(+{withFlags.Length - withoutFlags.Length} B over 500 samples)");
    }

    [Fact]
    public void ACorruptedOrForeignBlobIsRejected()
    {
        byte[] block = GorillaBlockCodec.Encode(Regular(50, 1_000, 10),
            Enumerable.Range(0, 50).Select(i => (double)i).ToArray(), Zeros(50));

        Action foreign = () => GorillaBlockCodec.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 });
        foreign.Should().Throw<InvalidDataException>();

        byte[] newerVersion = (byte[])block.Clone();
        newerVersion[1] = 99;
        Action newer = () => GorillaBlockCodec.Decode(newerVersion);
        newer.Should().Throw<InvalidDataException>();

        Action truncated = () => GorillaBlockCodec.Decode(block[..(block.Length / 2)]);
        truncated.Should().Throw<InvalidDataException>();
    }

    /// <summary>Slowly varying telemetry: a walk in the low bits of a steady reading.</summary>
    private static double[] Walk(int count, double start, double step, int seed, int quantisationDigits)
    {
        var random = new Random(seed);
        var values = new double[count];
        double current = start;
        for (int i = 0; i < count; i++)
        {
            current += (random.NextDouble() - 0.5) * step;
            values[i] = quantisationDigits >= 0 ? Math.Round(current, quantisationDigits) : current;
        }

        return values;
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void MeasuredCompressionRatioOnRealisticTelemetry()
    {
        const long start = 638_300_000_000_000_000L;
        var random = new Random(99);

        (string Name, Func<int, double[]> Values, Func<int, long[]> Ticks)[] profiles =
        {
            ("constant 400.0 V, exact 1 Hz",
                n => Enumerable.Repeat(400.0, n).ToArray(),
                n => Regular(n, start, TimeSpan.TicksPerSecond)),
            ("walk 0.01-quantised, exact 1 Hz",
                n => Walk(n, 23.5, 0.05, 1, 2),
                n => Regular(n, start, TimeSpan.TicksPerSecond)),
            ("walk full precision, exact 1 Hz",
                n => Walk(n, 400.0, 0.2, 2, -1),
                n => Regular(n, start, TimeSpan.TicksPerSecond)),
            ("walk full precision, +-2 ms jitter",
                n => Walk(n, 400.0, 0.2, 3, -1),
                n => Regular(n, start, TimeSpan.TicksPerSecond)
                      .Select(t => t + (long)((random.NextDouble() - 0.5) * 4 * TimeSpan.TicksPerMillisecond))
                      .ToArray()),
            ("white noise, exact 1 kHz",
                n => Enumerable.Range(0, n).Select(_ => random.NextDouble() * 1_000).ToArray(),
                n => Regular(n, start, TimeSpan.TicksPerMillisecond))
        };

        _output.WriteLine("profile                                block   bytes/sample   ratio vs 16 B   ratio vs 102.3 B row");
        foreach ((string name, Func<int, double[]> values, Func<int, long[]> ticks) in profiles)
        {
            foreach (int blockSize in new[] { 1, 10, 60, 512, 4_096 })
            {
                double[] sample = values(blockSize);
                long[] stamps = ticks(blockSize);
                byte[] block = GorillaBlockCodec.Encode(stamps, sample, Zeros(blockSize));

                ShouldBeBitIdentical(GorillaBlockCodec.Decode(block).Values, sample);

                double perSample = block.Length / (double)blockSize;
                _output.WriteLine(
                    $"{name,-38}{blockSize,5}   {perSample,10:F2}   {16.0 / perSample,13:F2}x   {102.3 / perSample,18:F1}x");
            }
        }
    }
}
