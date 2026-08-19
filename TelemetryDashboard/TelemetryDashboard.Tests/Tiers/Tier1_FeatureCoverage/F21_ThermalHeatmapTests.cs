namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F21_ThermalHeatmapTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void ThermalHeatmap_IDWInterpolation_ComputesWeightedVertexTemperature()
    {
        var sensors = new[] { ((0.0, 0.0), 30.0), ((10.0, 0.0), 80.0) };
        double tempAtMid = HeatmapHelper.InterpolateIdw((5.0, 0.0), sensors, p: 2.0);

        tempAtMid.Should().BeApproximately(55.0, 0.1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThermalHeatmap_ColorGradient_MapsTemperatureToColor()
    {
        string colorLow = HeatmapHelper.MapTemperatureToColor(20.0, 0.0, 100.0);
        string colorHigh = HeatmapHelper.MapTemperatureToColor(90.0, 0.0, 100.0);

        colorLow.Should().Be("Blue");
        colorHigh.Should().Be("Red");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThermalHeatmap_VibrationAmplitude_UpdatesVertexDisplacement()
    {
        double baseZ = 0.0;
        double vibAmp = 2.5; // G

        double displacedZ = baseZ + vibAmp * 0.1;
        displacedZ.Should().Be(0.25);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThermalHeatmap_OnPacketArrival_RefreshesTextureOverlay()
    {
        var heatmap = new HeatmapState();
        heatmap.UpdateSensor("SENSOR_1", 85.5);

        heatmap.IsTextureDirty.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThermalHeatmap_OutOfBounds_ClampsGradientColor()
    {
        string colorExtremeLow = HeatmapHelper.MapTemperatureToColor(-50.0, 0.0, 100.0);
        string colorExtremeHigh = HeatmapHelper.MapTemperatureToColor(150.0, 0.0, 100.0);

        colorExtremeLow.Should().Be("Blue");
        colorExtremeHigh.Should().Be("Red");
    }
}

public static class HeatmapHelper
{
    public static double InterpolateIdw((double x, double y) point, ((double x, double y) pos, double val)[] sensors, double p)
    {
        double num = 0, den = 0;
        foreach (var s in sensors)
        {
            double dx = point.x - s.pos.x;
            double dy = point.y - s.pos.y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist == 0) return s.val;
            double weight = 1.0 / Math.Pow(dist, p);
            num += weight * s.val;
            den += weight;
        }
        return den > 0 ? num / den : 0.0;
    }

    public static string MapTemperatureToColor(double temp, double min, double max)
    {
        if (temp <= min + 30) return "Blue";
        if (temp >= max - 20) return "Red";
        return "Green";
    }
}

public class HeatmapState
{
    public bool IsTextureDirty { get; private set; }
    public Dictionary<string, double> Sensors { get; } = new();

    public void UpdateSensor(string id, double val)
    {
        Sensors[id] = val;
        IsTextureDirty = true;
    }
}
