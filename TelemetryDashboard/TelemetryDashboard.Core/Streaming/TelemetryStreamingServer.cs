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
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Embedded web server hosting the live telemetry feed and the web console assets.
/// </summary>
/// <remarks>
/// Endpoints: <c>ws://host:port/ws</c> (WebSocket), <c>/stream</c> (Server-Sent Events),
/// <c>/api/status</c>, <c>/api/dvr/replay</c>, <c>/api/dvr/report</c>, plus static assets.
/// Visualisation lives in the client; this server's contract is to deliver frames and files.
/// </remarks>
public partial class TelemetryStreamingServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    /// <summary>What a refused client is told, on either transport.</summary>
    /// <remarks>
    /// The same sentence for both so an operator reading a browser console and one reading a socket
    /// close frame are looking at the same fact, and it names the ceiling because "too many" without
    /// a number tells nobody whether to raise it or to go find the client that is looping.
    /// </remarks>
    internal string HubFullReason =>
        $"stream client limit reached ({_hub.MaxSubscribers}); "
        + "refusing this connection rather than degrading the streams already running";

    /// <summary>Connections turned away because the hub was already full.</summary>
    public long RefusedConnections => _hub.RefusedConnections;

    /// <summary>Most concurrent stream subscribers this server admits.</summary>
    public int MaxStreamClients => _hub.MaxSubscribers;

    private readonly TelemetryBroadcastHub _hub;
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

    /// <summary>The durable archive to serve <c>/api/history</c> from, when the host keeps one.</summary>
    /// <remarks>
    /// Settable rather than constructed here because the store belongs to the host's lifetime, not
    /// the server's: it has to be flushed and closed after the last sample, which is a shutdown
    /// ordering the server does not own. Null means this host keeps no archive, and the endpoint
    /// says so rather than returning an empty result that reads like a quiet machine.
    /// </remarks>
    public Interfaces.IDataLogger? Archive { get; set; }

    /// <summary>Expressions this host serves from <c>/api/computed</c>.</summary>
    /// <remarks>
    /// Declared by the host rather than by a request, so a computed channel is a property of the
    /// installation and not of whoever opened a browser. An expression accepted from a query string
    /// would let any viewer name any channel and get an answer nobody configured — and two viewers
    /// would then disagree about what "efficiency" means on the same machine.
    /// </remarks>
    public IReadOnlyList<Analytics.ComputedChannel> Computed { get; set; } =
        Array.Empty<Analytics.ComputedChannel>();

    /// <summary>What this host may be commanded to change, or null when nothing.</summary>
    /// <remarks>
    /// Null on a host reading a real device, and that is the enforcement: there is no object to
    /// command rather than a check that refuses. Moving real hardware from a browser is not a
    /// setting this endpoint withholds, it is a different feature with its own arming.
    /// </remarks>
    public Interfaces.ISimulatedControl? Control { get; set; }

    /// <summary>Engineering limits in force on this host, or null when none were declared.</summary>
    /// <remarks>
    /// Null rather than an empty monitor, so <c>/api/limits</c> can distinguish "this host is not
    /// checking limits" from "it is checking and nothing is out of band". Those look identical from
    /// a quiet alarm list and mean opposite things about whether the machine is protected.
    /// </remarks>
    public Analytics.LimitMonitor? Limits { get; set; }

    /// <summary>
    /// The credential every request must carry, or null while the console is open to its machine.
    /// </summary>
    /// <remarks>
    /// Null is today's behaviour and stays the default: a loopback console is reachable only by
    /// somebody already on the machine, and demanding a password from them would be ceremony. It
    /// becomes non-null the moment an operator asks for one, and it is what any future decision to
    /// bind beyond loopback has to depend on.
    /// </remarks>
    public ConsoleAccessGate? Access { get; set; }

    /// <summary>
    /// What each port is delivering, when the host is keeping an inventory of it.
    /// </summary>
    /// <remarks>
    /// Nullable rather than an empty inventory, so /api/inputs can say "nobody is tracking this"
    /// instead of "there is nothing on your rig". They are different facts and only one of them is
    /// a reason to go and check the cable.
    /// </remarks>
    public Ingest.InputInventory? Inputs { get; set; }

    /// <summary>The pump publishing those channels, when one is running.</summary>
    /// <remarks>
    /// Null when this host computes nothing, which <c>/api/status</c> reports as such rather than
    /// as zeros — a pump that published nothing and no pump at all are different situations.
    /// </remarks>
    /// <summary>
    /// Where to ask which nodes are expected and which are missing, or null when nobody is asking.
    /// </summary>
    /// <remarks>
    /// A function rather than a snapshot, because a snapshot taken when the server started would
    /// answer every later request with the state of the fleet at boot -- when nothing has been
    /// heard from yet and everything is missing.
    /// <para>
    /// Set by the host, which owns the ledger. Coverage used to be printed once, at shutdown, so an
    /// operator could learn a converter had stopped reporting only by stopping the hub as well.
    /// </para>
    /// </remarks>
    public Func<Cluster.CoverageSnapshot>? Coverage { get; set; }

    /// <summary>Clock offsets per node, or null when nothing is comparing clocks.</summary>
    /// <remarks>
    /// A function for the same reason <see cref="Coverage"/> is one: a snapshot taken at start-up
    /// would answer every later request with the state of the fleet before anything had been heard.
    /// <para>
    /// Null means no ledger is attached; an empty list means one is attached and no sample has
    /// arrived carrying a clock of its own. Those are different facts, and only the second says
    /// anything about the fleet.
    /// </para>
    /// </remarks>
    public Func<IReadOnlyList<Models.NodeClock>>? Clocks { get; set; }

    /// <summary>What has been refused as already taken, or null when nothing is checking.</summary>
    /// <remarks>
    /// Null and "zero duplicates" are not the same claim, and the second is the dangerous one to
    /// infer: a link whose sender emits no sequence reports zero forever while nothing watches.
    /// The filter counts that case separately for exactly that reason.
    /// </remarks>
    public Cluster.DuplicateFilter? Duplicates { get; set; }

    public IComputedChannelCounters? ComputedCounters { get; set; }

    /// <summary>Raised when a client sends a command upstream over the WebSocket.</summary>
    public event EventHandler<string>? CommandReceived;

    /// <summary>True when the server accepts connections from other machines.</summary>
    public bool IsNetworkReachable { get; }

    /// <summary>Prefixes the listener was configured with, for the operator to see.</summary>
    public IReadOnlyList<string> BoundPrefixes { get; }

    /// <summary>
    /// Whether the link this server binds encrypts what crosses it.
    /// </summary>
    /// <remarks>
    /// Read off the scheme actually bound rather than stated, so it stays a measurement if a TLS
    /// prefix is ever added. Today nothing constructs one, and this answers false everywhere --
    /// which is the point: an operator asking "is my password protected on this hop" gets the
    /// answer from the socket rather than from a document.
    /// <para>
    /// An empty prefix list answers false, because "no prefixes" is not "all of them encrypted".
    /// </para>
    /// </remarks>
    public bool IsLinkEncrypted =>
        BoundPrefixes.Count > 0
        && BoundPrefixes.All(prefix => prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The endpoint paths this server answers, in the order <c>/api/status</c> lists them.
    /// </summary>
    /// <remarks>
    /// Public because the start-up banner prints it and used to obtain it by fetching
    /// <c>/api/status</c> over HTTP. That round trip stopped working the moment a credential was
    /// configured -- the host holds a PBKDF2 derivation and not the password, so it cannot
    /// authenticate to itself -- and the banner reported "did not answer" for a listener that had
    /// answered 401. One list, read by both, so the banner cannot describe a server that is not
    /// this one.
    /// </remarks>
    public static readonly IReadOnlyList<string> AdvertisedEndpoints =
    [
        "/ws", "/stream", "/api/status", "/api/series", "/api/spectrum", "/api/aligned",
        "/api/computed", "/api/limits", "/api/inputs", "/api/control", "/api/history",
        "/api/incident", "/api/dvr/replay", "/api/dvr/report", MetricsEndpoint.Path
    ];

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
    /// It stays the default regardless, and opening up is an explicit argument. It is also no
    /// longer an argument that can be given on its own: this endpoint streams live telemetry and
    /// accepts commands over its WebSocket, so <see cref="Start"/> refuses to bind wide unless
    /// <see cref="Access"/> is set. Which leaves confidentiality, and that this cannot supply --
    /// Basic on a cleartext link puts the password on the wire, so a wide binding belongs on a
    /// segment the operator controls or behind a TLS terminator, and the banner says which.
    ///
    /// On Windows a wildcard prefix normally needs an administrator or a <c>netsh http add
    /// urlacl</c> reservation; <see cref="Start"/> reports that plainly rather than failing with
    /// HttpListener's bare "Access is denied".
    /// </remarks>
    /// <param name="maxStreamClients">
    /// Most concurrent stream subscribers. Above this, connections are refused rather than
    /// admitted into a hub whose existing clients would pay for them; see
    /// <see cref="TelemetryBroadcastHub.MaxSubscribers"/>. Non-positive means the default.
    /// </param>
    public TelemetryStreamingServer(
        int port = 8080, bool acceptRemoteConnections = false, int maxStreamClients = 0)
    {
        Port = port;
        IsNetworkReachable = acceptRemoteConnections;
        _hub = new TelemetryBroadcastHub
        {
            MaxSubscribers = maxStreamClients > 0
                ? maxStreamClients
                : TelemetryBroadcastHub.DefaultMaxSubscribers
        };

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
        SeriesQuery = new SeriesQueryService(Series);
    }

    /// <summary>Registers an additional directory whose files may be served.</summary>
    public void AddContentRoot(string directory) => _content.AddRoot(directory);

    /// <summary>
    /// Refused rather than warned about: a listener on every interface with no credential.
    /// </summary>
    /// <remarks>
    /// The host's argument parser refuses the same combination earlier and with a better message,
    /// but a check that lives only in an argument parser protects only the callers that go through
    /// it. This one is at the place that actually binds the socket, so the desktop shell, a test
    /// and whatever calls this next all get it, and the unsafe state has no construction path at
    /// all rather than a documented convention against it.
    /// <para>
    /// Before the prefixes are touched, so a refused configuration never opens the port even for
    /// the instant it takes to throw.
    /// </para>
    /// </remarks>
    private void RefuseWideBindingWithoutACredential()
    {
        if (!IsNetworkReachable || Access is not null) return;

        throw new InvalidOperationException(
            $"binding all interfaces on port {Port} was asked for with no credential set. This "
            + "server streams live telemetry, replays recorded incidents and accepts commands over "
            + "its WebSocket, so an open listener on a shared segment publishes the plant to it. "
            + $"Set {nameof(Access)} before {nameof(Start)}, or bind loopback.");
    }

    public void Start(string htmlClientFilePath)
    {
        if (IsRunning) return;

        RefuseWideBindingWithoutACredential();

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
        StartPump();
        _acceptLoop = Task.Run(() => AcceptClientsAsync(_cts.Token));
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts.Cancel();
        try { _listener.Stop(); } catch (ObjectDisposedException) { }

        await StopPumpAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Records the frame on the DVR timeline and in the series store, then fans it out to every
    /// subscriber that has not asked for something narrower.
    /// </summary>
    /// <remarks>
    /// This is the whole-frame path, and it costs one JSON serialisation plus one parse per
    /// sample. It is kept because existing producers use it and because a client that never
    /// subscribes still expects the raw feed, but it does not scale: at a million samples a second
    /// it is roughly 220 MB/s on the wire for every connected browser. Producers at that rate
    /// should call <see cref="PublishSample"/> and let viewers subscribe.
    /// </remarks>
    public void BroadcastTelemetry(object telemetryPacket)
    {
        if (telemetryPacket is null) return;

        string json = JsonSerializer.Serialize(telemetryPacket);

        // One parse feeds both timelines. The DVR keeps its own epoch; the series store is stamped
        // in Unix seconds because that is what a browser plots against.
        TelemetryFrameRecorder.Record(DvrPlayer, json, TelemetryFrameRecorder.DefaultAnomalyThreshold, Series);

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

    /// <summary>Answers 401 with the challenge a client needs in order to try again.</summary>
    /// <remarks>
    /// The realm is offered so that curl's -u and a browser's own prompt both work without the
    /// caller having to know how this endpoint authenticates. The body says which flag turned the
    /// credential on, because the person most likely to meet this refusal is the operator who just
    /// configured it and forgot to pass the password to whatever they are testing with.
    /// </remarks>
    private static void Challenge(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        response.StatusCode = 401;
        response.AddHeader("WWW-Authenticate", $"Basic realm=\"{ConsoleAccessGate.Realm}\", charset=\"UTF-8\"");
        response.ContentType = "text/plain; charset=utf-8";

        byte[] body = System.Text.Encoding.UTF8.GetBytes(
            "This console requires the credential the host was started with (--credential)." + Environment.NewLine);
        response.ContentLength64 = body.Length;

        try
        {
            response.OutputStream.Write(body, 0, body.Length);
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException)
        {
            // A client that hung up before reading its refusal is not this server's problem.
        }

        response.Close();
    }

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";

            // Before the path is even looked at. Every surface this server has -- the console page, the
            // JSON endpoints, the SSE stream and the WebSocket upgrade -- arrives here, so one check
            // covers all of them and none can be added later that quietly misses it.
            if (Access is { } gate && !gate.Allows(context.Request.Headers["Authorization"]))
            {
                Challenge(context);
                return;
            }

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
        catch (Exception ex)
        {
            // Anything else used to escape here into a fire-and-forget Task.Run, where it was never
            // observed and the response was never closed -- so the caller waited forever. A hung
            // request is the worst answer available: it is indistinguishable from a slow query, a
            // wedged server and a dropped network, and none of those lead anyone to the fault.
            //
            // Found by the first endpoint that threw. Every route before it happened not to.
            Console.Error.WriteLine($"[http] {context.Request.Url?.AbsolutePath} failed: {ex}");
            await FailAsync(context, ex).ConfigureAwait(false);
        }
    }

    /// <summary>Answers a failed request with a 500 rather than leaving the caller waiting.</summary>
    /// <remarks>
    /// The message names the exception type and its text. This endpoint has no authentication and
    /// binds loopback by default, so the reader is the operator, and withholding the reason from
    /// them buys nothing.
    /// </remarks>
    private static async Task FailAsync(HttpListenerContext context, Exception ex)
    {
        try
        {
            context.Response.StatusCode = 500;
            byte[] body = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "Error",
                    reason = $"{ex.GetType().Name}: {ex.Message}",
                    path = context.Request.Url?.AbsolutePath
                }));

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The response was already begun or the client is gone; closing below is all that is left.
        }
        finally
        {
            try { context.Response.Close(); } catch (Exception) { }
        }
    }

    private async Task AcceptWebSocketAsync(HttpListenerContext context, CancellationToken token)
    {
        WebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        var subscriber = new WebSocketSubscriber(Guid.NewGuid().ToString("N"), wsContext.WebSocket);
        if (!_hub.TryAdd(subscriber))
        {
            // Closed with a status the client can read, rather than left open on a hub that will
            // never send it anything -- a socket that connects and stays silent is the hardest
            // failure of all to diagnose from the other end.
            await wsContext.WebSocket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation, HubFullReason, token).ConfigureAwait(false);
            return;
        }

        try
        {
            await ReceiveCommandsAsync(wsContext.WebSocket, subscriber.Id, token).ConfigureAwait(false);
        }
        finally
        {
            await _hub.RemoveAsync(subscriber.Id).ConfigureAwait(false);
        }
    }

    /// <summary>Reads upstream commands, reassembling messages that span multiple frames.</summary>
    /// <remarks>
    /// A subscription message is applied here and consumed. Anything else reaches
    /// <see cref="CommandReceived"/> exactly as before, so the application command channel is
    /// unchanged for every producer that was already using it.
    /// </remarks>
    private async Task ReceiveCommandsAsync(WebSocket socket, string subscriberId, CancellationToken token)
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
                string text = Encoding.UTF8.GetString(message.ToArray());
                if (!TryApplySubscription(subscriberId, text))
                {
                    CommandReceived?.Invoke(this, text);
                }
            }
            message.SetLength(0);
        }
    }

    private async Task AcceptServerSentEventsAsync(HttpListenerContext context, CancellationToken token)
    {
        HttpListenerResponse response = context.Response;

        // Admission before the 200. Sending event-stream headers and then discovering the hub is
        // full would leave the client holding a stream that never produces a frame.
        var subscriber = new ServerSentEventSubscriber(Guid.NewGuid().ToString("N"), response.OutputStream);
        if (!_hub.TryAdd(subscriber))
        {
            response.StatusCode = 503;
            response.ContentType = "text/plain; charset=utf-8";
            response.AddHeader("Retry-After", "5");
            byte[] body = Encoding.UTF8.GetBytes(HubFullReason);
            await response.OutputStream.WriteAsync(body, token).ConfigureAwait(false);
            response.Close();
            return;
        }

        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.SendChunked = true;

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
