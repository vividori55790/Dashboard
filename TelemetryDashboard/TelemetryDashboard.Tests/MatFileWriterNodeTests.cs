using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Samples from two devices must not arrive in one column.
/// </summary>
/// <remarks>
/// Found while giving the exporter a headless caller. Matrices were grouped by channel name alone,
/// so a hub archiving two converters that both report <c>Vout</c> exported a single matrix holding
/// both of them, ordered by time and interleaved, with nothing left in the file to say which
/// reading came from which device. Invisible while the only caller was a desktop app watching one
/// rig; the normal case for a host, which is what a hub is for.
/// <para>
/// The MAT-file is read back by <see cref="MatLevel4Reader"/> rather than inspected through the
/// writer, so these assert what a loader will actually find.
/// </para>
/// </remarks>
public sealed class MatFileWriterNodeTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 3, 14, 1, 59, 26, DateTimeKind.Utc);

    private readonly TempWorkspace _workspace = new();
    private readonly MatFileWriter _writer = new();

    public void Dispose() => _workspace.Dispose();

    private IReadOnlyList<MatMatrix> WriteAndRead(params TelemetryPacket[] packets)
    {
        string target = _workspace.File("nodes.mat");
        _writer.WritePackets(target, packets);
        return MatLevel4Reader.Read(target);
    }

    private static TelemetryPacket Sample(string node, string channel, double value, int second) =>
        new(node, channel, value, "V", T0.AddSeconds(second));

    [Fact]
    [Trait("Category", "Storage")]
    public void TwoNodesReportingTheSameChannel_AreNotMergedIntoOneMatrix()
    {
        // Interleaved in time on purpose: a merge sorted by timestamp is exactly what produced the
        // single column of alternating readings, so a grouping that only happened to work because
        // one device's samples all preceded the other's would pass a weaker test than this.
        IReadOnlyList<MatMatrix> matrices = WriteAndRead(
            Sample("PSFB-01", "Vout", 48.0, 0),
            Sample("PSFB-02", "Vout", 12.0, 1),
            Sample("PSFB-01", "Vout", 48.1, 2),
            Sample("PSFB-02", "Vout", 12.1, 3));

        matrices.Should().HaveCount(2, "each converter's readings are its own measurement");
        matrices.Select(m => m.Rows).Should().AllBeEquivalentTo(2);

        foreach (MatMatrix matrix in matrices)
        {
            // Each matrix holds one device's readings, which for this data means both of its values
            // sit on the same side of 30 V. A merged matrix has one of each.
            bool high = matrix.Values[0, 1] > 30.0;
            (matrix.Values[1, 1] > 30.0).Should().Be(high, "a matrix must hold one device, not two");
        }
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void MatricesFromSeveralNodes_AreNamedForTheNodeTheyCameFrom()
    {
        IReadOnlyList<MatMatrix> matrices = WriteAndRead(
            Sample("PSFB-01", "Vout", 48.0, 0),
            Sample("PSFB-02", "Vout", 12.0, 1));

        matrices.Select(m => m.Name).Should().BeEquivalentTo("PSFB_01_Vout", "PSFB_02_Vout");
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void AnExportFromOneNode_KeepsThePlainChannelNames()
    {
        // The common case, and the one every existing script reads: a single rig's export must not
        // grow a prefix because the writer learned about nodes.
        IReadOnlyList<MatMatrix> matrices = WriteAndRead(
            Sample("PSFB-01", "Vout", 48.0, 0),
            Sample("PSFB-01", "Iout", 3.2, 1));

        matrices.Select(m => m.Name).Should().BeEquivalentTo("Vout", "Iout");
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void TheNodePrefixIsAllOrNothingWithinOneFile()
    {
        // Deciding per channel would mean a name could not be read without knowing how many nodes
        // happened to report it -- so a script asking for Temp would break the day a second rig
        // started reporting Vout, a channel it never touched.
        IReadOnlyList<MatMatrix> matrices = WriteAndRead(
            Sample("PSFB-01", "Vout", 48.0, 0),
            Sample("PSFB-02", "Vout", 12.0, 1),
            Sample("PSFB-01", "Temp", 41.5, 2));

        matrices.Select(m => m.Name).Should().BeEquivalentTo(
            "PSFB_01_Vout", "PSFB_02_Vout", "PSFB_01_Temp");
    }
}
