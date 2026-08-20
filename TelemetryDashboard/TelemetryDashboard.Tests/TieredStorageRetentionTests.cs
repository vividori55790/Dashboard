using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Retention: what the store does by default (nothing), what it does when armed, and what it
/// records about it either way.
/// </summary>
public sealed class TieredStorageRetentionTests
{
    private static readonly DateTime Now = new(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChannelKey Channel = new("node-3", "temperature");

    /// <summary>One block per hour, back from <paramref name="hoursAgo"/> to one hour ago.</summary>
    private static async Task Fill(TieredTelemetryStore store, int hoursAgo)
    {
        for (int hour = hoursAgo; hour >= 1; hour--)
        {
            DateTime start = Now.AddHours(-hour);
            await store.WriteBatchAsync(Enumerable.Range(0, 60)
                .Select(i => new TelemetryPacket(
                    Channel.NodeId, Channel.Variable, 20.0 + i * 0.01, "C", start.AddMinutes(i)))
                .ToList());
        }
    }

    private static long CountRows(string path, string table)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static string LastLogDetail(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT detail FROM retention_log ORDER BY id DESC LIMIT 1;";
        return (string?)command.ExecuteScalar() ?? string.Empty;
    }

    [Fact]
    public void TheDefaultPolicyIsDisabled()
    {
        RetentionPolicy.Disabled.Enabled.Should().BeFalse();
        new RetentionPolicy().Enabled.Should().BeFalse(
            "a store nobody configured must not start deleting the recording");
    }

    [Fact]
    public async Task AFirstRunDeletesNothingAndSaysWhatItWouldHaveDeleted()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("retention-default.db");
        using var store = new TieredTelemetryStore(path);
        await Fill(store, hoursAgo: 400); // ~16 days, well past the 7-day default window

        store.Retention.Enabled.Should().BeFalse();
        long blocksBefore = CountRows(path, "raw_block");

        RetentionPruneReport report = await store.PruneAsync(Now);

        report.Applied.Should().BeFalse();
        report.RawBlocksRemoved.Should().BeGreaterThan(0, "the dry run still reports what arming would cost");
        report.OldestRemovedUtc.Should().NotBeNull();
        report.Describe().Should().Contain("dry run");

        CountRows(path, "raw_block").Should().Be(blocksBefore, "a dry run deletes nothing");
        (await store.QueryAsync(new QueryFilter(Limit: 1))).Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnArmedPolicyRemovesOnlyBlocksEntirelyOlderThanTheCutoff()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("retention-armed.db");
        var policy = new RetentionPolicy { Enabled = true, RawRetention = TimeSpan.FromHours(5) };
        using var store = new TieredTelemetryStore(path, policy);
        await Fill(store, hoursAgo: 10);

        long blocksBefore = CountRows(path, "raw_block");
        blocksBefore.Should().Be(10);

        RetentionPruneReport report = await store.PruneAsync(Now);

        report.Applied.Should().BeTrue();
        report.RawCutoffUtc.Should().Be(Now.AddHours(-5));
        report.RawBlocksRemoved.Should().Be(5, "blocks ending before the cutoff go; the rest stay whole");
        report.RawSamplesRemoved.Should().Be(300);
        report.OldestRemovedUtc.Should().Be(Now.AddHours(-10));

        CountRows(path, "raw_block").Should().Be(5);
        List<TelemetryPacket> survivors = (await store.QueryAsync(new QueryFilter(Limit: 10_000))).ToList();
        survivors.Should().HaveCount(300);
        survivors.Min(p => p.Timestamp).Should().BeOnOrAfter(Now.AddHours(-5));
    }

    [Fact]
    public async Task EveryPruneIsRecordedInTheDatabase()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("retention-log.db");
        using var store = new TieredTelemetryStore(path);
        await Fill(store, hoursAgo: 300);

        await store.PruneAsync(Now);
        CountRows(path, "retention_log").Should().Be(1, "a dry run is logged too");
        LastLogDetail(path).Should().Contain("would remove");

        using var armed = new TieredTelemetryStore(
            path, new RetentionPolicy { Enabled = true, RawRetention = TimeSpan.FromDays(1) });
        RetentionPruneReport applied = await armed.PruneAsync(Now);

        CountRows(path, "retention_log").Should().Be(2);
        string detail = LastLogDetail(path);
        detail.Should().Contain("removed");
        detail.Should().Contain(applied.RawSamplesRemoved.ToString());
    }

    [Fact]
    public async Task PruningRaisesTheReportToWhoeverIsListening()
    {
        using var workspace = new TempWorkspace();
        using var store = new TieredTelemetryStore(workspace.File("retention-event.db"));
        await Fill(store, hoursAgo: 50);

        RetentionPruneReport? observed = null;
        store.Pruned += (_, report) => observed = report;
        await store.PruneAsync(Now);

        observed.Should().NotBeNull();
        observed!.Applied.Should().BeFalse();
    }

    [Fact]
    public async Task RollupsOutliveTheRawSamplesAndTheQuerySaysSo()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("retention-fallback.db");
        using var store = new TieredTelemetryStore(
            path, new RetentionPolicy { Enabled = true, RawRetention = TimeSpan.FromHours(5) });
        await Fill(store, hoursAgo: 10);
        await store.PruneAsync(Now);

        // The window is entirely inside the pruned span; raw samples for it no longer exist.
        var request = new TieredQueryRequest(Channel, Now.AddHours(-10), Now.AddHours(-8));
        TieredQueryResult result = await store.QueryTieredAsync(request);

        // Minute, not Second: the raw samples are gone AND two hours at one-second buckets is
        // 7,200 points for a chart that asked for far fewer. Both happened, and the answer has to
        // say both -- the pruning is a permanent loss, the coarsening is only a display decision.
        result.Tier.Should().Be(TelemetryTier.Minute, "raw was pruned and the window was coarsened to fit");
        result.IsRaw.Should().BeFalse();
        result.TierReason.Should().Contain("raw samples start at")
              .And.Contain("more than", "losing the pruning notice would hide a permanent data loss");
        result.Points.Should().NotBeEmpty("the rollups are what survived");
        // 121, not 120: this store treats a window as inclusive at both ends, so a two-hour span
        // of one-per-minute samples returns the buckets at both boundaries. Worth knowing when
        // summing adjacent windows, because the shared boundary is counted by each of them.
        result.Points.Sum(p => p.Count).Should().Be(121, "the window includes both endpoints");
    }

    [Fact]
    public async Task RollupTiersCanBeGivenTheirOwnWindows()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("retention-tiers.db");
        using var store = new TieredTelemetryStore(path, new RetentionPolicy
        {
            Enabled = true,
            RawRetention = TimeSpan.FromHours(2),
            SecondRetention = TimeSpan.FromHours(4),
            MinuteRetention = TimeSpan.FromHours(6)
        });
        await Fill(store, hoursAgo: 12);

        RetentionPruneReport report = await store.PruneAsync(Now);

        report.RollupWindowsRemoved.Should().ContainKey(RollupInterval.Second);
        report.RollupWindowsRemoved[RollupInterval.Second].Should().BeGreaterThan(0);
        report.RollupWindowsRemoved.Should().NotContainKey(
            RollupInterval.Hour, "a tier with no stated window is kept indefinitely");

        // The hour tier still covers the whole recording after everything finer was pruned.
        TieredQueryResult hourly = await store.QueryTieredAsync(new TieredQueryRequest(
            Channel, Now.AddHours(-12), Now, TimeSpan.FromHours(1)));
        hourly.Tier.Should().Be(TelemetryTier.Hour);
        hourly.Points.Sum(p => p.Count).Should().Be(720, "twelve hours of one-per-minute samples");
    }

    [Fact]
    public void ANonPositiveRetentionWindowIsRefused()
    {
        Action zero = () => _ = new TieredTelemetryStore(
            "unused.db", new RetentionPolicy { Enabled = true, RawRetention = TimeSpan.Zero });

        zero.Should().Throw<ArgumentOutOfRangeException>(
            "a zero window means delete everything, which has to be said out loud");
    }
}
