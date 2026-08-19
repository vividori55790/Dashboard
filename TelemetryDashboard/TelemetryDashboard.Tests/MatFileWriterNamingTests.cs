using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Channel names must survive into the MAT-file as distinct, legal MATLAB identifiers.
/// </summary>
/// <remarks>
/// Found while giving <c>MatFileWriter</c> a production export path. Sanitisation accepted any
/// Unicode letter, so every non-ASCII channel name passed through intact and was then flattened to
/// question marks by the ASCII encoder — on this Korean-language dashboard "온도" and "습도" both
/// became the single name "??", and the file that came out held one matrix instead of two. The
/// export looked successful and a channel was simply missing from it.
/// </remarks>
public sealed class MatFileWriterNamingTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 3, 14, 1, 59, 26, DateTimeKind.Utc);

    private readonly TempWorkspace _workspace = new();
    private readonly MatFileWriter _writer = new();

    public void Dispose() => _workspace.Dispose();

    private IReadOnlyList<MatMatrix> WriteAndRead(params string[] channels)
    {
        string target = _workspace.File("names.mat");
        _writer.WritePackets(target, channels.Select((name, i) =>
            new TelemetryPacket("N", name, i + 1, "u", T0)));
        return MatLevel4Reader.Read(target);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void AsciiChannelNames_ArePassedThroughUnchanged()
    {
        IReadOnlyList<MatMatrix> matrices = WriteAndRead("bus_voltage", "coil_temp");

        matrices.Select(m => m.Name).Should().Equal("bus_voltage", "coil_temp");
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void NonAsciiChannelNames_ProduceDistinctMatricesRatherThanOverwritingEachOther()
    {
        IReadOnlyList<MatMatrix> matrices = WriteAndRead("온도", "습도");

        matrices.Should().HaveCount(2, "neither channel may disappear from the export");
        matrices.Select(m => m.Name).Should().OnlyHaveUniqueItems();
        // Values identify which channel each matrix came from: 1 was written first.
        matrices[0].Values[0, 1].Should().Be(1);
        matrices[1].Values[0, 1].Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void EveryEmittedName_IsALegalMatlabIdentifier()
    {
        IReadOnlyList<MatMatrix> matrices =
            WriteAndRead("온도", "3_phase", "bus/voltage", "_leading", new string('x', 40));

        foreach (MatMatrix matrix in matrices)
        {
            matrix.Name.Should().MatchRegex("^[A-Za-z][A-Za-z0-9_]*$",
                "MATLAB rejects a name that starts with anything but a letter or contains punctuation");
            matrix.Name.Length.Should().BeLessThanOrEqualTo(31);
        }

        matrices.Select(m => m.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void NamesCollidingOnlyAfterTruncation_AreStillDistinguished()
    {
        // Both names agree for well over 31 characters, so truncation alone would merge them.
        string prefix = new('c', 30);
        IReadOnlyList<MatMatrix> matrices = WriteAndRead(prefix + "_alpha", prefix + "_beta");

        matrices.Should().HaveCount(2);
        matrices.Select(m => m.Name).Should().OnlyHaveUniqueItems();
    }
}
