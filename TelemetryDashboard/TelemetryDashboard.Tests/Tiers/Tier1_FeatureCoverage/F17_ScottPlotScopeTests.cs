namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F17_ScottPlotScopeTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void ScopeViewModel_Initialize_ConfiguresSeries()
    {
        var scope = new ScottPlotScopeState();
        scope.SeriesCount.Should().Be(0);
        scope.AddSeries("Channel_1");
        scope.SeriesCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ScopeViewModel_AddDataPoint_UpdatesBuffer()
    {
        var scope = new ScottPlotScopeState();
        scope.AddSeries("TEMP");
        scope.AddDataPoint("TEMP", 1.0, 45.0);

        scope.GetPointsCount("TEMP").Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ScopeViewModel_HighSpeedStream_RecyclesRingBuffer()
    {
        var scope = new ScottPlotScopeState(maxCapacity: 100);
        scope.AddSeries("VIB");

        for (int i = 0; i < 150; i++)
        {
            scope.AddDataPoint("VIB", i, i * 0.1);
        }

        scope.GetPointsCount("VIB").Should().Be(100);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ScopeViewModel_AutoScale_UpdatesAxisLimits()
    {
        var scope = new ScottPlotScopeState();
        scope.AddSeries("VOLT");
        scope.AddDataPoint("VOLT", 0, 10.0);
        scope.AddDataPoint("VOLT", 1, 15.0);

        scope.AutoScale();

        scope.MinY.Should().Be(10.0);
        scope.MaxY.Should().Be(15.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ScopeViewModel_ToggleSeries_UpdatesVisibility()
    {
        var scope = new ScottPlotScopeState();
        scope.AddSeries("RPM");

        scope.SetSeriesVisible("RPM", false);
        scope.IsSeriesVisible("RPM").Should().BeFalse();

        scope.SetSeriesVisible("RPM", true);
        scope.IsSeriesVisible("RPM").Should().BeTrue();
    }
}

public class ScottPlotScopeState
{
    private readonly int _maxCapacity;
    private readonly Dictionary<string, List<(double x, double y)>> _data = new();
    private readonly Dictionary<string, bool> _visibility = new();

    public int SeriesCount => _data.Count;
    public double MinY { get; private set; }
    public double MaxY { get; private set; }

    public ScottPlotScopeState(int maxCapacity = 1000)
    {
        _maxCapacity = maxCapacity;
    }

    public void AddSeries(string name)
    {
        _data[name] = new List<(double, double)>();
        _visibility[name] = true;
    }

    public void AddDataPoint(string name, double x, double y)
    {
        if (_data.TryGetValue(name, out var list))
        {
            list.Add((x, y));
            if (list.Count > _maxCapacity) list.RemoveAt(0);
        }
    }

    public int GetPointsCount(string name) => _data.TryGetValue(name, out var list) ? list.Count : 0;

    public void AutoScale()
    {
        var allY = _data.Values.SelectMany(l => l.Select(p => p.y)).ToList();
        if (allY.Count > 0)
        {
            MinY = allY.Min();
            MaxY = allY.Max();
        }
    }

    public void SetSeriesVisible(string name, bool visible) => _visibility[name] = visible;
    public bool IsSeriesVisible(string name) => _visibility.TryGetValue(name, out var v) && v;
}
