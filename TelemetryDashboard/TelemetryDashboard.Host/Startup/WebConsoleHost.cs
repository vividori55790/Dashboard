using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Starts <see cref="TelemetryStreamingServer"/> and resolves what it serves.
/// </summary>
/// <remarks>
/// A thin composition seam, not a second web stack: the streaming server already owns the
/// WebSocket, SSE, status and DVR endpoints, and standing up another listener beside it would give
/// the console two sources of truth about the same telemetry.
/// </remarks>
public sealed class WebConsoleHost : IAsyncDisposable
{
    /// <summary>File names probed for the console page, in order, when none was specified.</summary>
    private static readonly string[] KnownClientFiles =
    {
        "stream_client.html", "index.html", "custom_dashboard.html"
    };

    private WebConsoleHost(TelemetryStreamingServer server, IReadOnlyList<string> roots, string? client)
    {
        Server = server;
        ContentRoots = roots;
        ClientFile = client;
    }

    /// <summary>The running server.</summary>
    public TelemetryStreamingServer Server { get; }

    /// <summary>Directories the server will serve files from.</summary>
    public IReadOnlyList<string> ContentRoots { get; }

    /// <summary>The file served at <c>/</c>, or null when no console page was found.</summary>
    public string? ClientFile { get; }

    /// <summary>The port actually bound.</summary>
    public int BoundPort => Server.Port;

    /// <summary>Base address the console is reachable at.</summary>
    public string BaseAddress => $"http://localhost:{BoundPort}";

    /// <summary>Binds the port and begins serving. Throws when the port cannot be bound.</summary>
    public static WebConsoleHost Start(HostOptions options)
    {
        var server = new TelemetryStreamingServer(options.Port);

        IReadOnlyList<string> roots = ResolveRoots(options);
        foreach (string root in roots)
        {
            server.AddContentRoot(root);
        }

        string? client = options.ClientFile ?? ProbeForClient(roots);

        // An empty path is the server's own "no fallback page" contract; it then answers / with a
        // built-in placeholder rather than pretending a console exists.
        server.Start(client ?? string.Empty);

        return new WebConsoleHost(server, roots, client);
    }

    /// <summary>
    /// Asks the running server which endpoints it advertises, over its own HTTP surface.
    /// </summary>
    /// <remarks>
    /// The banner prints what <c>/api/status</c> answers rather than a list compiled into this
    /// host. A hardcoded copy would keep printing five endpoints the day the server stops serving
    /// one of them, and the round trip doubles as proof the listener is actually answering.
    /// </remarks>
    public async Task<IReadOnlyList<string>> QueryAdvertisedEndpointsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string body = await client.GetStringAsync($"{BaseAddress}/api/status", cancellationToken).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("endpoints")
                .EnumerateArray()
                .Select(element => element.GetString() ?? string.Empty)
                .Where(path => path.Length > 0)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Server.DisposeAsync();

    private static IReadOnlyList<string> ResolveRoots(HostOptions options) =>
        options.ContentRoots.Count > 0
            ? options.ContentRoots
            : new[] { AppContext.BaseDirectory };

    private static string? ProbeForClient(IReadOnlyList<string> roots)
    {
        foreach (string root in roots)
        {
            foreach (string candidate in KnownClientFiles)
            {
                string path = Path.Combine(root, candidate);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}
