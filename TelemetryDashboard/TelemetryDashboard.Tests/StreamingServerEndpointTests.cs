using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// End-to-end checks against a live listener: the specification's SSE endpoint, WebSocket
/// delivery under concurrent broadcast, and static-content confinement.
/// </summary>
public class StreamingServerEndpointTests : IAsyncLifetime
{
    private TelemetryStreamingServer _server = null!;
    private string _webRoot = null!;
    private int _port;

    /// <summary>
    /// Leases a free loopback port. A fixed port made consecutive tests in this class collide
    /// while the previous listener was still unwinding.
    /// </summary>
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public Task InitializeAsync()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "td-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "widget.html"), "<html>widget</html>");

        // A secret sitting beside the web root must never be reachable through it.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_webRoot)!, "td-secret.txt"), "PRIVATE KEY");

        _port = FindFreePort();
        _server = new TelemetryStreamingServer(_port);
        _server.AddContentRoot(_webRoot);
        _server.Start(Path.Combine(_webRoot, "widget.html"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        try { Directory.Delete(_webRoot, true); } catch (IOException) { }
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task StatusEndpoint_AdvertisesWebSocketAndSseEndpoints()
    {
        using var client = new HttpClient();
        string body = await client.GetStringAsync($"http://localhost:{_port}/api/status");

        using JsonDocument document = JsonDocument.Parse(body);
        string[] endpoints = document.RootElement.GetProperty("endpoints")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        endpoints.Should().Contain("/ws");
        endpoints.Should().Contain("/stream", "the specification requires an SSE endpoint");
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task SseStream_DeliversBroadcastFrames()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_port}/stream");

        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Handshake frame confirms the subscriber is registered before we broadcast.
        string? handshake = await ReadEventAsync(reader);
        handshake.Should().Contain("connected");

        _server.BroadcastTelemetry(new { nodeId = "COM7", temp = 42.5 });

        string? frame = await ReadEventAsync(reader);
        frame.Should().NotBeNull();
        frame!.Should().Contain("COM7").And.Contain("42.5");
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task WebSocket_SurvivesRapidConcurrentBroadcasts()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://localhost:{_port}/ws"), CancellationToken.None);
        await WaitForSubscribersAsync(1);

        // Overlapping sends on one socket threw before delivery was serialised per subscriber.
        for (int i = 0; i < 200; i++)
        {
            _server.BroadcastTelemetry(new { nodeId = "COM3", seq = i });
        }

        var buffer = new byte[8192];
        int received = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (received < 200 && !timeout.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            received++;
        }

        received.Should().Be(200);
        socket.State.Should().Be(WebSocketState.Open);
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task WebSocket_IsRejectedOutsideTheWsPath()
    {
        using var socket = new ClientWebSocket();

        Func<Task> connect = () => socket.ConnectAsync(
            new Uri($"ws://localhost:{_port}/not-ws"), CancellationToken.None);

        await connect.Should().ThrowAsync<WebSocketException>();
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task StaticContent_ServesWebRootButRefusesTraversal()
    {
        using var client = new HttpClient();

        (await client.GetStringAsync($"http://localhost:{_port}/widget.html")).Should().Contain("widget");

        using HttpResponseMessage escaped = await client.GetAsync(
            $"http://localhost:{_port}/%2e%2e/td-secret.txt");

        escaped.IsSuccessStatusCode.Should().BeFalse();
        (await escaped.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE KEY");
    }

    private async Task WaitForSubscribersAsync(int expected)
    {
        for (int i = 0; i < 100 && _server.ConnectedClientCount < expected; i++)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>Reads one SSE "data:" frame.</summary>
    private static async Task<string?> ReadEventAsync(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(timeout.Token);
            if (line is null) return null;
            if (line.StartsWith("data: ")) return line["data: ".Length..];
        }
        return null;
    }
}
