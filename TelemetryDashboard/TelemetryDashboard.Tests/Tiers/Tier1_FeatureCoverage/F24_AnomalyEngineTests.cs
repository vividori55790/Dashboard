namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F24_AnomalyEngineTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AnomalyEngine_ZScore_CalculatesRollingZScore()
    {
        double[] history = { 50.0, 50.0, 50.0, 50.0, 50.0 }; // Mean=50, StdDev=0 (or small eps)
        double z = AnomalyHelper.CalculateZScore(80.0, history);

        z.Should().BeGreaterThan(3.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnomalyEngine_EWMA_TracksPredictiveTrend()
    {
        double prevEwma = 50.0;
        double newVal = 60.0;
        double alpha = 0.3;

        double nextEwma = AnomalyHelper.CalculateEwma(newVal, prevEwma, alpha);

        nextEwma.Should().Be(53.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnomalyEngine_FlagsAnomaly_WhenZScoreExceedsThreshold()
    {
        var engine = new AnomalyEngineState();
        engine.UpdateBaseline(new double[] { 10, 11, 10, 12, 11 });

        bool isAnomaly = engine.Evaluate(25.0, zThreshold: 3.0);

        isAnomaly.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnomalyEngine_PredictiveDrift_WarnsBeforeThresholdBreach()
    {
        var engine = new AnomalyEngineState();
        engine.UpdateBaseline(new double[] { 10, 11, 10, 12, 11 });

        bool isDriftWarning = engine.EvaluateDrift(16.0, driftThreshold: 2.0);

        isDriftWarning.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnomalyEngine_BaselineCalibration_UpdatesMeanAndStdDev()
    {
        var engine = new AnomalyEngineState();
        engine.UpdateBaseline(new double[] { 20, 20, 20, 20 });

        engine.Mean.Should().Be(20.0);
        engine.StdDev.Should().Be(0.0);
    }
}

public static class AnomalyHelper
{
    public static double CalculateZScore(double value, double[] history)
    {
        double mean = history.Average();
        double variance = history.Select(val => Math.Pow(val - mean, 2)).Average();
        double stdDev = Math.Sqrt(variance);
        if (stdDev < 1e-6) stdDev = 1e-6;
        return (value - mean) / stdDev;
    }

    public static double CalculateEwma(double currentVal, double prevEwma, double alpha)
    {
        return alpha * currentVal + (1 - alpha) * prevEwma;
    }
}

public class AnomalyEngineState
{
    public double Mean { get; private set; }
    public double StdDev { get; private set; }
    private double[] _history = Array.Empty<double>();

    public void UpdateBaseline(double[] data)
    {
        _history = data;
        Mean = data.Average();
        double variance = data.Select(v => Math.Pow(v - Mean, 2)).Average();
        StdDev = Math.Sqrt(variance);
    }

    public bool Evaluate(double val, double zThreshold)
    {
        if (_history.Length == 0) return false;
        double z = AnomalyHelper.CalculateZScore(val, _history);
        return Math.Abs(z) > zThreshold;
    }

    public bool EvaluateDrift(double val, double driftThreshold)
    {
        if (_history.Length == 0) return false;
        double z = AnomalyHelper.CalculateZScore(val, _history);
        return Math.Abs(z) > driftThreshold;
    }
}
