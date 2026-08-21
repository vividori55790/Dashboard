using System;
using System.IO;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// The host's durable archive: every ingested sample, queryable afterwards.
/// </summary>
/// <remarks>
/// The headless host is the cross-platform product — the desktop shell is Windows-only — and it had
/// no durable store at all. It could write a CSV and hold a few minutes in an in-memory ring, so
/// "what did this channel do last Tuesday" had no answer anywhere on Linux or macOS. The shell has
/// had SQLite since M4; this gives the same thing to the half of the product that runs everywhere.
/// <para>
/// A bounded ring in front of a drain, the same shape the shell uses. Writing to SQLite on the
/// ingest thread would put a disk flush between two samples, so a slow disk would show up as a gap
/// in the telemetry rather than as a slow disk. What the ring cannot hold is counted, because a
/// silent gap in an archive is worse than a short one: the gap is discovered months later by
/// someone who assumes the machine was quiet.
/// </para>
/// </remarks>
public sealed class ArchiveSink : IAsyncDisposable
{
    /// <summary>Packets the ring holds while waiting for the drain.</summary>
    /// <remarks>Roughly half a minute at 120 samples a second, which is a slow disk's worth.</remarks>
    public const int RingCapacity = 4_000;

    private readonly SqliteDataLogger _store;
    private readonly ChannelDataLogger _ring;
    private readonly ChannelDataLoggerDrain _drain;

    private ArchiveSink(SqliteDataLogger store, ChannelDataLogger ring, ChannelDataLoggerDrain drain)
    {
        _store = store;
        _ring = ring;
        _drain = drain;
    }

    /// <summary>The database this run is writing into.</summary>
    public string DatabasePath => _store.DatabasePath;

    /// <summary>Samples committed to disk.</summary>
    public long Written => _store.WrittenCount;

    /// <summary>Samples the ring could not hold, and which are therefore not in the archive.</summary>
    public long Dropped => _ring.DroppedCount;

    /// <summary>The store, for reading the archive back.</summary>
    public IDataLogger Store => _store;

    /// <summary>Opens the archive, or returns null when it was not asked for.</summary>
    /// <remarks>
    /// A store that cannot be opened stops the start rather than running without one. An operator
    /// who passed <c>--archive</c> and got a run with no archive would find out when they came
    /// looking for the data, which is the worst possible moment.
    /// </remarks>
    public static ArchiveSink? Open(string? path)
    {
        if (path is null) return null;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var store = new SqliteDataLogger(path);
        var ring = new ChannelDataLogger(RingCapacity);
        var drain = new ChannelDataLoggerDrain(ring, store);
        drain.Start();

        return new ArchiveSink(store, ring, drain);
    }

    /// <summary>Offers one sample. Never blocks and never throws.</summary>
    public void Offer(TelemetryPacket packet) => _ring.TryEnqueue(packet);

    /// <summary>One line for the shutdown report.</summary>
    public string Summary()
    {
        string lost = Dropped > 0
            ? $", {Dropped:N0} dropped before reaching disk"
            : string.Empty;

        return $"archive: {Written:N0} sample(s) in {DatabasePath}{lost}";
    }

    /// <summary>Flushes what the ring still holds, then closes the store.</summary>
    /// <remarks>
    /// Stopping the drain first is what makes the tail of a run survive: the ring holds the last
    /// batch, and closing the database under it would discard exactly the samples closest to
    /// whatever made the operator stop.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try { await _drain.StopAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"telemetry-host: archive did not flush cleanly: {ex.Message}");
        }

        _store.Dispose();
    }
}
