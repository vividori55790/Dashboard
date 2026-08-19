namespace TelemetryDashboard.Tests.Tiers.Tier2_BoundaryCornerCases;

using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;

/// <summary>F23 and F24: signal generator and anomaly engine boundary cases.</summary>
/// <remarks>
/// These ten cases were the portable remainder of <c>F17_F24_VisualizationBoundaryTests</c>.
/// <c>SignalGeneratorService</c> and <c>AnomalyEngine</c> both live in <c>Core/Analytics</c>, so
/// moving the whole file to the desktop suite would have made the anomaly engine — the one
/// component whose correctness the product's honesty depends on — unverifiable on a Linux agent,
/// to keep six WPF regions company. The F17–F22 regions moved; these stayed.
/// </remarks>
public class F23_F24_SignalAndAnomalyBoundaryTests
{
    #region F23: Interactive Signal Generator (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F23_Boundary_ZeroFrequency_OutputsStaticDcSignal()
    {
        var sigGen = new SignalGeneratorService();
        sigGen.Configure(WaveformType.Sine, frequencyHz: 0.0, amplitude: 5.0);

        double sample = sigGen.GetNextSample(0.1);
        sample.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F23_Boundary_NegativeAmplitude_ClampsToZeroOrInverts()
    {
        var sigGen = new SignalGeneratorService();
        sigGen.Configure(WaveformType.Sine, frequencyHz: 10.0, amplitude: -5.0);

        sigGen.Amplitude.Should().Be(5.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F23_Boundary_UnsupportedWaveformType_DefaultsToSine()
    {
        var sigGen = new SignalGeneratorService();
        sigGen.Configure((WaveformType)999, frequencyHz: 10.0, amplitude: 1.0);

        sigGen.CurrentWaveform.Should().Be(WaveformType.Sine);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F23_Boundary_HighFrequencyExceedingSampleRate_WarnsAliasing()
    {
        var sigGen = new SignalGeneratorService();
        bool aliasing = sigGen.CheckAliasingWarning(freqHz: 10000.0, sampleRateHz: 1000.0);
        aliasing.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F23_Boundary_StopGeneratorWhenNotActive_NoOp()
    {
        var sigGen = new SignalGeneratorService();
        Action act = () => sigGen.Stop();
        act.Should().NotThrow();
    }

    #endregion

    #region F24: AI / Statistical Anomaly Engine (Boundary Tests)

    [Fact]
    [Trait("Category", "Tier2")]
    public void F24_Boundary_InsufficientSampleCount_ReturnsNoAnomalyResult()
    {
        var anomalyEngine = new AnomalyEngine();
        double[] fewSamples = new double[] { 10.0, 10.5 };

        var result = anomalyEngine.Evaluate(fewSamples);
        result.IsAnomaly.Should().BeFalse();
        result.Reason.Should().Contain("Insufficient");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F24_Boundary_ConstantValueSeries_ZeroVariance_HandlesZScoreDivZero()
    {
        var anomalyEngine = new AnomalyEngine();
        double[] constantSeries = Enumerable.Repeat(50.0, 100).ToArray();

        var result = anomalyEngine.Evaluate(constantSeries);
        result.IsAnomaly.Should().BeFalse();
        result.ZScore.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F24_Boundary_EwmaAlphaZeroOrOne_CalculatesExtremeWeightings()
    {
        var anomalyEngine = new AnomalyEngine();
        anomalyEngine.SetEwmaAlpha(0.0); // Ignore new data
        double ewma0 = anomalyEngine.UpdateEwma(100.0);

        anomalyEngine.SetEwmaAlpha(1.0); // Pure new data
        double ewma1 = anomalyEngine.UpdateEwma(100.0);

        ewma1.Should().Be(100.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F24_Boundary_ExtremeOutlierValue_TriggersImmediateCriticalWarning()
    {
        var anomalyEngine = new AnomalyEngine();
        double[] normalSeries = Enumerable.Repeat(20.0, 50).Concat(new double[] { 9999.0 }).ToArray();

        var result = anomalyEngine.Evaluate(normalSeries);
        result.IsAnomaly.Should().BeTrue();
        result.ZScore.Should().BeGreaterThan(3.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F24_Boundary_NaNValuesInInputSeries_IgnoredInStatistics()
    {
        var anomalyEngine = new AnomalyEngine();
        double[] dataWithNaN = new double[] { 10.0, double.NaN, 20.0, 30.0, double.NaN };

        var result = anomalyEngine.Evaluate(dataWithNaN);
        result.ProcessedSampleCount.Should().Be(3);
    }

    #endregion
}
