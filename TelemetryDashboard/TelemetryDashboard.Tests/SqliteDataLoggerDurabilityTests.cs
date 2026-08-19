using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// How the durable store treats an ambiguous timestamp and a file that is not a database.
/// </summary>
/// <remarks>
/// Both properties are invisible in normal operation and only surface as damage: a timestamp
/// silently shifted by the machine's offset makes one archive index differently in Seoul than in
/// Frankfurt, and a corrupt file silently replaced destroys the history the operator opened the
/// application to read.
/// </remarks>
public sealed class SqliteDataLoggerDurabilityTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    [Trait("Category", "Storage")]
    public async Task UnspecifiedKindTimestamp_IsStoredAsAlreadyUtc_NotShiftedByTheMachineOffset()
    {
        using var logger = new SqliteDataLogger(_workspace.File("kind.db"));
        var unspecified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(unspecified);

        await logger.WriteAsync(new TelemetryPacket("N", "v", 1, "u", unspecified));
        TelemetryPacket stored = (await logger.QueryAsync(new QueryFilter())).Single();

        stored.Timestamp.Ticks.Should().Be(
            unspecified.Ticks,
            "an unspecified kind states no zone, and inventing the machine's ({0}) makes the same "
            + "archive index differently on differently configured machines", offset);
        stored.Timestamp.Kind.Should().Be(DateTimeKind.Utc);

        // The equality above only discriminates where the local offset is non-zero — on a UTC host
        // the shifted and unshifted values coincide. Assert the difference explicitly where the
        // machine can show it, rather than letting a UTC agent report a pass it never earned.
        if (offset != TimeSpan.Zero)
        {
            stored.Timestamp.Ticks.Should().NotBe(unspecified.ToUniversalTime().Ticks);
        }
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task LocalKindTimestamp_IsConvertedToUtc()
    {
        using var logger = new SqliteDataLogger(_workspace.File("local.db"));
        var local = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);

        await logger.WriteAsync(new TelemetryPacket("N", "v", 1, "u", local));
        TelemetryPacket stored = (await logger.QueryAsync(new QueryFilter())).Single();

        // The counterpart to the test above: the store distinguishes the kinds rather than simply
        // never converting anything.
        stored.Timestamp.Ticks.Should().Be(local.ToUniversalTime().Ticks);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void CorruptDatabaseFile_ThrowsAndLeavesTheFileByteIntact()
    {
        string path = _workspace.File("corrupt.db");
        byte[] original = Encoding.ASCII.GetBytes("SQLite format 3 -- except it very much is not.");
        File.WriteAllBytes(path, original);

        Action open = () =>
        {
            using var logger = new SqliteDataLogger(path);
        };

        open.Should().Throw<SqliteException>().WithMessage("*not a database*");
        // Recreating the file would look like a clean start and would have destroyed whatever the
        // operator was trying to recover.
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task CorruptionAfterAGoodWrite_SurfacesOnReopenWithTheDamageIntact()
    {
        string path = _workspace.File("was-good.db");
        using (var logger = new SqliteDataLogger(path))
        {
            await logger.WriteAsync(new TelemetryPacket("N", "v", 1, "u", SqliteDataLoggerTests.At(0)));
        }

        byte[] damaged = Encoding.ASCII.GetBytes("NOT A DATABASE");
        File.WriteAllBytes(path, damaged);

        Action reopen = () =>
        {
            using var logger = new SqliteDataLogger(path);
        };

        reopen.Should().Throw<SqliteException>();
        File.ReadAllBytes(path).Should().Equal(damaged);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task QueryAsync_NegativeLimit_IsRejectedRatherThanTreatedAsUnbounded()
    {
        using var logger = new SqliteDataLogger(_workspace.File("limit.db"));

        Func<Task> query = () => logger.QueryAsync(new QueryFilter(Limit: -1));

        await query.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
