using System;
using System.IO;
using System.Text;
using FluentAssertions;
using TelemetryDashboard.UI.ViewModels;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// Every way a model file fails, and what the operator is told about it.
/// </summary>
/// <remarks>
/// These cases are here because they were reachable in the running program and could not be
/// reproduced anywhere else. The load happens from the panel's <c>Loaded</c> handler, so before the
/// reading was split out of the control an unhandled parse error would have come out on the
/// dispatcher and taken the dashboard down the first time anyone opened the twin — and a malformed
/// .stl sitting beside the executable was enough to do it.
/// <para>
/// Each fixture is written to disk rather than mocked, because the failures being pinned are
/// properties of real files: a length that does not match a declared triangle count, a header word
/// that promises a format the body does not keep.
/// </para>
/// </remarks>
public class TwinModelLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "twin-loader-" + Guid.NewGuid().ToString("N"));

    public TwinModelLoaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A binary STL of <paramref name="triangles"/> triangles spanning <paramref name="edge"/> units.</summary>
    private string WriteBinaryStl(string name, int triangles, float edge)
    {
        string path = Path.Combine(_directory, name);
        using var stream = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("test mesh".PadRight(80, '\0')));
        writer.Write(triangles);
        for (int i = 0; i < triangles; i++)
        {
            writer.Write(0f); writer.Write(0f); writer.Write(1f);           // normal
            writer.Write(0f); writer.Write(0f); writer.Write(0f);           // vertex 1
            writer.Write(edge); writer.Write(0f); writer.Write(0f);         // vertex 2
            writer.Write(0f); writer.Write(edge); writer.Write(0f);         // vertex 3
            writer.Write((ushort)0);
        }
        return path;
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void ABinaryStlIsReadIntoTrianglesEvenThoughItCarriesNoIndexList()
    {
        // The format's own shape: binary STL lists three vertices per facet and no indices at all.
        // A flattener that required an index list would report "holds no geometry" for every
        // binary mesh in existence, which is most of them.
        TwinModelLoad load = TwinModelLoader.Read(WriteBinaryStl("cube.stl", triangles: 12, edge: 200f));

        load.Succeeded.Should().BeTrue(load.Failure);
        load.Failure.Should().BeNull();
        load.Indices.Should().HaveCount(12 * 3);
        load.Vertices.Should().HaveCount(12 * 3 * 3);
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AFileWhoseFirstWordIsSolidButIsOtherwiseProseIsRefusedRatherThanThrowing()
    {
        // Measured on the running application. This clears StlFileProbe -- it is long enough and it
        // starts with the STL keyword -- and then produces no facets at all.
        string path = Write("prose.stl", "solid " + new string('x', 200));

        TwinModelLoad load = TwinModelLoader.Read(path);

        load.Succeeded.Should().BeFalse();
        load.Failure.Should().Be("holds no geometry");
        load.Vertices.Should().BeEmpty();
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void AMalformedFacetIsReportedByTheKindOfFailureRatherThanCrashingTheDashboard()
    {
        // Also measured on the running application, where it surfaced as
        // "twin-model.stl could not be parsed: FormatException" on the panel's own toolbar.
        string path = Write("broken.stl",
            "solid broken\n"
            + string.Concat(System.Linq.Enumerable.Repeat(
                "facet normal not-a-number also-not one\n  outer loop\n"
                + "    vertex 0 0 0\n    vertex 1 0 0\n    vertex 0 1 0\n  endloop\nendfacet\n", 3))
            + "endsolid broken\n");

        TwinModelLoad load = TwinModelLoader.Read(path);

        load.Succeeded.Should().BeFalse();
        load.Failure.Should().Contain("could not be parsed").And.Contain("FormatException");
        load.Failure.Should().NotContain("line ",
            "a reader's message names a position in a file the operator did not write");
    }

    [WpfFact]
    [Trait("Category", "Tier2")]
    public void AFileThatIsNotThereIsAnAnswerRatherThanAnException()
    {
        TwinModelLoad load = TwinModelLoader.Read(Path.Combine(_directory, "absent.stl"));

        load.Succeeded.Should().BeFalse();
        load.Failure.Should().NotBeNullOrWhiteSpace();
    }

    [WpfFact]
    [Trait("Category", "Tier1")]
    public void TheWholePathFromFileToNormalisedModelAgreesWithWhatThePanelReported()
    {
        // The running panel showed "twin-model.stl - 60 triangles - 0.05x to fit" for a heatsink
        // mesh 200 mm along its longest edge. This walks the same steps the control walks, so the
        // number in that readout is pinned rather than remembered.
        string path = WriteBinaryStl("heatsink.stl", triangles: 60, edge: 200f);
        var service = new Twin3DService();

        service.LoadModel(path).Should().BeTrue();
        TwinModelLoad load = TwinModelLoader.Read(path);
        service.SetCustomMesh(load.Vertices, load.Indices);

        service.TriangleCount.Should().Be(60);
        service.ModelScale.Should().BeApproximately(0.05f, 1e-6f);
        service.NormalizedBoundingBoxSize.Should().BeApproximately(10f, 1e-4f);
        service.IsFallbackModelActive.Should().BeFalse();
    }
}
