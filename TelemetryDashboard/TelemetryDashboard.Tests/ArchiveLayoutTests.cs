using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Working out which store wrote an archive, without writing to it.
/// </summary>
/// <remarks>
/// The read has to be non-destructive, and that is not a nicety: both stores open their file with
/// <c>CREATE TABLE IF NOT EXISTS</c>, so a reader that guessed wrong and opened a row archive as a
/// tiered one would leave tiered tables in the operator's file — and the next reader would then
/// find both and have no way to tell which half was the recording.
/// </remarks>
public sealed class ArchiveLayoutTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static IEnumerable<TelemetryPacket> OneSample() => new[]
    {
        new TelemetryPacket("RIG", "rail", 48.0, "V", DateTime.UtcNow)
    };

    private static IReadOnlyList<string> TablesIn(string path)
    {
        var names = new List<string>();
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AnArchiveTheRowStoreWroteIsReadAsTheRowStores()
    {
        string path = _workspace.File("rows.db");
        using (var store = new SqliteDataLogger(path)) await store.WriteBatchAsync(OneSample());

        ArchiveLayout.Detect(path).Should().Be(ArchiveLayoutKind.Rows);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AnArchiveTheTieredStoreWroteIsReadAsTheTieredOne()
    {
        string path = _workspace.File("tiered.db");
        using (var store = new TieredTelemetryStore(path)) await store.WriteBatchAsync(OneSample());

        ArchiveLayout.Detect(path).Should().Be(ArchiveLayoutKind.Tiered);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void ADatabaseThatIsNotAnArchiveIsNotMistakenForAnEmptyOne()
    {
        // An empty archive and somebody else's database look alike only if you stop at "it opened".
        string path = _workspace.File("stranger.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE notes(id INTEGER);";
            command.ExecuteNonQuery();
        }

        ArchiveLayout.Detect(path).Should().Be(ArchiveLayoutKind.Unknown);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AFileBothStoresHaveOpenedIsReportedAsAmbiguousRatherThanResolved()
    {
        // Exactly what the pre-detection code would produce, and it is unresolvable after the fact:
        // either table may hold the recording somebody is asking for.
        string path = _workspace.File("both.db");
        using (var rows = new SqliteDataLogger(path)) await rows.WriteBatchAsync(OneSample());
        using (var tiered = new TieredTelemetryStore(path)) await tiered.WriteBatchAsync(OneSample());

        ArchiveLayout.Detect(path).Should().Be(ArchiveLayoutKind.Ambiguous);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AskingWhichLayoutAFileHoldsDoesNotChangeIt()
    {
        // The whole reason this is a separate read rather than a property of either store.
        string path = _workspace.File("untouched.db");
        using (var store = new SqliteDataLogger(path)) await store.WriteBatchAsync(OneSample());

        IReadOnlyList<string> before = TablesIn(path);
        ArchiveLayout.Detect(path);

        TablesIn(path).Should().Equal(before);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void AFileThatIsNotADatabaseAtAllRaisesRatherThanReportingALayout()
    {
        string path = _workspace.File("prose.db");
        File.WriteAllText(path, "solid teapot\nthis is not a database\n");

        Action detect = () => ArchiveLayout.Detect(path);

        detect.Should().Throw<SqliteException>();
    }
}
