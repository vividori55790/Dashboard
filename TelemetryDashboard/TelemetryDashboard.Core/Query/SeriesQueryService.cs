using System;
using System.Buffers;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Executes screen-shaped queries against the rolling store.
/// </summary>
/// <remarks>
/// The window is extracted into a pooled buffer, reduced, and the buffer returned; the only
/// allocation that survives a call is the reply itself. A query for four channels at 2,000 points
/// costs four pooled rentals, whatever the sample rate behind them.
/// </remarks>
public sealed class SeriesQueryService
{
    private readonly SeriesStore _store;

    public SeriesQueryService(SeriesStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public SeriesStore Store => _store;

    /// <summary>Runs the query, returning at most <c>MaxPoints</c> points per channel.</summary>
    public SeriesQueryResult Execute(SeriesQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var series = new List<ReducedSeries>(request.Channels.Count);
        foreach (string channel in request.Channels)
        {
            series.Add(QueryChannel(channel, request));
        }

        return new SeriesQueryResult(request, series);
    }

    private ReducedSeries QueryChannel(string channel, SeriesQueryRequest request)
    {
        ChannelSeriesBuffer? buffer = _store.Find(channel);
        if (buffer is null) return ReducedSeries.Empty(channel);

        int available = buffer.CountInWindow(request.StartSec, request.EndSec);
        if (available == 0) return ReducedSeries.Empty(channel);

        bool truncated = buffer.RetentionTruncates(request.StartSec);

        SeriesPoint[] window = ArrayPool<SeriesPoint>.Shared.Rent(available);
        try
        {
            // Ingest may append between the count and the copy. Reducing only what was actually
            // copied keeps the reported sample count equal to the samples that were examined,
            // rather than to a count taken a moment earlier.
            int copied = Math.Min(available, buffer.CopyWindow(request.StartSec, request.EndSec, window));

            ReducedSeries reduced = SeriesReducer.Reduce(
                channel, window.AsSpan(0, copied), request.MaxPoints, request.Method);

            return truncated ? WithRetentionFlag(reduced) : reduced;
        }
        finally
        {
            ArrayPool<SeriesPoint>.Shared.Return(window);
        }
    }

    /// <summary>Re-stamps a series to say its window starts where retention did, not where data did.</summary>
    private static ReducedSeries WithRetentionFlag(ReducedSeries series)
    {
        ReductionMetadata source = series.Metadata;
        return new ReducedSeries(series.Channel, series.Points, new ReductionMetadata
        {
            SourceSampleCount = source.SourceSampleCount,
            ReturnedPointCount = source.ReturnedPointCount,
            Method = source.Method,
            PreservesExtremes = source.PreservesExtremes,
            BucketWidthSec = source.BucketWidthSec,
            WindowStartSec = source.WindowStartSec,
            WindowEndSec = source.WindowEndSec,
            SourceMinimum = source.SourceMinimum,
            SourceMaximum = source.SourceMaximum,
            WindowTruncatedByRetention = true
        });
    }
}
