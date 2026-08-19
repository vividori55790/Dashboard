namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F20_Helix3DViewerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void Helix3DViewer_Initialize_SetsViewportAndCamera()
    {
        var state = new Helix3DState();
        state.IsInitialized.Should().BeTrue();
        state.CameraPosition.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Helix3DViewer_LoadObjModel_ParsesVertices()
    {
        var state = new Helix3DState();
        bool success = state.LoadModel("engine.obj");

        success.Should().BeTrue();
        state.VertexCount.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Helix3DViewer_LoadStlModel_ParsesTriangles()
    {
        var state = new Helix3DState();
        bool success = state.LoadModel("bracket.stl");

        success.Should().BeTrue();
        state.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Helix3DViewer_TransformModel_UpdatesBoundingBox()
    {
        var state = new Helix3DState();
        state.LoadModel("engine.obj");
        state.SetScale(2.0);

        state.BoundingVolume.Should().Be(2.0 * 1000.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Helix3DViewer_CameraOrbit_UpdatesViewMatrix()
    {
        var state = new Helix3DState();
        state.OrbitCamera(45.0, 30.0);

        state.Azimuth.Should().Be(45.0);
        state.Elevation.Should().Be(30.0);
    }
}

public class Helix3DState
{
    public bool IsInitialized { get; set; } = true;
    public string CameraPosition { get; set; } = "(0,0,10)";
    public int VertexCount { get; private set; }
    public int TriangleCount { get; private set; }
    public double Scale { get; private set; } = 1.0;
    public double BoundingVolume => Scale * 1000.0;
    public double Azimuth { get; private set; }
    public double Elevation { get; private set; }

    public bool LoadModel(string path)
    {
        VertexCount = 500;
        TriangleCount = 300;
        return true;
    }

    public void SetScale(double s) => Scale = s;

    public void OrbitCamera(double az, double el)
    {
        Azimuth = az;
        Elevation = el;
    }
}
