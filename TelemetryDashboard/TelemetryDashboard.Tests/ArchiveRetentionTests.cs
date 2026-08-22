using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Which layout the archive uses, and whether anything is ever actually removed.
/// </summary>
/// <remarks>
/// <c>TieredTelemetryStore</c> shipped with a prune path, a retention log and its own note recording
/// the gap: "Nothing calls this on a timer and nothing calls it at start-up." So an archive with a
/// policy grew exactly as fast as one without, and four partial files of tiering, rollups and
/// retention were all written, all tested and all off.
/// <para>
/// Driven on the running host: a replay archived into a tiered store, then the host restarted with
/// <c>--retain "raw=10s,minute=1d"</c>, printed at start-up
/// "retention removed: 602 raw blocks / 602 samples (…), 0 minute windows" — the raw data gone and
/// the minute rollups kept, which is the entire point of the tiering.
/// </para>
/// </remarks>
public class ArchiveRetentionTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "archive-" + Guid.NewGuid().ToString("N"));

    public ArchiveRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Db(string name) => Path.Combine(_dir, name);

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AnArchiveWithNoPolicyStaysTheRowStoreThatKeepsEverything()
    {
        // Today's behaviour, unchanged, and it has to stay unchanged: the row store is the only one
        // that keeps the original wire text, so an archive that has to show the bytes a device sent
        // is this one whatever it costs.
        await using ArchiveSink? archive = ArchiveSink.Open(Db("rows.db"));

        archive.Should().NotBeNull();
        archive!.Tiered.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task AskingForRetentionChangesTheLayoutAndNotOnlyTheLifetime()
    {
        RetentionSpec.TryParse("raw=7d,minute=90d", out RetentionPolicy policy, out _).Should().BeTrue();

        await using ArchiveSink? archive = ArchiveSink.Open(Db("tiered.db"), policy);

        archive!.Tiered.Should().NotBeNull("only the tiered layout can be pruned");
        archive.Tiered!.Retention.Enabled.Should().BeTrue();
        archive.Tiered.Retention.RawRetention.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ThePruneRemovesRawDataPastTheCutoffAndKeepsTheRollups()
    {
        // The whole bargain of the tiering: an hour of raw samples costs more than a year of minute
        // averages, so the raw goes and the summary stays. A store that dropped both would be a
        // delete with extra steps.
        RetentionSpec.TryParse("raw=1h,minute=365d", out RetentionPolicy policy, out _).Should().BeTrue();
        using var store = new TieredTelemetryStore(Db("prune.db"), policy);

        DateTime old = DateTime.UtcNow.AddHours(-6);
        var batch = new List<TelemetryPacket>();
        for (int i = 0; i < 200; i++)
        {
            batch.Add(new TelemetryPacket("RIG", "rail", 48.0 + i * 0.001, "V", old.AddSeconds(i)));
        }
        await store.WriteBatchAsync(batch);

        RetentionPruneReport report = await store.PruneAsync();

        report.Applied.Should().BeTrue();
        report.RawSamplesRemoved.Should().Be(200);
        report.RollupWindowsRemoved.Values.Sum().Should().Be(0, "a year of minute windows is inside the policy");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task WithoutAPolicyAPruneIsADryRunThatDeletesNothing()
    {
        // The store's own guarantee, and the reason a first run and a run nobody configured behave
        // the same: nothing is destroyed until somebody enabled a policy and asked.
        using var store = new TieredTelemetryStore(Db("dry.db"));

        await store.WriteBatchAsync(new[]
        {
            new TelemetryPacket("RIG", "rail", 48.0, "V", DateTime.UtcNow.AddYears(-5))
        });

        RetentionPruneReport report = await store.PruneAsync();

        report.Applied.Should().BeFalse();
        report.Describe().Should().Contain("dry run");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task DataInsideThePolicySurvivesThePrune()
    {
        RetentionSpec.TryParse("raw=30d", out RetentionPolicy policy, out _).Should().BeTrue();
        using var store = new TieredTelemetryStore(Db("keep.db"), policy);

        await store.WriteBatchAsync(new[]
        {
            new TelemetryPacket("RIG", "rail", 48.0, "V", DateTime.UtcNow.AddMinutes(-5))
        });

        RetentionPruneReport report = await store.PruneAsync();

        report.RawSamplesRemoved.Should().Be(0);
        report.RemovedAnything.Should().BeFalse();
    }
}
