using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Embedded web server hosting the live telemetry feed and the web console assets.
/// </summary>
/// <remarks>
/// Endpoints: <c>ws://host:port/ws</c> (WebSocket), <c>/stream</c> (Server-Sent Events),
/// <c>/api/status</c>, <c>/api/dvr/replay</c>, <c>/api/dvr/report</c>, plus static assets.
/// Visualisation lives in the client; this server's contract is to deliver frames and files.
/// </remarks>
public class TelemetryStreamingServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly TelemetryBroadcastHub _hub = new();
    private readonly StaticContentHost _content = new();
    private readonly CancellationTokenSource _cts = new();

    private string _htmlClientPath = string.Empty;
    private Task? _acceptLoop;

    /// <summary>Win32 ERROR_ACCESS_DENIED, raised for an unreserved wildcard prefix.</summary>
    private const int AccessDenied = 5;

    public bool IsRunning { get; private set; }

    public int Port { get; }

    public int ConnectedClientCount => _hub.SubscriberCount;

    public long TotalPacketsBroadcasted => _hub.FramesDelivered;

    public TimeTravelDvrPlayer DvrPlayer { get; } = new();

    /// <summary>Raised when a client sends a command upstream over the WebSocket.</summary>
    public event EventHandler<string>? CommandReceived;

    /// <summary>True when the server accepts connections from other machines.</summary>
    public bool IsNetworkReachable { get; }

    /// <summary>Prefixes the listener was configured with, for the operator to see.</summary>
    public IReadOnlyList<string> BoundPrefixes { get; }

    /// <summary>
    /// Binds the console. Loopback only unless <paramref name="acceptRemoteConnections"/> is set.
    /// </summary>
    /// <param name="acceptRemoteConnections">
    /// Opens the listener to every interface so browsers on other devices can reach it.
    /// </param>
    /// <remarks>
    /// The desktop shell is Windows-only, so a phone, a Mac or a Linux workstation reaches this
    /// hub solely through a browser — and a listener bound to <c>localhost</c> is unreachable from
    /// all of them. That made loopback-only binding the single thing standing between a portable
    /// backbone and an actually usable one.
    ///
    /// It stays the default regardless, and opening up is an explicit argument. This endpoint has
    /// no authentication: it streams live telemetry and accepts commands over the WebSocket, so
    /// binding every interface publishes plant data to whatever shares the network. That is a
    /// decision an operator makes deliberately, not one a default makes for them.
    ///
    /// On Windows a wildcard prefix normally needs an administrator or a <c>netsh http add
    /// urlacl</c> reservation; <see cref="Start"/> reports that plainly rather than failing with
    /// HttpListener's bare "Access is denied".
    /// </remarks>
    public TelemetryStreamingServer(int port = 8080, bool acceptRemoteConnections = false)
    {
        Port = port;
        IsNetworkReachable = acceptRemoteConnections;

        var prefixes = new List<string>();
        if (acceptRemoteConnections)
        {
            prefixes.Add($"http://+:{Port}/");
        }
        else
        {
            prefixes.Add($"http://localhost:{Port}/");
            prefixes.Add($"http://127.0.0.1:{Port}/");
        }

        foreach (string prefix in prefixes) _listener.Prefixes.Add(prefix);
        BoundPrefixes = prefixes;
    }

    /// <summary>Registers an additional directory whose files may be served.</summary>
    public void AddContentRoot(string directory) => _content.AddRoot(directory);

    public void Start(string htmlClientFilePath)
    {
        if (IsRunning) return;

        _htmlClientPath = htmlClientFilePath ?? string.Empty;
        _content.AddRoot(AppDomain.CurrentDomain.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(_htmlClientPath))
        {
            _content.AddRoot(Path.GetDirectoryName(_htmlClientPath));
        }

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex) when (IsNetworkReachable && ex.ErrorCode == AccessDenied)
        {
            // Wildcard prefixes are reserved on Windows. Saying so beats HttpListener's bare
            // "Access is denied", which sends operators looking at firewalls for an hour.
            throw new HttpListenerException(ex.ErrorCode,
                $"Binding all interfaces on port {Port} requires elevation or a URL reservation. " +
                $"Run as administrator, or reserve it once with: " +
                $"netsh http add urlacl url=http://+:{Port}/ user=\"{Environment.UserName}\"");
        }

        IsRunning = true;
        _acceptLoop = Task.Run(() => AcceptClientsAsync(_cts.Token));
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts.Cancel();
        try { _listener.Stop(); } catch (ObjectDisposedException) { }

        await _hub.DisposeAsync().ConfigureAwait(false);

        if (_acceptLoop is not null)
        {
            // Never block on the accept loop indefinitely; Stop() may be called from the UI thread.
            await Task.WhenAny(_acceptLoop, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _listener.Close();
    }

    /// <summary>Records the frame on the DVR timeline and fans it out to every subscriber.</summary>
    public void BroadcastTelemetry(object telemetryPacket)
    {
        if (telemetryPacket is null) return;

        string json = JsonSerializer.Serialize(telemetryPacket);
        TelemetryFrameRecorder.Record(DvrPlayer, json);

        if (!IsRunning) return;

        byte[] payload = Encoding.UTF8.GetBytes(json);
        _ = _hub.BroadcastAsync(payload, _cts.Token);
    }

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        int consecutiveFailures = 0;

        while (IsRunning && !token.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                consecutiveFailures = 0;
                _ = Task.Run(() => DispatchAsync(context, token), token);
            }
            catch (Exception) when (!IsRunning || token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Back off instead of spinning: the old loop retried instantly and burned a core
                // whenever the listener faulted while still marked running.
                if (++consecutiveFailures > 10) break;
                await Task.Delay(TimeSpan.FromMilliseconds(100 * consecutiveFailures), token).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";

            if (context.Request.IsWebSocketRequest)
            {
                if (!path.Equals("/ws", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                await AcceptWebSocketAsync(context, token).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/stream", StringComparison.OrdinalIgnoreCase))
            {
                await AcceptServerSentEventsAsync(context, token).ConfigureAwait(false);
                return;
            }

            await TelemetryHttpRoutes.HandleAsync(context, path, this, _content, _htmlClientPath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // Client disconnected mid-request.
        }
    }

    private async Task AcceptWebSocketAsync(HttpListenerContext context, CancellationToken token)
    {
        WebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        var subscriber = new WebSocketSubscriber(Guid.NewGuid().ToString("N"), wsContext.WebSocket);
        _hub.Add(subscriber);

        try
        {
            await ReceiveCommandsAsync(wsContext.WebSocket, token).ConfigureAwait(false);
        }
        finally
        {
            await _hub.RemoveAsync(subscriber.Id).ConfigureAwait(false);
        }
    }

    /// <summary>Reads upstream commands, reassembling messages that span multiple frames.</summary>
    private async Task ReceiveCommandsAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close) break;

            message.Write(buffer, 0, result.Count);

            // A command longer than one frame must be reassembled; treating each frame as a whole
            // command split long payloads into fragments that parsed as garbage.
            if (!result.EndOfMessage) continue;

            if (message.Length > 0)
            {
                CommandReceived?.Invoke(this, Encoding.UTF8.GetString(message.ToArray()));
            }
            message.SetLength(0);
        }
    }

    private async Task AcceptServerSentEventsAsync(HttpListenerContext context, CancellationToken token)
    {
        HttpListenerResponse response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.SendChunked = true;

        var subscriber = new ServerSentEventSubscriber(Guid.NewGuid().ToString("N"), response.OutputStream);
        _hub.Add(subscriber);

        // Prime the stream so clients see an immediate connection confirmation.
        await subscriber.SendAsync(
            Encoding.UTF8.GetBytes($"{{\"event\":\"connected\",\"port\":{Port}}}"), token).ConfigureAwait(false);

        try
        {
            // Hold the response open; the hub writes to it until the client disconnects.
            while (!token.IsCancellationRequested && subscriber.IsConnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down.
        }
        finally
        {
            await _hub.RemoveAsync(subscriber.Id).ConfigureAwait(false);
        }
    }
}
