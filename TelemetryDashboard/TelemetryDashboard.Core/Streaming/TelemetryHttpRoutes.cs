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
        endpoints = new[] { "/ws", "/stream", "/api/status", "/api/series", "/api/spectrum", "/api/aligned", "/api/dvr/replay", "/api/dvr/report" }
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
        string[] channels = (query["channels"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
