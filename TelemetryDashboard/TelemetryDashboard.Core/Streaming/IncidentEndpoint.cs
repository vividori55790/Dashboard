using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/incident</c>: what every channel was doing around a given instant.
/// </summary>
/// <remarks>
/// An alert names a time. This turns that time into the run-up to it, across every channel at once,
/// out of the durable archive — so the question can be asked days later and from another machine.
/// <para>
/// The window is deliberately asymmetric, which is <see cref="FailureSnapshotExtractor"/>'s whole
/// point: ten seconds before the failure and two after. What happened <em>before</em> a fault is
/// what explains it, and the short tail shows how the system responded.
/// </para>
/// <para>
/// The instant is supplied rather than discovered. The archive stores measurements and not
/// verdicts — deliberately, since a score kept beside the value it came from disagrees with the
/// detector after any change to it — so this endpoint cannot claim to have found an incident. It
/// answers about a moment somebody else identified, which is what an alert, an event log line or an
/// operator's memory provides.
/// </para>
/// </remarks>
public static class IncidentEndpoint
{
    public const double DefaultLeadSec = 10.0;
    public const double DefaultTrailSec = 2.0;

    /// <summary>Widest run-up this endpoint will assemble in one request.</summary>
    public const double MaximumLeadSec = 3600.0;

    public sealed record ChannelWindow
    {
        public string NodeId { get; init; } = string.Empty;
        public string Variable { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public int Samples { get; init; }

        /// <summary>Lowest and highest value inside the window.</summary>
        public double Minimum { get; init; }
        public double Maximum { get; init; }

        /// <summary>The last value before the instant, or null when the channel was silent before it.</summary>
        /// <remarks>
        /// Null rather than the first value after. A reading taken after the fault does not describe
        /// the state that led to it, and presenting it as though it did is the mistake this whole
        /// window exists to avoid.
        /// </remarks>
        public double? ValueBefore { get; init; }

        public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
        public IReadOnlyList<string> Timestamps { get; init; } = Array.Empty<string>();
    }

    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        public string AtUtc { get; init; } = string.Empty;
        public double LeadSec { get; init; }
        public double TrailSec { get; init; }

        /// <summary>Channels that reported anything inside the window.</summary>
        public int ChannelCount { get; init; }
        public int TotalSamples { get; init; }

        public IReadOnlyList<ChannelWindow> Channels { get; init; } = Array.Empty<ChannelWindow>();
    }

    /// <summary>Assembles the window around <paramref name="atUtc"/>.</summary>
    public static async Task<Result> QueryAsync(
        IDataLogger? store,
        DateTime? atUtc,
        double leadSec,
        double trailSec,
        string? node,
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

        if (atUtc is not DateTime instant)
        {
            return new Result
            {
                Status = "Error",
                Reason = "no instant given; pass ?at=<iso timestamp>, the moment the alert names"
            };
        }

        double lead = Clamp(leadSec, DefaultLeadSec);
        double trail = Clamp(trailSec, DefaultTrailSec);

        // Read the window out of the archive first, then let the extractor cut it. Asking the store
        // for exactly the window means the extractor is trimming a slice rather than scanning a day.
        var filter = new QueryFilter(
            node, null, instant.AddSeconds(-lead), instant.AddSeconds(trail), int.MaxValue);

        IEnumerable<TelemetryPacket> packets =
            await store.QueryAsync(filter, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TelemetryPacket> window =
            new FailureSnapshotExtractor { LeadSeconds = lead, TrailSeconds = trail }
                .Extract10sFailureSnapshot(packets, instant);

        List<ChannelWindow> channels = window
            .GroupBy(p => (p.NodeId, p.Variable))
            .OrderBy(g => g.Key.NodeId, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Variable, StringComparer.Ordinal)
            .Select(g =>
            {
                List<TelemetryPacket> ordered = g.OrderBy(p => p.Timestamp).ToList();
                TelemetryPacket? last = ordered.LastOrDefault(p => p.Timestamp <= instant);

                return new ChannelWindow
                {
                    NodeId = g.Key.NodeId,
                    Variable = g.Key.Variable,
                    Unit = ordered[0].Unit ?? string.Empty,
                    Samples = ordered.Count,
                    Minimum = ordered.Min(p => p.Value),
                    Maximum = ordered.Max(p => p.Value),
                    ValueBefore = last?.Value,
                    Values = ordered.Select(p => p.Value).ToList(),
                    Timestamps = ordered
                        .Select(p => p.Timestamp.ToString("o", CultureInfo.InvariantCulture))
                        .ToList()
                };
            })
            .ToList();

        return new Result
        {
            AtUtc = instant.ToString("o", CultureInfo.InvariantCulture),
            LeadSec = lead,
            TrailSec = trail,
            ChannelCount = channels.Count,
            TotalSamples = window.Count,
            Channels = channels
        };
    }

    private static double Clamp(double requested, double fallback) =>
        double.IsFinite(requested) && requested > 0
            ? Math.Min(requested, MaximumLeadSec)
            : fallback;
}
