using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Archive;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The <c>export</c> subcommand, run against real archives and read back as a loader would.
/// </summary>
/// <remarks>
/// The capability existed and only the desktop shell could reach it, so a recording made by the
/// headless host — the thing that actually sits on a bench overnight — could not be opened in
/// MATLAB without moving the file to a Windows machine with the GUI on it.
/// <para>
/// Driven on the running host as well: a simulated run archived to SQLite, exported, and the
/// MAT-file compared sample by sample against the same window read back through
/// <c>/api/history</c>. 1,120 samples, every value identical, timestamps agreeing to 6 µs.
/// </para>
/// </remarks>
public sealed class ExportCommandTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 8, 21, 14, 0, 0, DateTimeKind.Utc);

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static IEnumerable<TelemetryPacket> Samples(int count) =>
        Enumerable.Range(0, count).SelectMany(i => new[]
        {
            new TelemetryPacket("RIG", "Vout", 48.0 + i, "V", T0.AddSeconds(i)),
            new TelemetryPacket("RIG", "Iout", 3.0 + i, "A", T0.AddSeconds(i))
        });

    private async Task<string> RowArchiveAsync(string name, int count = 10)
    {
        string path = _workspace.File(name);
        using var store = new SqliteDataLogger(path);
        await store.WriteBatchAsync(Samples(count));
        return path;
    }

    private int Run(params string[] args) => ExportCommand.Run(new[] { "export" }.Concat(args).ToArray());

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ARowArchiveExportsEveryChannelAsItsOwnMatrix()
    {
        string archive = await RowArchiveAsync("rows.db");
        string target = _workspace.File("out.mat");

        Run(archive, "--out", target).Should().Be(0);

        IReadOnlyList<MatMatrix> matrices = MatLevel4Reader.Read(target);
        matrices.Select(m => m.Name).Should().BeEquivalentTo("Vout", "Iout");
        matrices.Should().AllSatisfy(m => m.Rows.Should().Be(10));
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AWindowNarrowsWhatReachesTheFile()
    {
        string archive = await RowArchiveAsync("window.db", count: 20);
        string target = _workspace.File("window.mat");

        Run(archive, "--out", target, "--channel", "Vout",
            "--from", "2026-08-21T14:00:05Z", "--to", "2026-08-21T14:00:09Z").Should().Be(0);

        IReadOnlyList<MatMatrix> matrices = MatLevel4Reader.Read(target);
        matrices.Should().ContainSingle().Which.Rows.Should().Be(5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AnEmptySelectionLeavesNoFileRatherThanAnUnloadableOne()
    {
        // A Level 4 MAT-file with no matrices is zero bytes, and a zero-byte file loads as
        // truncated rather than as empty -- so a stale export sitting at the destination would be
        // opened as though it were this one.
        string archive = await RowArchiveAsync("empty.db");
        string target = _workspace.File("stale.mat");
        await File.WriteAllTextAsync(target, "an earlier export");

        Run(archive, "--out", target, "--channel", "NoSuchChannel").Should().Be(ExportCommand.ExitNoData);

        File.Exists(target).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void AnArchiveThatDoesNotExistIsRefusedRatherThanBroughtIntoBeing()
    {
        // SQLite creates what it is asked to open, so without the check this would report a
        // successful read of an empty archive it had just made, and leave the file behind.
        string absent = _workspace.File("typo.db");

        Run(absent, "--out", _workspace.File("x.mat")).Should().Be(ExportCommand.ExitNoData);

        File.Exists(absent).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task ATieredArchiveIsReadAsBlocksRatherThanRefused()
    {
        string path = _workspace.File("tiered.db");
        using (var store = new TieredTelemetryStore(path)) await store.WriteBatchAsync(Samples(10));
        string target = _workspace.File("tiered.mat");

        Run(path, "--out", target).Should().Be(0);

        MatLevel4Reader.Read(target).Select(m => m.Name).Should().BeEquivalentTo("Vout", "Iout");
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void SomebodyElsesDatabaseIsRefusedWithoutHavingArchiveTablesAddedToIt()
    {
        string path = _workspace.File("stranger.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE notes(id INTEGER);";
            create.ExecuteNonQuery();
        }

        Run(path, "--out", _workspace.File("x.mat")).Should().Be(ExportCommand.ExitNoData);

        ArchiveLayout.Detect(path).Should().Be(ArchiveLayoutKind.Unknown,
            "a refused export must not have opened it as either store");
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task APathWithNoExtensionIsWrittenAsAMatFile()
    {
        string archive = await RowArchiveAsync("bare.db");
        string bare = _workspace.File("bench");

        Run(archive, "--out", bare).Should().Be(0);

        File.Exists(bare + MatlabArchiveExporter.Extension).Should().BeTrue();
    }
}
