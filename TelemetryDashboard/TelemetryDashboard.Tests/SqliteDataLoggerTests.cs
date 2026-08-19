using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Round-trip, atomicity and failure-surfacing tests for the only durable <see cref="IDataLogger"/>.
/// </summary>
/// <remarks>
/// Telemetry that scrolls out of the in-memory ring is written here or lost, and until these tests
/// existed nothing in the repository executed that path — the store was verified once by a harness
/// that was then deleted, so any regression in it would have shipped green. Every test below drives
/// the real <see cref="SqliteDataLogger"/> against a real file; none of them assert against a stub.
/// </remarks>
public sealed class SqliteDataLoggerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private SqliteDataLogger NewLogger(string name = "telemetry.db") =>
        new(_workspace.File(name));

    [Fact]
    [Trait("Category", "Storage")]
    public async Task WriteAsync_RoundTrip_PreservesAllSevenFieldsAndFullTickPrecision()
    {
        using SqliteDataLogger logger = NewLogger();
        // Sub-millisecond ticks: a store that persisted milliseconds, or a DATETIME text column,
        // would round these away and still look correct at second resolution.
        var timestamp = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_567);
        var packet = new TelemetryPacket
        {
            Timestamp = timestamp,
            NodeId = "DAB_CONVERTER",
            Variable = "bus_voltage",
            Value = -273.15009765625,
            Unit = "V",
            RawData = "$V,-273.15*7F",
            Flags = PacketFlags.IsDerived | PacketFlags.AlarmExceeded
        };

        await logger.WriteAsync(packet);
        TelemetryPacket stored = (await logger.QueryAsync(new QueryFilter())).Single();

        stored.Timestamp.Ticks.Should().Be(timestamp.Ticks);
        stored.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
        stored.NodeId.Should().Be("DAB_CONVERTER");
        stored.Variable.Should().Be("bus_voltage");
        stored.Value.Should().Be(-273.15009765625);
        stored.Unit.Should().Be("V");
        stored.RawData.Should().Be("$V,-273.15*7F");
        stored.Flags.Should().Be(PacketFlags.IsDerived | PacketFlags.AlarmExceeded);
        logger.WrittenCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task WriteBatchAsync_BadElementMidBatch_RollsBackTheWholeBatch()
    {
        using SqliteDataLogger logger = NewLogger();
        await logger.WriteBatchAsync(Series("BASE", 3));
        long baseline = logger.WrittenCount;

        var withHole = new[]
        {
            new TelemetryPacket("N", "a", 1, "u", At(10)),
            null!,
            new TelemetryPacket("N", "b", 2, "u", At(11))
        };

        Func<Task> write = () => logger.WriteBatchAsync(withHole);

        await write.Should().ThrowAsync<ArgumentException>();
        // The element before the hole was already inserted inside the transaction. Surviving it
        // would leave a prefix that reads back as a complete recording.
        (await logger.QueryAsync(new QueryFilter(Limit: 1000))).Should().HaveCount(3);
        logger.WrittenCount.Should().Be(baseline);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task WriteBatchAsync_CancelledMidBatch_CommitsNothing()
    {
        using SqliteDataLogger logger = NewLogger();
        using var cts = new CancellationTokenSource();

        // Cancellation is triggered by the enumeration itself, not by a timer, so the batch is
        // always cut at the same element.
        IEnumerable<TelemetryPacket> packets = Enumerable.Range(0, 50).Select(i =>
        {
            if (i == 20) cts.Cancel();
            return new TelemetryPacket("N", "v", i, "u", At(i));
        });

        Func<Task> write = () => logger.WriteBatchAsync(packets, cts.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
        (await logger.QueryAsync(new QueryFilter(Limit: 1000))).Should().BeEmpty();
        logger.WrittenCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task WriteAsync_NonFiniteReading_ReadsBackAsNaNRatherThanZero()
    {
        using SqliteDataLogger logger = NewLogger();

        // Guard, not decoration: binding NaN straight at a REAL parameter throws in
        // Microsoft.Data.Sqlite, and the same shape of value once made a disconnected sensor take
        // an entire batch down with it. NaN must survive as NaN — zero would be plotted as a real
        // measurement from a sensor that had actually stopped reporting.
        await logger.WriteAsync(new TelemetryPacket("SENSOR", "temp", double.NaN, "C", At(1)));

        TelemetryPacket stored = (await logger.QueryAsync(new QueryFilter())).Single();

        double.IsNaN(stored.Value).Should().BeTrue();
        stored.Value.Should().NotBe(0.0);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public void RawNaNBinding_WithoutTheSchemaGuard_IsRejectedByTheDriver()
    {
        // Pins the reason the guard exists. If a future driver started accepting NaN this test
        // fails, and the guard can be reconsidered on evidence instead of on a comment.
        using var connection = new SqliteConnection($"Data Source={_workspace.File("raw.db")};Pooling=False");
        connection.Open();
        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE t(v REAL NULL);";
        create.ExecuteNonQuery();

        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO t(v) VALUES ($v);";
        insert.Parameters.Add("$v", SqliteType.Real).Value = double.NaN;

        Action bind = () => insert.ExecuteNonQuery();

        bind.Should().Throw<InvalidOperationException>().WithMessage("*NaN*");
    }

    internal static DateTime At(int seconds) =>
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

    internal static IEnumerable<TelemetryPacket> Series(string nodeId, int count) =>
        Enumerable.Range(0, count).Select(i => new TelemetryPacket(nodeId, "v", i, "u", At(i)));
}
