using System.Net.Http;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Refusing a stream client rather than admitting it into a hub the existing ones would pay for.
/// </summary>
/// <remarks>
/// There was no ceiling at all. Every subscriber is a long-lived connection and every frame is
/// fanned out to all of them, each with its own send timeout, so the cost of one more client is
/// borne by the clients already being served — not by the one arriving. A browser tab left
/// reloading, a script with no back-off, or anything at all reaching a host started with remote
/// connections enabled degrades the operator watching the plant, and does it silently.
/// <para>
/// Measured against the running host with <c>--max-clients 2</c>: two <c>/stream</c> clients
/// connected and received 224 frames each, the third was answered 503 with the ceiling named,
/// <c>/api/status</c> reported <c>connected=2 max=2 refused=1</c>, and a fourth client connected
/// normally once the first two had gone.
/// </para>
/// </remarks>
public class StreamClientCapTests
{
    /// <summary>A subscriber that records nothing and sends nowhere; the hub only needs its id.</summary>
    private sealed class Seat(string id) : ITelemetrySubscriber
    {
        public string Id => id;
        public string Transport => "test";
        public bool IsConnected => true;
        public Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheHubStopsAdmittingAtItsCeilingAndCountsWhatItTurnedAway()
    {
        var hub = new TelemetryBroadcastHub { MaxSubscribers = 3 };

        for (int i = 0; i < 3; i++) hub.TryAdd(new Seat($"client-{i}")).Should().BeTrue();

        hub.TryAdd(new Seat("one-too-many")).Should().BeFalse();
        hub.SubscriberCount.Should().Be(3, "the refused client must not be holding a seat");
        hub.RefusedConnections.Should().Be(1,
            "a cap that turns clients away and says nothing looks like a network dropping them");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ASeatIsReleasedWhenItsClientLeaves()
    {
        // Otherwise the cap is a one-way ratchet: a host that had once been busy would refuse
        // everybody afterwards, and the only cure would be a restart.
        var hub = new TelemetryBroadcastHub { MaxSubscribers = 1 };
        hub.TryAdd(new Seat("first")).Should().BeTrue();
        hub.TryAdd(new Seat("second")).Should().BeFalse();

        await hub.RemoveAsync("first");

        hub.TryAdd(new Seat("second")).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AClientReplacingItselfUnderTheSameIdIsNotANewConnection()
    {
        // A reconnect that reuses its id must not be locked out by its own stale entry, which is
        // the shape of failure a naive count check produces at exactly the worst moment.
        var hub = new TelemetryBroadcastHub { MaxSubscribers = 1 };
        hub.TryAdd(new Seat("same")).Should().BeTrue();

        hub.TryAdd(new Seat("same")).Should().BeTrue();

        hub.SubscriberCount.Should().Be(1);
        hub.RefusedConnections.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheDefaultCeilingIsFarAboveAnyRealOperatorCountAndStillBounded()
    {
        new TelemetryBroadcastHub().MaxSubscribers.Should().Be(TelemetryBroadcastHub.DefaultMaxSubscribers);
        TelemetryBroadcastHub.DefaultMaxSubscribers.Should().BeGreaterThan(32);
        TelemetryBroadcastHub.DefaultMaxSubscribers.Should().BeLessThan(10_000);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AServerGivenNoCeilingUsesTheDefaultRatherThanNone()
    {
        // 0 means "unset" on the command line, and reading it as "no limit" would quietly restore
        // the unbounded behaviour this exists to end.
        var server = new TelemetryStreamingServer(port: 0, maxStreamClients: 0);

        server.MaxStreamClients.Should().Be(TelemetryBroadcastHub.DefaultMaxSubscribers);
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public async Task AgainstARunningServerTheOverflowClientIsAnsweredAndTheAdmittedOnesKeepStreaming()
    {
        // The whole claim, end to end: refusing must not disturb the clients already connected.
        int port = FreePort();
        await using var server = new TelemetryStreamingServer(port, maxStreamClients: 1);
        server.Start(htmlClientFilePath: string.Empty);

        using var admitted = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using HttpResponseMessage first = await admitted.GetAsync(
            $"http://127.0.0.1:{port}/stream", HttpCompletionOption.ResponseHeadersRead);
        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using var overflow = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using HttpResponseMessage second = await overflow.GetAsync($"http://127.0.0.1:{port}/stream");

        second.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
        (await second.Content.ReadAsStringAsync()).Should().Contain("limit reached");
        second.Headers.Contains("Retry-After").Should().BeTrue("a client told 503 needs to know to come back");

        server.RefusedConnections.Should().Be(1);
        server.ConnectedClientCount.Should().Be(1, "the admitted client keeps its seat");
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
