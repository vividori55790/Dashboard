using System;
using System.Collections.Generic;
using System.IO;
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
public sealed partial class WebConsoleHost : IAsyncDisposable
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
        var server = new TelemetryStreamingServer(
            options.Port,
            acceptRemoteConnections: options.ListenOnAllInterfaces,
            maxStreamClients: options.MaxStreamClients);

        // Attached before Start, so there is no window in which the port is open and the gate is
        // not. A credential configured but applied a moment late is a credential that was not
        // applied, and the moment is exactly when a listener is most interesting.
        if (options.CredentialPath is { } path)
        {
            // The parser already proved this loads. If it does not now, the file changed underneath
            // the run, and the one thing that must not happen is serving openly because the lock
            // went missing quietly.
            Core.Security.PasswordCredential credential = Core.Security.CredentialFile.Load(path)
                ?? throw new InvalidOperationException(
                    $"the credential file '{path}' could not be read at start-up, and serving "
                    + "without the credential that was asked for is not an option this host takes.");

            server.Access = new Core.Streaming.ConsoleAccessGate(credential);
        }

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
