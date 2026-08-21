using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/history</c>: samples read back out of the durable archive.
/// </summary>
/// <remarks>
/// The in-memory series store holds the last few minutes and the DVR a little more. Neither
/// survives a restart, so before the archive existed the answer to "what did this channel do last
/// Tuesday" was that the host had not kept it.
/// <para>
/// This is the only endpoint that reads a store rather than the live stream, so it is the only one
/// that can be asked about a time the host was not running for.
/// </para>
/// </remarks>
public static class HistoryEndpoint
{
    /// <summary>Most samples one request may return.</summary>
    /// <remarks>
    /// Bounded because a query with no window over a month of archive would materialise the lot
    /// into memory and then into a JSON string. A caller that wants more asks for a narrower window;
    /// the response says whether it was truncated, so nobody reads a capped answer as a complete one.
    /// </remarks>
    public const int MaximumLimit = 50_000;

    /// <summary>Default window when none is given.</summary>
    public const double DefaultWindowSec = 300.0;

    public sealed record Sample(string NodeId, string Variable, double Value, string Unit, string TimestampIso);

    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        public string? Node { get; init; }
        public string? Variable { get; init; }
        public string FromUtc { get; init; } = string.Empty;
        public string ToUtc { get; init; } = string.Empty;

        public int Count { get; init; }

        /// <summary>True when the answer stopped at the limit rather than at the end of the data.</summary>
        /// <remarks>
        /// A truncated answer and a complete one look identical from the outside, and a reader who
        /// cannot tell them apart will conclude the machine went quiet at whatever moment the cap
        /// happened to fall.
        /// </remarks>
        public bool Truncated { get; init; }

        public int Limit { get; init; }

        public IReadOnlyList<Sample> Samples { get; init; } = Array.Empty<Sample>();
    }

    /// <summary>Reads the archive, or explains why it cannot.</summary>
    public static async Task<Result> QueryAsync(
        IDataLogger? store,
        string? node,
        string? variable,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (store is null)
        {
            return new Result
            {
                Status = "Error",
                Reason = "this host has no archive; start it with --archive <file> to keep one"
            };
        }

        DateTime to = toUtc ?? DateTime.UtcNow;
        DateTime from = fromUtc ?? to.AddSeconds(-DefaultWindowSec);

        if (from >= to)
        {
            return new Result
            {
                Status = "Error",
                Reason = "the window ends before it starts; check ?from= and ?to="
            };
        }

        int capped = limit <= 0 ? MaximumLimit : Math.Min(limit, MaximumLimit);

        // One more than asked for, so a full page can be reported as truncated rather than guessed
        // at by comparing the count against the limit.
        var filter = new QueryFilter(node, variable, from, to, capped + 1);
        IEnumerable<TelemetryPacket> packets =
            await store.QueryAsync(filter, cancellationToken).ConfigureAwait(false);

        List<TelemetryPacket> found = packets.ToList();
        bool truncated = found.Count > capped;
        if (truncated) found.RemoveRange(capped, found.Count - capped);

        return new Result
        {
            Node = node,
            Variable = variable,
            FromUtc = from.ToString("o", CultureInfo.InvariantCulture),
            ToUtc = to.ToString("o", CultureInfo.InvariantCulture),
            Count = found.Count,
            Truncated = truncated,
            Limit = capped,
            Samples = found.Select(p => new Sample(
                p.NodeId,
                p.Variable,
                p.Value,
                p.Unit ?? string.Empty,
                p.Timestamp.ToString("o", CultureInfo.InvariantCulture))).ToList()
        };
    }

    /// <summary>Parses an ISO timestamp as UTC, or null when absent or unreadable.</summary>
    /// <remarks>
    /// <c>AssumeUniversal</c> so a timestamp written without an offset is read as UTC rather than
    /// as the server's local time — the same reasoning the archive schema applies when storing one.
    /// A caller in Seoul asking about 09:00 and a server in UTC answering about 09:00 local would
    /// otherwise disagree by nine hours and both look right.
    /// <para>
    /// This was <c>RoundtripKind | AdjustToUniversal</c>, which .NET rejects outright — and rejects
    /// before it looks at the input, so it threw even when no timestamp was given at all. Every
    /// request to this endpoint failed, and the failure hung rather than answering, because the
    /// server was dropping unexpected exceptions into an unobserved task.
    /// </para>
    /// </remarks>
    public static DateTime? ReadTimestamp(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed
            : null;
}
