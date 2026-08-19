using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The production route from the durable archive to a MATLAB MAT-file.
/// </summary>
/// <remarks>
/// <c>MatFileWriter</c> had no caller anywhere in the product, so "exports to MATLAB" was a class
/// rather than a capability. These tests exercise the whole path — packets recorded into SQLite,
/// queried back through <see cref="IDataLogger"/>, written as MAT — and decode the resulting bytes
/// with <see cref="MatLevel4Reader"/>, which shares no code with the writer.
/// </remarks>
public sealed class MatlabArchiveExporterTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 3, 14, 1, 59, 26, DateTimeKind.Utc);

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    /// <summary>Records two channels into a real SQLite archive and returns it.</summary>
    private async Task<SqliteDataLogger> RecordedArchiveAsync(int samples)
    {
        var archive = new SqliteDataLogger(_workspace.File("archive.db"));
        var ring = new ChannelDataLogger(capacity: 4096);
        var drain = new ChannelDataLoggerDrain(ring, archive, batchSize: 64);

        for (int i = 0; i < samples; i++)
        {
            DateTime ts = T0.AddTicks(i * 20_000L); // 2 ms spacing
            ring.TryEnqueue(new TelemetryPacket("DAB", "bus_voltage", 380.0 + i, "V", ts));
            ring.TryEnqueue(new TelemetryPacket("DAB", "coil_temp", 41.5 + (0.05 * i), "C", ts));
        }

        // Drained through StopAsync rather than the background loop: the archive is fixture data
        // here, and it must be complete before the export runs.
        await drain.StopAsync();
        return archive;
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ExportAsync_RecordedArchive_ProducesOneParseableMatrixPerChannel()
    {
        using SqliteDataLogger archive = await RecordedArchiveAsync(samples: 25);
        var exporter = new MatlabArchiveExporter(archive);
        string target = _workspace.File("recording.mat");

        int exported = await exporter.ExportAsync(target, new QueryFilter(NodeId: "DAB", Limit: 10_000));

        exported.Should().Be(50);
        exporter.FileExtension.Should().Be(".mat");

        IReadOnlyList<MatMatrix> matrices = MatLevel4Reader.Read(target);
        matrices.Select(m => m.Name).Should().BeEquivalentTo(new[] { "bus_voltage", "coil_temp" });

        MatMatrix bus = matrices.Single(m => m.Name == "bus_voltage");
        bus.Rows.Should().Be(25);
        bus.Columns.Should().Be(2, "each row is [datenum, value]");
        bus.Values[0, 1].Should().Be(380.0);
        bus.Values[24, 1].Should().Be(404.0);

        MatMatrix coil = matrices.Single(m => m.Name == "coil_temp");
        coil.Values[0, 1].Should().Be(41.5);
        coil.Values[10, 1].Should().BeApproximately(42.0, 1e-12);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ExportAsync_TimestampColumn_CarriesMatlabDateNumbers()
    {
        using SqliteDataLogger archive = await RecordedArchiveAsync(samples: 3);
        var exporter = new MatlabArchiveExporter(archive);
        string target = _workspace.File("times.mat");

        await exporter.ExportAsync(target, new QueryFilter(Limit: 1000));

        MatMatrix bus = MatLevel4Reader.Read(target).Single(m => m.Name == "bus_voltage");

        // datenum counts days from year 0, with 719529 at the Unix epoch. Reconstructing the instant
        // from the number is what an engineer does in MATLAB, so that is what is asserted.
        double expected = 719529.0 + (T0 - DateTime.UnixEpoch).TotalDays;
        bus.Values[0, 0].Should().BeApproximately(expected, 1e-9);

        DateTime recovered = DateTime.UnixEpoch.AddDays(bus.Values[0, 0] - 719529.0);
        recovered.Should().BeCloseTo(T0, TimeSpan.FromMilliseconds(1));

        // Rows are 2 ms apart, which must survive as a difference in the datenum column.
        double stepDays = bus.Values[1, 0] - bus.Values[0, 0];
        TimeSpan.FromDays(stepDays).TotalMilliseconds.Should().BeApproximately(2.0, 0.01);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ExportAsync_RowsAreSortedByTimeEvenWhenTheArchiveIsNot()
    {
        using var archive = new SqliteDataLogger(_workspace.File("shuffled.db"));
        await archive.WriteBatchAsync(new[]
        {
            new TelemetryPacket("N", "ch", 30, "u", T0.AddSeconds(3)),
            new TelemetryPacket("N", "ch", 10, "u", T0.AddSeconds(1)),
            new TelemetryPacket("N", "ch", 20, "u", T0.AddSeconds(2))
        });
        string target = _workspace.File("sorted.mat");

        await new MatlabArchiveExporter(archive).ExportAsync(target, new QueryFilter());

        MatMatrix matrix = MatLevel4Reader.Read(target).Single();
        new[] { matrix.Values[0, 1], matrix.Values[1, 1], matrix.Values[2, 1] }
            .Should().Equal(10, 20, 30);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ExportAsync_EmptyWindow_WritesNoFileAndRemovesAStaleOne()
    {
        using SqliteDataLogger archive = await RecordedArchiveAsync(samples: 2);
        var exporter = new MatlabArchiveExporter(archive);
        string target = _workspace.File("reused.mat");
        await exporter.ExportAsync(target, new QueryFilter(Limit: 1000));
        File.Exists(target).Should().BeTrue();

        int exported = await exporter.ExportAsync(target, new QueryFilter(NodeId: "NO_SUCH_NODE"));

        exported.Should().Be(0);
        // A zero-matrix Level 4 file is zero bytes, and a zero-byte file does not load — it reports
        // as truncated. Leaving the previous export in place would be worse still: it would open.
        File.Exists(target).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ExportAsync_MissingDestinationFolder_ThrowsBeforeWritingAnything()
    {
        using SqliteDataLogger archive = await RecordedArchiveAsync(samples: 2);
        string target = Path.Combine(_workspace.Root, "no-such-folder", "out.mat");

        Func<Task> export = () => new MatlabArchiveExporter(archive)
            .ExportAsync(target, new QueryFilter(Limit: 1000));

        await export.Should().ThrowAsync<DirectoryNotFoundException>();
    }
}
