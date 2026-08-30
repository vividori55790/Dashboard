using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Cluster;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Asks a peer for the interval this host was not connected for.
/// </summary>
/// <remarks>
/// ARCHITECTURE §4's backfill, in the shape this product's exchange actually has. The section is
/// written as though a node buffers locally and pushes when the link returns; the exchange here is
/// pull, so the sender has no memory of who was listening and nothing to push. The receiver asks
/// instead — it knows the interval from <see cref="LinkOutageLedger"/>, and the sender's
/// <c>/api/history</c> answers for a time that has passed.
/// <para>
/// The reply is deserialised into <see cref="HistoryEndpoint.Result"/> — the same type the endpoint
/// serialises — so the two cannot drift the way a second copy of the field names would. That is the
/// pattern <see cref="PeerFrameParser"/> uses for the live stream, applied to the archive.
/// </para>
/// <para>
/// What comes back is turned into peer frames and pushed through the ordinary ingest path rather
/// than injected past it. Everything downstream then applies unchanged: the samples are marked
/// late-arriving because their <c>ObservedAt</c> is old, and they are deduplicated on identity
/// because an archive stores a reading and not the frame that delivered it, so there is no counter
/// to check them by. A fill run twice recovers the same window and admits it once.
/// </para>
/// </remarks>
public static class ArchiveGapFill
{
    /// <summary>How much of a gap this host will pull in one request.</summary>
    /// <remarks>
    /// A four-hour partition against a 20 Hz rig is a quarter of a million samples, and pulling it
    /// is a decision about somebody's bandwidth and memory. Bounded, and an outage past the bound
    /// is reported as <see cref="GapFillOutcome.TooLong"/> rather than as the peer having nothing
    /// -- the two would otherwise be the same empty result and only one is a reason to go looking.
    /// </remarks>
    public static readonly TimeSpan LongestGap = TimeSpan.FromMinutes(15);

    /// <summary>Most samples to accept from one answer.</summary>
    public const int Limit = 20_000;

    /// <summary>Where the peer answers questions about the past, derived from its stream URL.</summary>
    public static Uri HistoryUriFor(Uri streamEndpoint, DateTime fromUtc, DateTime toUtc) =>
        new UriBuilder(streamEndpoint)
        {
            Path = "/api/history",
            Query = $"from={Uri.EscapeDataString(fromUtc.ToString("o"))}"
                  + $"&to={Uri.EscapeDataString(toUtc.ToString("o"))}"
                  + $"&limit={Limit}"
        }.Uri;

    /// <summary>Fetches the gap, and reports what happened either way.</summary>
    /// <returns>The outcome, and the frames to feed into the ingest path.</returns>
    public static async Task<(GapFill Fill, IReadOnlyList<string> Frames)> FetchAsync(
        HttpClient client, Uri streamEndpoint, LinkOutage outage, CancellationToken cancellationToken)
    {
        DateTime from = outage.BeganUtc;
        DateTime to = outage.EndedUtc ?? DateTime.UtcNow;

        if (to - from > LongestGap)
        {
            return (new GapFill(from, to, GapFillOutcome.TooLong, 0, false), Array.Empty<string>());
        }

        HistoryEndpoint.Result? answer;
        try
        {
            answer = await client
                .GetFromJsonAsync<HistoryEndpoint.Result>(
                    HistoryUriFor(streamEndpoint, from, to), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (new GapFill(from, to, GapFillOutcome.Unreachable, 0, false), Array.Empty<string>());
        }

        if (answer is null)
        {
            return (new GapFill(from, to, GapFillOutcome.Unreachable, 0, false), Array.Empty<string>());
        }

        // The endpoint's own words for "I cannot answer for a past time". A peer with no archive
        // and a peer that was quiet are different facts and the operator's next action differs:
        // one means the plant was calm, the other means the peer needs --archive.
        if (!string.Equals(answer.Status, "Success", StringComparison.Ordinal))
        {
            GapFillOutcome outcome = (answer.Reason ?? string.Empty).Contains("no archive", StringComparison.OrdinalIgnoreCase)
                ? GapFillOutcome.SenderHasNoArchive
                : GapFillOutcome.Unreachable;

            return (new GapFill(from, to, outcome, 0, false), Array.Empty<string>());
        }

        var frames = new List<string>(answer.Samples.Count);
        foreach (HistoryEndpoint.Sample sample in answer.Samples)
        {
            frames.Add(FrameFor(sample));
        }

        GapFillOutcome result = frames.Count > 0 ? GapFillOutcome.Filled : GapFillOutcome.NothingThere;
        return (new GapFill(from, to, result, frames.Count, answer.Truncated), frames);
    }

    /// <summary>Rebuilds a peer frame from an archived reading.</summary>
    /// <remarks>
    /// The synthetic mark is read off the node id rather than invented. An archive stores a reading
    /// and not the flag, but <c>SimulatedNodeMarker</c> puts the mark inside the name precisely so
    /// it survives into a recording -- <c>TelemetryFrame</c>'s own summary says that is what the
    /// prefix is for. Reading it back out is using the carrier as designed; defaulting to
    /// <c>false</c> would relabel a simulator's output as measured on the way back in.
    /// <para>
    /// No <c>epoch</c> or <c>seq</c>, because the archive has none to give. That is what the
    /// duplicate filter's identity path is for.
    /// </para>
    /// </remarks>
    private static string FrameFor(HistoryEndpoint.Sample sample) =>
        JsonSerializer.Serialize(new
        {
            timestamp = sample.TimestampIso,
            source = "ARCHIVE_BACKFILL",
            simulated = SimulatedNodeMarker.IsMarked(sample.NodeId),
            nodeId = sample.NodeId,
            variable = sample.Variable,
            value = sample.Value,
            unit = sample.Unit
        });
}
