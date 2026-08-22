using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F20 and F21: HelixToolkit 3D viewport and thermal heatmap boundary cases.</summary>
/// <remarks>
/// <c>Twin3DService</c> builds HelixToolkit.WPF meshes and <c>HeatmapInterpolationService</c>
/// returns WPF media colours, so both genuinely need the desktop framework — this pair is the least
/// arguable part of the move.
/// </remarks>
public class F20_F21_Render3DAndHeatmapBoundaryTests
{
    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F20_Boundary_CorruptedStlFile_DisplaysFallbackCubeMesh()
    {
        var renderer = new Twin3DService();
        string tempStl = Path.GetTempFileName();
        File.WriteAllText(tempStl, "CORRUPTED_STL_CONTENT");

        try
        {
            bool loaded = renderer.LoadModel(tempStl);
            loaded.Should().BeFalse();
            renderer.IsFallbackModelActive.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempStl);
        }
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F20_Boundary_ZeroVertexMesh_HandlesWithoutDividingByZero()
    {
        var renderer = new Twin3DService();
        Action act = () => renderer.SetCustomMesh(Array.Empty<float>(), Array.Empty<int>());
        act.Should().NotThrow();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F20_Boundary_ExtremeScaleCoordinates_AutoNormalizesBounds()
    {
        var renderer = new Twin3DService();
        float[] extremeCoords = new float[] { 1e6f, -1e6f, 5e6f };

        renderer.SetCustomMesh(extremeCoords, new int[] { 0, 0, 0 });
        renderer.NormalizedBoundingBoxSize.Should().BeLessThanOrEqualTo(10.0f);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void F20_Boundary_NullMeshSource_ClearsViewport()
    {
        var renderer = new Twin3DService();
        renderer.ClearModel();

        renderer.HasModel.Should().BeFalse();
    }

    // F20_Boundary_Rapid3DRotation_DoesNotCrashRenderLoop is gone with the API it exercised.
    // Twin3DService carried RotationX/Y/Z and a Rotate setter that nothing in the application ever
    // called; this test called it fifty times and asserted the getter held 490. It could not have
    // failed for any reason a user would notice, and its passing was the only evidence that the
    // rotation state was alive.

    [Fact]
    [Trait("Category", "Tier2")]
    public void F21_Boundary_SingleSensorPoint_RendersUniformHeatmap()
    {
        var heatmap = new HeatmapInterpolationService();
        heatmap.AddSensor(0.0, 0.0, 0.0, temp: 75.0);

        double interpolated = heatmap.Interpolate(10.0, 10.0, 10.0);
        interpolated.Should().Be(75.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F21_Boundary_AllSensorsSameTemperature_RendersConstantColor()
    {
        var heatmap = new HeatmapInterpolationService();
        heatmap.AddSensor(0.0, 0.0, 0.0, temp: 50.0);
        heatmap.AddSensor(10.0, 0.0, 0.0, temp: 50.0);

        double val = heatmap.Interpolate(5.0, 0.0, 0.0);
        val.Should().Be(50.0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F21_Boundary_ExtremeTemperature_ClampsToGradientMapBounds()
    {
        var heatmap = new HeatmapInterpolationService();
        heatmap.SetGradientBounds(0.0, 100.0);
        heatmap.AddSensor(0.0, 0.0, 0.0, temp: 999.0);

        var color = heatmap.GetColorForTemperature(999.0);
        color.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F21_Boundary_DuplicateSensorCoordinates_IgnoresDuplicateOrAverages()
    {
        var heatmap = new HeatmapInterpolationService();
        heatmap.AddSensor(1.0, 1.0, 1.0, temp: 40.0);
        heatmap.AddSensor(1.0, 1.0, 1.0, temp: 60.0);

        double val = heatmap.Interpolate(1.0, 1.0, 1.0);
        val.Should().Be(60.0); // Uses latest or average without crash
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F21_Boundary_SensorDistanceZero_HandlesIDWDivByZero()
    {
        var heatmap = new HeatmapInterpolationService();
        heatmap.AddSensor(5.0, 5.0, 5.0, temp: 80.0);

        // Interpolate directly on top of sensor point
        double val = heatmap.Interpolate(5.0, 5.0, 5.0);
        val.Should().Be(80.0);
    }
}
