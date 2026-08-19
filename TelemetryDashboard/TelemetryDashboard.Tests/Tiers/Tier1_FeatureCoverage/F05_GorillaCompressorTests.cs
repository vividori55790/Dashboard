using System;
using FluentAssertions;
using TelemetryDashboard.Core.Services;
using Xunit;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F05_GorillaCompressorTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_CompressDoubles_RoundTripLossless()
    {
        var compressor = new GorillaCompressor();
        double[] samples = new double[] { 24.5, 24.5, 24.52, 24.55, 24.55, 24.55, 25.0, 24.8 };

        byte[] compressed = compressor.CompressDoubles(samples);
        compressed.Should().NotBeNullOrEmpty();

        double[] decompressed = compressor.DecompressDoubles(compressed);
        decompressed.Should().Equal(samples);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_CompressDoubles_ConstantSeries_HighCompressionRatio()
    {
        var compressor = new GorillaCompressor();
        double[] samples = new double[1000];
        Array.Fill(samples, 3.1415926535);

        byte[] compressed = compressor.CompressDoubles(samples);
        int uncompressedSizeBytes = samples.Length * sizeof(double); // 8000 bytes

        // Constant float series should achieve > 90% compression ratio in Gorilla
        compressed.Length.Should().BeLessThan(uncompressedSizeBytes / 5);

        double[] decompressed = compressor.DecompressDoubles(compressed);
        decompressed.Should().Equal(samples);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_CompressTimeStamps_DeltaOfDelta_RoundTripLossless()
    {
        var compressor = new GorillaCompressor();
        long baseTs = 1700000000;
        long[] timestamps = new long[] { baseTs, baseTs + 10, baseTs + 20, baseTs + 30, baseTs + 40, baseTs + 50 };

        byte[] compressed = compressor.CompressTimeStamps(timestamps);
        compressed.Should().NotBeNullOrEmpty();

        long[] decompressed = compressor.DecompressTimeStamps(compressed);
        decompressed.Should().Equal(timestamps);
    }
}
