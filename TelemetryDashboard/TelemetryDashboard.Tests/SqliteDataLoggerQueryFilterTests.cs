using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Every <see cref="QueryFilter"/> member, alone and combined, against the real store.
/// </summary>
/// <remarks>
/// <c>SqliteTelemetryQuery</c> builds its WHERE clause from only the members the caller set, so
/// each member is a separate branch and a filter that quietly stopped constraining would still
/// return plausible rows. The fixture holds five packets whose values identify them uniquely, which
/// makes every expectation below an exact sequence rather than a count.
/// </remarks>
public sealed class SqliteDataLoggerQueryFilterTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTime T0 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TempWorkspace _workspace = new();
    private SqliteDataLogger _logger = null!;

    /// <summary>Loads the fixture once per test, deliberately out of timestamp order.</summary>
    /// <remarks>
    /// Inserted shuffled so that "ordered by timestamp ascending" cannot be satisfied by accident
    /// through insertion order.
    /// </remarks>
    public async Task InitializeAsync()
    {
        _logger = new SqliteDataLogger(_workspace.File("filter.db"));
        await _logger.WriteBatchAsync(new[]
        {
            new TelemetryPacket("B", "vib", 4, "g", T0.AddSeconds(3)),
            new TelemetryPacket("A", "temp", 1, "C", T0),
            new TelemetryPacket("A", "temp", 5, "C", T0.AddSeconds(4)),
            new TelemetryPacket("B", "temp", 3, "C", T0.AddSeconds(2)),
            new TelemetryPacket("A", "vib", 2, "g", T0.AddSeconds(1))
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _logger?.Dispose();
        _workspace.Dispose();
    }

    private async Task<double[]> Values(QueryFilter filter) =>
        (await _logger.QueryAsync(filter)).Select(p => p.Value).ToArray();

    [Fact]
    [Trait("Category", "Storage")]
    public async Task NullMembers_ImposeNoConstraint_AndResultsAreTimestampAscending()
    {
        (await Values(new QueryFilter())).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task NodeIdAlone_SelectsThatNodeOnly()
    {
        (await Values(new QueryFilter(NodeId: "A"))).Should().Equal(1, 2, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task VariableAlone_SelectsThatChannelAcrossNodes()
    {
        (await Values(new QueryFilter(Variable: "temp"))).Should().Equal(1, 3, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task StartTimeAlone_IsInclusiveOfItsOwnInstant()
    {
        (await Values(new QueryFilter(StartTime: T0.AddSeconds(2)))).Should().Equal(3, 4, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task EndTimeAlone_IsInclusiveOfItsOwnInstant()
    {
        (await Values(new QueryFilter(EndTime: T0.AddSeconds(2)))).Should().Equal(1, 2, 3);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task LimitAlone_TakesTheOldestRowsNotAnArbitrarySlice()
    {
        (await Values(new QueryFilter(Limit: 2))).Should().Equal(1, 2);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task NodeAndVariableCombined_NarrowToTheIntersection()
    {
        (await Values(new QueryFilter(NodeId: "A", Variable: "temp"))).Should().Equal(1, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task AllFourMembersCombined_ApplyTogether()
    {
        var filter = new QueryFilter(
            NodeId: "A", Variable: "temp",
            StartTime: T0.AddSeconds(1), EndTime: T0.AddSeconds(4), Limit: 5);

        // Packet 1 is excluded by StartTime, packets 2/3/4 by node or channel.
        (await Values(filter)).Should().Equal(5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task EmptyStringMembers_ImposeNoConstraint()
    {
        (await Values(new QueryFilter(NodeId: string.Empty, Variable: string.Empty)))
            .Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task IdentifierComparisonIsCaseSensitive()
    {
        // Firmware emits exact tokens, and a NOCASE comparison would also cost the index. Stated as
        // a test so the choice is visible rather than inferred from an empty result somewhere.
        (await Values(new QueryFilter(NodeId: "a"))).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task WindowThatMatchesNothing_ReturnsEmptyRatherThanEverything()
    {
        (await Values(new QueryFilter(StartTime: T0.AddDays(1)))).Should().BeEmpty();
    }
}
