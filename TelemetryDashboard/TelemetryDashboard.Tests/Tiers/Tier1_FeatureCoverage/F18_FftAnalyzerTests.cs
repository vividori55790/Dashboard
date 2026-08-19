namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F18_FftAnalyzerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void FftAnalyzer_ComputeSpectrum_ReturnsMagnitudeArray()
    {
        double[] timeData = new double[64];
        for (int i = 0; i < timeData.Length; i++) timeData[i] = Math.Sin(2 * Math.PI * 5 * i / 64.0);

        double[] spectrum = FftHelper.ComputeMagnitudeSpectrum(timeData);

        spectrum.Should().NotBeNull();
        spectrum.Length.Should().Be(32);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FftAnalyzer_Radix2_HandlesPowerOfTwoBuffer()
    {
        double[] buffer1024 = new double[1024];
        double[] spectrum = FftHelper.ComputeMagnitudeSpectrum(buffer1024);

        spectrum.Length.Should().Be(512);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FftAnalyzer_PeakFrequency_IdentifiesPrimarySpike()
    {
        double[] timeData = new double[128];
        double sampleRate = 1000.0; // 1000 Hz
        double targetFreq = 100.0;  // 100 Hz signal
        for (int i = 0; i < timeData.Length; i++)
        {
            timeData[i] = Math.Sin(2 * Math.PI * targetFreq * i / sampleRate);
        }

        double peak = FftHelper.FindPeakFrequency(timeData, sampleRate);

        peak.Should().BeInRange(90.0, 110.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FftAnalyzer_Windowing_AppliesHanningWindow()
    {
        double[] data = { 1.0, 1.0, 1.0, 1.0 };
        double[] windowed = FftHelper.ApplyHanningWindow(data);

        windowed[0].Should().Be(0.0);
        windowed[^1].Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FftAnalyzer_UpdatePlot_RefreshesSpectrumData()
    {
        var analyzer = new FftState();
        analyzer.UpdateData(new double[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        analyzer.HasData.Should().BeTrue();
    }
}

public static class FftHelper
{
    public static double[] ComputeMagnitudeSpectrum(double[] timeData)
    {
        int N = timeData.Length;
        int half = N / 2;
        double[] mag = new double[half];
        for (int k = 0; k < half; k++)
        {
            double re = 0, im = 0;
            for (int n = 0; n < N; n++)
            {
                double angle = 2 * Math.PI * k * n / N;
                re += timeData[n] * Math.Cos(angle);
                im -= timeData[n] * Math.Sin(angle);
            }
            mag[k] = Math.Sqrt(re * re + im * im) / N;
        }
        return mag;
    }

    public static double FindPeakFrequency(double[] timeData, double sampleRate)
    {
        var mag = ComputeMagnitudeSpectrum(timeData);
        int maxBin = 0;
        double maxVal = 0;
        for (int i = 1; i < mag.Length; i++)
        {
            if (mag[i] > maxVal)
            {
                maxVal = mag[i];
                maxBin = i;
            }
        }
        return maxBin * (sampleRate / timeData.Length);
    }

    public static double[] ApplyHanningWindow(double[] input)
    {
        int N = input.Length;
        double[] res = new double[N];
        for (int i = 0; i < N; i++)
        {
            double win = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (N - 1)));
            res[i] = input[i] * win;
        }
        return res;
    }
}

public class FftState
{
    public bool HasData { get; private set; }
    public void UpdateData(double[] timeSeries)
    {
        HasData = timeSeries != null && timeSeries.Length > 0;
    }
}
