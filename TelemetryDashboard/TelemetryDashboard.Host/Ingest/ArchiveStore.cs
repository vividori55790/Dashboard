using System;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Storage;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Which store an archive is, and how to ask it what it has done.
/// </summary>
/// <remarks>
/// Two layouts with the same interface and different counters, so the choice and the two ways of
/// reading a total live together rather than being spread through the sink. Picking one is a real
/// decision and not a tuning knob: the row store keeps every sample and its original wire text
/// forever, the tiered store keeps compressed blocks and rollups and can be pruned, and only one of
/// them can answer "show me the bytes that device sent".
/// </remarks>
internal sealed record ArchiveStore(IDataLogger Logger, Func<string> Path, Func<long> Written)
{
    /// <summary>The tiered store, or null when this archive is the row store.</summary>
    public TieredTelemetryStore? Tiered => Logger as TieredTelemetryStore;

    /// <summary>Opens the layout <paramref name="retention"/> calls for.</summary>
    public static ArchiveStore Open(string path, RetentionPolicy retention)
    {
        if (!retention.Enabled)
        {
            var rows = new SqliteDataLogger(path);
            return new ArchiveStore(rows, () => rows.DatabasePath, () => rows.WrittenCount);
        }

        // One row per batch per channel instead of one per sample, with rollups maintained as data
        // arrives -- which is what makes keeping a year of minute averages cost less than a week of
        // raw rows, and is the only layout here a prune can act on.
        var tiered = new TieredTelemetryStore(path, retention);
        return new ArchiveStore(tiered, () => tiered.DatabasePath, () => tiered.WrittenSampleCount);
    }
}
