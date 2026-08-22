using System;
using System.IO;
using System.Windows.Media.Media3D;
using FluentAssertions;
using TelemetryDashboard.UI.ViewModels;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// The pieces the digital-twin panel needed before it could show a machine instead of a box.
/// </summary>
/// <remarks>
/// <c>DigitalTwin3DViewControl</c> was complete — toolbar, viewport, lights, grid — and had no
/// window anywhere in the shell, so it had never appeared in the running application. The mesh it
/// drew was a box written into its own markup, while <see cref="Twin3DService"/>,
/// <c>StlFileProbe</c> and <c>MeshBounds</c> sat beside it, tested, and constructed by nothing.
/// <para>
/// Wiring it needed one addition to the service and one new seam, and both are here.
/// </para>
/// </remarks>
public class Twin3DPanelWiringTests
{
    private static MeshGeometry3D Cube(double edge, Point3D origin)
    {
        var mesh = new MeshGeometry3D();
        foreach ((double dx, double dy, double dz) in new[]
        {
            (0d, 0d, 0d), (1d, 0d, 0d), (1d, 1d, 0d), (0d, 1d, 0d),
            (0d, 0d, 1d), (1d, 0d, 1d), (1d, 1d, 1d), (0d, 1d, 1d)
        })
        {
            mesh.Positions.Add(new Point3D(origin.X + dx * edge, origin.Y + dy * edge, origin.Z + dz * edge));
        }

        foreach (int index in new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }) mesh.TriangleIndices.Add(index);
        return mesh;
    }

    private static Model3DGroup GroupOf(params Model3D[] models)
    {
        var group = new Model3DGroup();
        foreach (Model3D model in models) group.Children.Add(model);
        return group;
    }

    private static GeometryModel3D Geometry(MeshGeometry3D mesh, Transform3D? transform = null) =>
        new() { Geometry = mesh, Transform = transform ?? Transform3D.Identity };

    // ---- the seam between the renderer and the view state ---------------------

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AMeshWithNoIndexListIsTakenAsConsecutiveTriples()
    {
        // Binary STL carries no index list at all. Without this rule a perfectly good model
        // flattens to zero triangles and the panel reports "holds no geometry" for a file it read.
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < 6; i++) mesh.Positions.Add(new Point3D(i, i * 2, i * 3));

        (float[] vertices, int[] indices) = TwinMeshFlattener.Flatten(Geometry(mesh));

        vertices.Should().HaveCount(18);
        indices.Should().Equal(new[] { 0, 1, 2, 3, 4, 5 });
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void TrianglesFromASecondMeshAreRebasedSoTheyDoNotPointAtTheFirst()
    {
        Model3DGroup scene = GroupOf(
            Geometry(Cube(1, new Point3D(0, 0, 0))),
            Geometry(Cube(1, new Point3D(5, 0, 0))));

        (float[] vertices, int[] indices) = TwinMeshFlattener.Flatten(scene);

        vertices.Should().HaveCount(2 * 8 * 3);
        indices.Should().HaveCount(2 * 12);
        indices[12..].Should().OnlyContain(i => i >= 8,
            "the second mesh's indices must move past the first mesh's vertices");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void APlacedSubAssemblyIsMeasuredWhereItSitsRatherThanAtTheOrigin()
    {
        // The failure ignoring transforms produces is not a crash. A scene whose parts are placed
        // by transform collapses onto itself, the bounding box comes out the size of one part, and
        // the model is then normalised by a scale computed from a size it does not have.
        MeshGeometry3D unit = Cube(1, new Point3D(0, 0, 0));
        Model3DGroup placed = GroupOf(
            Geometry(unit),
            Geometry(unit, new TranslateTransform3D(40, 0, 0)));

        (float[] vertices, _) = TwinMeshFlattener.Flatten(placed);

        MeshBoundsProbe(vertices).Should().BeApproximately(41f, 1e-4f,
            "the far cube sits 40 out and is 1 across");
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ATransformOnAGroupReachesTheMeshesInsideIt()
    {
        Model3DGroup inner = GroupOf(Geometry(Cube(1, new Point3D(0, 0, 0))));
        inner.Transform = new ScaleTransform3D(10, 10, 10);

        (float[] vertices, _) = TwinMeshFlattener.Flatten(GroupOf(inner));

        MeshBoundsProbe(vertices).Should().BeApproximately(10f, 1e-4f);
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AnEmptySceneFlattensToNothingRatherThanThrowing()
    {
        TwinMeshFlattener.Flatten(null).Vertices.Should().BeEmpty();
        TwinMeshFlattener.Flatten(new Model3DGroup()).Indices.Should().BeEmpty();
    }

    // ---- what the service could not previously say ---------------------------

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AFileThatPassesItsCheckAndThenCannotBeParsedStopsClaimingToBeLoaded()
    {
        // LoadModel returning true is a promise about the file, not about the mesh inside it, and
        // the two disagree in practice: measured on the running application, an .stl holding prose
        // that begins with the word "solid" clears the probe and then throws FormatException out
        // of the reader. Before RejectLoadedModel there was no way to walk that back -- the service
        // went on reporting a loaded model, with a path, for a mesh nothing had ever read.
        var service = new Twin3DService();
        string path = Path.Combine(Path.GetTempPath(), $"twin-{Guid.NewGuid():N}.stl");
        File.WriteAllText(path, "solid " + new string('x', 200));

        try
        {
            service.LoadModel(path).Should().BeTrue("the file is shaped like an STL");
            service.LoadedModelPath.Should().Be(path);

            service.RejectLoadedModel();

            service.LoadedModelPath.Should().BeNull();
            service.IsFallbackModelActive.Should().BeTrue();
            service.HasModel.Should().BeTrue("a placeholder is still something to draw");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void RejectingAModelDoesNotLeaveThePreviousModelsTriangleCountBehind()
    {
        var service = new Twin3DService();
        service.SetCustomMesh(FlatCube(200f), TriangleList(12));
        service.TriangleCount.Should().Be(12);

        service.RejectLoadedModel();

        service.TriangleCount.Should().Be(12, "the placeholder cube has twelve of its own");
        service.IsFallbackModelActive.Should().BeTrue();
    }

    // ---- the whole path, as the panel walks it -------------------------------

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ACadExportInMillimetresIsNormalisedIntoTheCameraFrustum()
    {
        // The measurement the running panel reported: a 200 mm heatsink mesh loaded from beside the
        // executable read "twin-model.stl - 60 triangles - 0.05x to fit". A CAD export carries no
        // dependable unit tag, so the same part arrives six orders of magnitude out and renders as
        // nothing at all unless something scales it.
        Model3DGroup scene = GroupOf(Geometry(Cube(200, new Point3D(0, 0, 0))));
        (float[] vertices, int[] indices) = TwinMeshFlattener.Flatten(scene);

        var service = new Twin3DService();
        service.SetCustomMesh(vertices, indices);

        service.ModelScale.Should().BeApproximately(0.05f, 1e-6f, "10 units of viewport over 200 mm");
        service.NormalizedBoundingBoxSize.Should().BeApproximately(10f, 1e-4f);
        service.TriangleCount.Should().Be(4);
        service.HasModel.Should().BeTrue();
        service.IsFallbackModelActive.Should().BeFalse();
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AModelAlreadySmallerThanTheViewportIsLeftAtItsOwnSize()
    {
        Model3DGroup scene = GroupOf(Geometry(Cube(2, new Point3D(0, 0, 0))));
        (float[] vertices, int[] indices) = TwinMeshFlattener.Flatten(scene);

        var service = new Twin3DService();
        service.SetCustomMesh(vertices, indices);

        service.ModelScale.Should().Be(1.0f, "scaling a small part up would misrepresent its size");
        service.NormalizedBoundingBoxSize.Should().BeApproximately(2f, 1e-4f);
    }

    /// <summary>Largest bounding-box edge of a flat vertex array, computed here independently.</summary>
    private static float MeshBoundsProbe(float[] vertices)
    {
        float extent = 0f;
        for (int axis = 0; axis < 3; axis++)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int v = 0; v * 3 + axis < vertices.Length; v++)
            {
                float value = vertices[v * 3 + axis];
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (min <= max) extent = Math.Max(extent, max - min);
        }
        return extent;
    }

    private static float[] FlatCube(float edge) => new[]
    {
        0f, 0f, 0f, edge, 0f, 0f, edge, edge, 0f, 0f, edge, 0f,
        0f, 0f, edge, edge, 0f, edge, edge, edge, edge, 0f, edge, edge
    };

    private static int[] TriangleList(int triangles)
    {
        var indices = new int[triangles * 3];
        for (int i = 0; i < indices.Length; i++) indices[i] = i % 8;
        return indices;
    }
}
