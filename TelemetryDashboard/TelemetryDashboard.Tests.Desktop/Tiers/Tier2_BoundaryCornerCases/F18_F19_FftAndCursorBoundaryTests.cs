using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F18 and F19: FFT spectrum and delta-cursor boundary cases.</summary>
/// <remarks>
/// Both services are pure arithmetic, yet both are declared under <c>UI/ViewModels</c>, so a
/// portable assembly cannot reference them. That placement is worth revisiting — an FFT and a
/// cursor delta are domain calculations, not presentation — but the tests must sit where the code
/// sits, and saying so here is more useful than quietly leaving them Windows-only.
/// </remarks>
public class F18_F19_FftAndCursorBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F18_Boundary_NonPowerOfTwoSampleCount_PadsZeroesToNextPowerOfTwo()
    {
        var fft = new FftAnalyzerService();
        double[] input100 = new double[100]; // Not power of 2

        double[] spectrum = fft.ComputeFft(input100);
        // Next power of 2 is 128
        spectrum.Length.Should().Be(64); // Half spectrum output
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F18_Boundary_AllZeroSignal_OutputsZeroSpectrum()
    {
        var fft = new FftAnalyzerService();
        double[] inputZeros = new double[256];

        double[] spectrum = fft.ComputeFft(inputZeros);
        spectrum.All(val => val == 0.0).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F18_Boundary_SingleSampleInput_ReturnsEmptyOrSingleBin()
    {
        var fft = new FftAnalyzerService();
        double[] inputSingle = new double[] { 5.0 };

        double[] spectrum = fft.ComputeFft(inputSingle);
        spectrum.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F18_Boundary_ExtremeFrequencyInput_ClampsToNyquistFrequency()
    {
        var fft = new FftAnalyzerService();
        double samplingRate = 1000.0; // 1 kHz
        double nyquist = samplingRate / 2.0;

        double maxFreq = fft.GetMaxFrequency(samplingRate, 1024);
        maxFreq.Should().Be(nyquist);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F18_Boundary_DcOffsetOnly_IdentifiesZeroHzPeak()
    {
        var fft = new FftAnalyzerService();
        double[] dcSignal = Enumerable.Repeat(10.0, 256).ToArray();

        double[] spectrum = fft.ComputeFft(dcSignal);
        spectrum[0].Should().BeGreaterThan(0);
        spectrum.Skip(1).All(v => v < spectrum[0]).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F19_Boundary_Cursor1EqualsCursor2_DeltaValuesAreZero()
    {
        var cursor = new DeltaCursorService();
        cursor.SetCursor1(10.0, 50.0);
        cursor.SetCursor2(10.0, 50.0);

        cursor.DeltaTime.Should().Be(0.0);
        cursor.DeltaValue.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F19_Boundary_CursorOutOfBounds_ClampsToVisibleDataRange()
    {
        var cursor = new DeltaCursorService();
        cursor.SetDataBounds(0.0, 100.0);
        cursor.SetCursor1(-50.0, 0.0);

        cursor.Cursor1Time.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F19_Boundary_NegativeTimeDelta_DisplaysAbsoluteOrSignedDelta()
    {
        var cursor = new DeltaCursorService();
        cursor.SetCursor1(20.0, 100.0);
        cursor.SetCursor2(10.0, 50.0);

        cursor.DeltaTime.Should().Be(-10.0);
        cursor.AbsoluteDeltaTime.Should().Be(10.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F19_Boundary_NoDataUnderCursor_DisplaysNoDataHud()
    {
        var cursor = new DeltaCursorService();
        cursor.ClearData();
        cursor.SetCursor1(5.0, 10.0);

        cursor.HasValidMeasurement.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F19_Boundary_RapidCursorDrag_MaintainsRenderPerformance()
    {
        var cursor = new DeltaCursorService();
        for (int i = 0; i < 100; i++)
        {
            cursor.SetCursor1(i * 0.1, i * 2.0);
        }
        cursor.Cursor1Time.Should().BeApproximately(9.9, 0.01);
    }
}
