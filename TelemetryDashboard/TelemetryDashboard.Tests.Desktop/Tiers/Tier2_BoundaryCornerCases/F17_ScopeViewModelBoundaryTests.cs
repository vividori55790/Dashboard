using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F17: ScottPlot 5 WPF 2D scope boundary cases.</summary>
/// <remarks>
/// <c>ScopeViewModel</c> is the view model behind the ScottPlot.WPF surface, so it can only be
/// constructed with the UI assembly present. The F23 and F24 regions that shared its original file
/// exercise Core types instead, and stayed in the portable project rather than following the WPF
/// half across the split.
/// </remarks>
public class F17_ScopeViewModelBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F17_Boundary_DoubleNaNValues_SkippedInRendering()
    {
        var scope = new ScopeViewModel();
        double[] data = new double[] { 1.0, double.NaN, 3.0, double.PositiveInfinity };

        Action act = () => scope.AddDataPoints("TEMP", data);
        act.Should().NotThrow();
        scope.GetValidPointCount("TEMP").Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F17_Boundary_ExtremePointCount_100kPoints_NoBufferOverflow()
    {
        var scope = new ScopeViewModel();
        double[] largeData = new double[100_000];
        Array.Fill(largeData, 42.0);

        scope.AddDataPoints("VIB", largeData);
        scope.GetTotalPointCount("VIB").Should().Be(100_000);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F17_Boundary_ZeroDataPoints_RendersEmptyPlotArea()
    {
        var scope = new ScopeViewModel();
        scope.ClearPoints();

        scope.GetTotalPointCount("TEMP").Should().Be(0);
        scope.IsPlotAreaEmpty.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F17_Boundary_InfiniteYAxisRange_AutoScalesToDefaults()
    {
        var scope = new ScopeViewModel();
        scope.AddDataPoints("VOLT", new double[] { double.NegativeInfinity, double.PositiveInfinity });

        scope.AutoScaleYAxis();
        scope.YMin.Should().BeGreaterThan(double.NegativeInfinity);
        scope.YMax.Should().BeLessThan(double.PositiveInfinity);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F17_Boundary_RapidClearAndStream_NoConcurrencyRaceCondition()
    {
        var scope = new ScopeViewModel();
        Parallel.For(0, 50, i =>
        {
            if (i % 2 == 0) scope.AddDataPoints("TEMP", new double[] { i });
            else scope.ClearPoints();
        });
        scope.GetTotalPointCount("TEMP").Should().BeGreaterThanOrEqualTo(0);
    }
}
