using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>The read paths: raw samples by filter, and a time window answered from a tier.</summary>
public sealed partial class TieredTelemetryStore
{
    /// <summary>Reads back raw samples matching <paramref name="filter"/>, oldest first.</summary>
    /// <remarks>
    /// Blocks overlapping the window are decompressed whole and then trimmed to it, so a narrow
    /// query still pays for the blocks it clips. <see cref="TelemetryPacket.RawData"/> comes back
    /// empty — see the note on the type.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="QueryFilter.Limit"/> is negative.</exception>
    public async Task<IEnumerable<TelemetryPacket>> QueryAsync(
        QueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), filter.Limit, "Limit must not be negative.");
        }

        long start = filter.StartTime is { } from ? RollupIntervals.ToUtcTicks(from) : 0L;
        long end = filter.EndTime is { } to ? RollupIntervals.ToUtcTicks(to) : long.MaxValue;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        List<CompressedSampleBlock> blocks =
            TieredStoreReader.ReadBlocks(connection, filter.NodeId, filter.Variable, start, end);

        return blocks
            .SelectMany(block => block.DecodePackets())
            .Where(packet => packet.Timestamp.Ticks >= start && packet.Timestamp.Ticks <= end)
            .OrderBy(packet => packet.Timestamp.Ticks)
            .Take(filter.Limit)
            .ToList();
    }

    /// <summary>
    /// Answers a time window from the coarsest tier that satisfies it, and says which one that was.
    /// </summary>
    /// <remarks>
    /// The tier is chosen by <see cref="TierSelector"/> from the requested resolution, the width of
    /// the window and whether raw samples for it still exist. The answer names the tier it came
    /// from and the width of a point, so an hourly mean cannot be mistaken for a sample: that
    /// distinction is the caller's only defence against reading a summary as a measurement.
    /// <para>
    /// An interval with no measurement simply has no point. Nothing here fills a gap with a zero,
    /// and nothing interpolates across one.
    /// </para>
    /// </remarks>
    public async Task<TieredQueryResult> QueryTieredAsync(
        TieredQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validated();

        long start = RollupIntervals.ToUtcTicks(request.StartUtc);
        long end = RollupIntervals.ToUtcTicks(request.EndUtc);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        long? earliest = TieredStoreReader.EarliestBlockTicks(connection, request.Channel);
        (TelemetryTier tier, string reason) = TierSelector.Select(
            request, earliest is { } ticks ? new DateTime(ticks, DateTimeKind.Utc) : null);

        return tier == TelemetryTier.Raw
            ? RawAnswer(connection, request, start, end, reason)
            : AggregatedAnswer(connection, request, tier, start, end, reason);
    }

    private static TieredQueryResult RawAnswer(
        SqliteConnection connection, TieredQueryRequest request, long start, long end, string reason)
    {
        List<TieredSeriesPoint> points = TieredStoreReader
            .ReadBlocks(connection, request.Channel.NodeId, request.Channel.Variable, start, end)
            .SelectMany(block => block.DecodePoints())
            .Where(point => point.StartUtc.Ticks >= start && point.StartUtc.Ticks <= end)
            .OrderBy(point => point.StartUtc.Ticks)
            .Take(request.MaxPoints + 1)
            .ToList();

        bool truncated = points.Count > request.MaxPoints;
        if (truncated) points.RemoveAt(points.Count - 1);

        return new TieredQueryResult(
            TelemetryTier.Raw, TimeSpan.Zero, reason, points, truncated);
    }

    private static TieredQueryResult AggregatedAnswer(
        SqliteConnection connection,
        TieredQueryRequest request,
        TelemetryTier tier,
        long start,
        long end,
        string reason)
    {
        RollupInterval interval = tier.AsInterval()!.Value;
        List<RollupWindow> windows = TieredStoreReader.ReadWindows(
            connection, request.Channel, interval, start, end, request.MaxPoints + 1);

        bool truncated = windows.Count > request.MaxPoints;
        if (truncated) windows.RemoveAt(windows.Count - 1);

        return new TieredQueryResult(
            tier,
            interval.Duration(),
            reason,
            windows.Select(TieredSeriesPoint.FromWindow).ToList(),
            truncated);
    }
}
