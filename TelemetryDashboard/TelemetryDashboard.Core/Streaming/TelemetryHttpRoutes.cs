using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// HTTP routing for the embedded console: status, DVR replay, incident report, and static assets.
/// </summary>
public static class TelemetryHttpRoutes
{
    public static async Task HandleAsync(
        HttpListenerContext context,
        string path,
        TelemetryStreamingServer server,
        StaticContentHost content,
        string fallbackHtmlPath)
    {
        HttpListenerResponse response = context.Response;
        response.AddHeader("Access-Control-Allow-Origin", "*");

        switch (path.ToLowerInvariant())
        {
            case "/api/status":
                await WriteJsonAsync(response, BuildStatus(server)).ConfigureAwait(false);
                return;

            case "/api/dvr/replay":
                await WriteJsonAsync(response, BuildReplay(server, context.Request.QueryString)).ConfigureAwait(false);
                return;

            case "/api/dvr/report":
                await WriteJsonAsync(response, BuildReport(server, context.Request.QueryString)).ConfigureAwait(false);
                return;

            case "/api/incident":
                await WriteJsonAsync(response, await IncidentEndpoint.QueryAsync(
                    server.Archive,
                    HistoryEndpoint.ReadTimestamp(context.Request.QueryString["at"]),
                    ReadDouble(context.Request.QueryString["lead"], IncidentEndpoint.DefaultLeadSec),
                    ReadDouble(context.Request.QueryString["trail"], IncidentEndpoint.DefaultTrailSec),
                    context.Request.QueryString["node"]).ConfigureAwait(false)
                    ).ConfigureAwait(false);
                return;

            case "/api/history":
                await WriteJsonAsync(response, await HistoryEndpoint.QueryAsync(
                    server.Archive,
                    context.Request.QueryString["node"],
                    context.Request.QueryString["channel"] ?? context.Request.QueryString["variable"],
                    HistoryEndpoint.ReadTimestamp(context.Request.QueryString["from"]),
                    HistoryEndpoint.ReadTimestamp(context.Request.QueryString["to"]),
                    (int)ReadDouble(context.Request.QueryString["limit"], 0)).ConfigureAwait(false)
                    ).ConfigureAwait(false);
                return;

            case "/api/aligned":
                await WriteJsonAsync(response, AlignedEndpoint.Compute(
                    server.Series,
                    (context.Request.QueryString["channels"] ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    // Relative by default: a browser rarely knows the server's clock, and "two
                    // seconds ago" is the question people actually ask.
                    ReadNullableDouble(context.Request.QueryString["at"])
                        ?? SeriesClock.UtcNowSec() - ReadDouble(context.Request.QueryString["ago"], 1.0),
                    ReadDouble(context.Request.QueryString["windowSec"], AlignedEndpoint.DefaultWindowSec))
                    ).ConfigureAwait(false);
                return;

            case "/api/computed":
                await WriteJsonAsync(response, ComputedEndpoint.Compute(
                    server.Series,
                    server.Computed,
                    ReadNullableDouble(context.Request.QueryString["at"])
                        ?? SeriesClock.UtcNowSec() - ReadDouble(context.Request.QueryString["ago"], 1.0),
                    ReadDouble(context.Request.QueryString["windowSec"], AlignedEndpoint.DefaultWindowSec),
                    (context.Request.QueryString["ids"] ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    ).ConfigureAwait(false);
                return;

            // POST to change, GET to see what may be changed. A GET that moved a setpoint would
            // let a link, a prefetch or a browser's history do it.
            case "/api/control":
                await WriteJsonAsync(response,
                    string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
                        ? ControlEndpoint.Apply(server.Control, context.Request.QueryString)
                        : ControlEndpoint.Describe(server.Control)).ConfigureAwait(false);
                return;

            case "/api/limits":
                await WriteJsonAsync(response, LimitsEndpoint.Query(server.Limits)).ConfigureAwait(false);
                return;

            case "/api/inputs":
                await WriteJsonAsync(response, InputsEndpoint.Query(
                    server.Inputs, DateTimeOffset.UtcNow)).ConfigureAwait(false);
                return;

            case "/api/spectrum":
                await WriteJsonAsync(response, SpectrumEndpoint.Compute(
                    server.Series,
                    context.Request.QueryString["channel"] ?? string.Empty,
                    ReadDouble(context.Request.QueryString["windowSec"], SpectrumEndpoint.DefaultWindowSec),
                    SeriesClock.UtcNowSec())).ConfigureAwait(false);
                return;

            case "/api/series":
                await WriteSeriesAsync(response, server, context.Request.QueryString).ConfigureAwait(false);
                return;
        }

        await ServeStaticAsync(response, path, content, fallbackHtmlPath).ConfigureAwait(false);
    }

    private static object BuildStatus(TelemetryStreamingServer server) => new
    {
        server = "TelemetryStreamingServer",
        status = server.IsRunning ? "Running" : "Stopped",
        port = server.Port,
        connectedClients = server.ConnectedClientCount,

        // The ceiling and what it has turned away. A cap that refuses clients and says nothing
        // looks, from outside, exactly like a network dropping connections -- so an operator
        // wondering why a second browser will not connect has the answer on the endpoint they
        // already check, with the number they would need in order to raise it.
        maxStreamClients = server.MaxStreamClients,
        refusedConnections = server.RefusedConnections,
        totalPackets = server.TotalPacketsBroadcasted,
        dvrFrames = server.DvrPlayer.FrameCount,
        dvrDurationSec = server.DvrPlayer.MaxDurationSec,

        // The display path's real cost, separated from ingest. subscribedClients is how many
        // viewers are being served a reduction rather than the raw feed, and reducedPointsSent is
        // what those viewers actually cost the wire.
        seriesChannels = server.Series.ChannelCount,
        seriesSamplesAccepted = server.Series.SamplesAccepted,
        seriesSamplesRefused = server.Series.SamplesRefused,
        subscribedClients = server.SubscribedClientCount,
        reducedFramesSent = server.ReducedFramesSent,
        reducedPointsSent = server.ReducedPointsSent,
        // Null when no limits are declared, for the same reason: a quiet alarm list on an
        // unprotected host and one on a healthy host are not the same fact.
        limits = server.Limits is { } limits
            ? new { declared = limits.Rules.Count, breached = limits.AnyBreached }
            : null,

        // Null when nothing is computing, so a reader can tell "no derived channels configured"
        // from "configured and publishing nothing".
        computed = server.ComputedCounters is { } counters
            ? new
            {
                declared = server.Computed.Count,
                published = counters.Published,
                withheld = counters.Withheld,
                faulted = counters.Faulted,
                fault = counters.FaultMessage
            }
            : null,
        // Null when no ledger is attached, so "nobody is tracking the fleet" reads differently
        // from "the fleet is complete". complete=false with an empty missing list is impossible.
        coverage = server.Coverage?.Invoke() is { } fleet
            ? new
            {
                summary = fleet.Describe(),
                complete = fleet.IsComplete,
                expected = fleet.Nodes.Count,
                reporting = fleet.Reporting.Count,
                silenceThresholdSec = fleet.SilenceThreshold.TotalSeconds,
                missing = fleet.Missing.Select(node => new
                {
                    node = node.NodeId,
                    presence = node.Presence.ToString(),
                    lastHeard = node.LastHeard?.UtcDateTime,
                    stalenessSec = node.Staleness?.TotalSeconds,
                    samples = node.Samples
                }).ToArray()
            }
            : null,

        // Who can reach this and what protects them, answered by the socket rather than by the
        // documentation. An operator asking "did I actually open this to the bench, and is the
        // password I type into it readable on the way" has both answers on the endpoint they are
        // already polling -- and a reader elsewhere in the fleet can tell an exposed hub from a
        // loopback one without being told.
        reachability = new
        {
            scope = server.IsNetworkReachable ? "network" : "loopback",
            prefixes = server.BoundPrefixes,
            authenticated = server.Access is not null,

            // False on every binding this product can construct today. Kept as a measured field
            // rather than dropped, because the honest reading of "authenticated over a cleartext
            // link" is that the credential is only as private as the segment, and a consumer that
            // cannot see this field would have to assume the better of the two.
            encrypted = server.IsLinkEncrypted
        },

        // Null when nobody is comparing clocks, empty when somebody is and no sample has carried
        // one. ARCHITECTURE §3's whole argument is that an offset without an error bar is a point
        // estimate read as a guarantee, so spreadSec travels with every offset and is null rather
        // than zero when a single observation cannot supply one.
        clocks = server.Clocks?.Invoke() is { } observed
            ? new
            {
                nodes = observed.Count,
                perNode = observed.Select(clock => new
                {
                    node = clock.NodeId,
                    offsetSec = clock.Offset.OffsetSec,
                    spreadSec = clock.Offset.SpreadSec,
                    samples = clock.Offset.Samples,

                    // The spread measures how much transit varied; one-way messages never separate
                    // transit from the offset itself, so this is a floor under the uncertainty and
                    // a consumer that reads spreadSec as the whole of it will order events it
                    // cannot order.
                    uncertaintyIsALowerBound = true,
                    summary = clock.Offset.Describe()
                }).ToArray()
            }
            : null,

        // unsequenced is the field that keeps duplicates readable. A link whose sender stamps no
        // sequence can never report a duplicate, and zero there would otherwise read as a clean
        // link rather than as an unwatched one.
        exchange = server.Duplicates is { } filter
            ? new
            {
                admitted = filter.Admitted,
                duplicatesRefused = filter.Duplicates,
                unsequenced = filter.Unsequenced,
                trackedSenders = filter.TrackedSenders,
                senderEvictions = filter.SenderEvictions
            }
            : null,

        // Null when there is no upstream at all. Present with outages = 0 means there is one and
        // it has never dropped -- much better news, and a different claim.
        link = server.Link is { } outages
            ? new
            {
                down = outages.IsDown,
                outages = outages.Count,
                totalDownSec = outages.Total.TotalSeconds,

                // The intervals, not just the tally. Four reconnections in a minute and one
                // four-hour gap give the same count, and only the second puts a hole in a chart.
                recent = outages.Recent().Select(gap => new
                {
                    fromUtc = gap.BeganUtc,
                    toUtc = gap.EndedUtc,
                    seconds = gap.Duration(DateTime.UtcNow).TotalSeconds,
                    open = gap.Open,
                    fault = gap.Fault
                }).ToArray()
            }
            : null,
        endpoints = TelemetryStreamingServer.AdvertisedEndpoints
    };

    /// <summary>
    /// Screen-shaped series query: <c>?channels=a,b&amp;windowSec=60&amp;maxPoints=2000&amp;reduction=minmax</c>.
    /// </summary>
    /// <remarks>
    /// The reply is the same shape the WebSocket pump sends, metadata included, so a consumer
    /// polling over HTTP and a consumer subscribed over a socket read identical guarantees about
    /// what they were given.
    /// </remarks>
    private static async Task WriteSeriesAsync(
        HttpListenerResponse response,
        TelemetryStreamingServer server,
        System.Collections.Specialized.NameValueCollection query)
    {
        // Refused rather than answered. SeriesRequest carries the account of why, and of how it
        // was found: a query for a channel that had 292 samples at that instant came back empty
        // and looked like a host holding nothing.
        if (!SeriesRequest.TryChannels(query["channels"], out string[] channels, out string? refusal))
        {
            await WriteJsonAsync(response, new { type = "series", status = "Error", reason = refusal })
                .ConfigureAwait(false);
            return;
        }

        var options = new SubscriptionOptions(
            channels,
            SubscriptionOptions.DefaultMaxUpdateHz,
            (int)ReadDouble(query["maxPoints"], SubscriptionOptions.DefaultMaxPoints),
            ReadDouble(query["windowSec"], SubscriptionOptions.DefaultWindowSec),
            ParseReduction(query["reduction"]));

        double now = SeriesClock.UtcNowSec();
        SeriesQueryResult result = server.Query(SeriesQueryRequest.Recent(
            options.Channels, options.WindowSec, options.MaxPoints, now, options.Method));

        ReadOnlyMemory<byte> body = SeriesFrameWriter.Write(result, now);
        await WriteAsync(response, "application/json; charset=utf-8", body.ToArray()).ConfigureAwait(false);
    }

    private static ReductionMethod ParseReduction(string? raw) => raw?.ToLowerInvariant() switch
    {
        "lttb" or "largest-triangle-three-buckets" => ReductionMethod.LargestTriangleThreeBuckets,
        "none" or "raw" => ReductionMethod.None,
        _ => ReductionMethod.MinMax
    };

    /// <summary>
    /// DVR replay honouring <c>?t=</c> (relative seconds) and <c>?window=</c> (span in seconds),
    /// so a client can actually scrub. The previous handler ignored the query string and always
    /// returned the last 60 seconds, which made time travel impossible.
    /// </summary>
    private static object BuildReplay(TelemetryStreamingServer server, System.Collections.Specialized.NameValueCollection query)
    {
        TimeTravelDvrPlayer dvr = server.DvrPlayer;
        double window = ReadDouble(query["window"], 60.0);
        double? relative = ReadNullableDouble(query["t"]);

        double center = relative.HasValue
            ? dvr.TimelineStartSec + relative.Value
            : TimeTravelDvrPlayer.UtcNowSeconds() - window / 2.0;

        List<DvrFrame> frames = dvr.ExtractSnapshot(center, window);

        return new
        {
            status = "Success",
            totalFrames = frames.Count,
            timelineStartSec = dvr.TimelineStartSec,
            maxDurationSec = dvr.MaxDurationSec,
            scrubPrecisionSec = TimeTravelDvrPlayer.ScrubPrecisionSec,
            playbackSpeed = dvr.PlaybackSpeed,
            requestedCenterSec = center,
            windowSec = window,
            frames
        };
    }

    private static object BuildReport(TelemetryStreamingServer server, System.Collections.Specialized.NameValueCollection query)
    {
        double window = ReadDouble(query["window"], 60.0);
        TimeTravelDvrPlayer dvr = server.DvrPlayer;
        List<DvrFrame> frames = dvr.ExtractSnapshot(TimeTravelDvrPlayer.UtcNowSeconds() - window / 2.0, window);

        // Only frames an analyzer actually examined can be called normal or anomalous. A frame
        // recorded without a verdict carries ZScore 0 by default, and counting it as "below
        // threshold" would report an all-clear the system never established.
        var critical = new List<DvrFrame>();
        int unevaluated = 0;
        foreach (DvrFrame frame in frames)
        {
            if (!frame.HasVerdict)
            {
                unevaluated++;
                continue;
            }

            if (frame.IsAnomaly || frame.ZScore >= 2.0) critical.Add(frame);
        }

        int evaluated = frames.Count - unevaluated;

        var markdown = new StringBuilder();
        markdown.AppendLine("# Telemetry Incident Report");
        markdown.AppendLine($"> **Generated**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  ");
        markdown.AppendLine($"> **Frames recorded**: {frames.Count} over {window:F1}s  ");
        markdown.AppendLine($"> **Frames scored**: {evaluated} (unscored: {unevaluated})  ");
        markdown.AppendLine($"> **Anomalies**: {critical.Count}");
        markdown.AppendLine();

        if (evaluated == 0)
        {
            markdown.AppendLine(frames.Count == 0
                ? "No frames were recorded in this window, so nothing could be assessed."
                : $"None of the {frames.Count} recorded frames carried an anomaly verdict, so this window cannot be assessed. Connect an analyzer to the ingest path before relying on this report.");
        }
        else if (critical.Count == 0)
        {
            markdown.AppendLine($"No anomalies among the {evaluated} scored frames in this window.");
        }
        else
        {
            markdown.AppendLine("## Incident Timeline");
            markdown.AppendLine();
            markdown.AppendLine("| Time (s) | Channel | Value | Z-Score | Severity |");
            markdown.AppendLine("|---|---|---|---|---|");

            foreach (DvrFrame frame in critical)
            {
                string severity = frame.ZScore >= 3.5 ? "CRITICAL" : "WARNING";
                markdown.AppendLine(
                    $"| {frame.TimestampSec - dvr.TimelineStartSec:F2} | `{frame.ChannelName}` | " +
                    $"{frame.Value:F3} | {frame.FormatZScore("F2")} | {severity} |");
            }
        }

        return new
        {
            status = "Success",
            anomalyCount = critical.Count,
            scoredFrameCount = evaluated,
            unscoredFrameCount = unevaluated,
            windowSec = window,
            markdown = markdown.ToString()
        };
    }

    private static async Task ServeStaticAsync(
        HttpListenerResponse response,
        string path,
        StaticContentHost content,
        string fallbackHtmlPath)
    {
        bool isRoot = path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
        string? file = isRoot ? null : content.Resolve(path);

        if (file is null && isRoot && File.Exists(fallbackHtmlPath))
        {
            file = fallbackHtmlPath;
        }

        if (file is null)
        {
            response.StatusCode = isRoot ? 200 : 404;
            await WriteAsync(response, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(
                isRoot
                    ? "<html><body><h1>TelemetryDashboard streaming server</h1>" +
                      "<p>WebSocket: <code>ws://localhost:8080/ws</code></p>" +
                      "<p>Server-Sent Events: <code>/stream</code></p></body></html>"
                    : "<html><body><h1>404 Not Found</h1></body></html>")).ConfigureAwait(false);
            return;
        }

        await WriteAsync(response, StaticContentHost.ContentTypeFor(file), await File.ReadAllBytesAsync(file).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, object payload) =>
        WriteAsync(response, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(payload));

    private static async Task WriteAsync(HttpListenerResponse response, string contentType, byte[] body)
    {
        response.ContentType = contentType;
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static double ReadDouble(string? raw, double fallback) => ReadNullableDouble(raw) ?? fallback;

    private static double? ReadNullableDouble(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
}
