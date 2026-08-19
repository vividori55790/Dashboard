using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Minimal MQTT 3.1.1 publisher: performs a real CONNECT/CONNACK handshake and PUBLISH over TCP.
/// </summary>
/// <remarks>
/// Implemented directly against the wire format rather than pulling in a broker client, because
/// the hub only needs QoS 0 publishing. Outbound messages are queued while the link is down and
/// the queue is bounded — refusing a message is honest backpressure, whereas an unbounded queue
/// converts a broker outage into a memory leak.
/// </remarks>
public sealed class MqttPublisher : IDisposable
{
    private readonly ConcurrentQueue<(string Topic, string Payload)> _queue = new();

    /// <summary>Serialises the bound check against the enqueue in <see cref="EnqueuePayload"/>.</summary>
    private readonly object _queueGate = new();
    private readonly MqttSession _session = new();

    public MqttPublisher(int maxQueueSize = 1000)
    {
        MaxQueueSize = Math.Max(1, maxQueueSize);
    }

    public int MaxQueueSize { get; }

    public int QueuedCount => _queue.Count;

    public string ClientId { get; init; } = "TelemetryDashboard-" + Guid.NewGuid().ToString("N")[..8];

    public bool IsConnected => _session.IsConnected;

    /// <summary>Opens a session with the broker. Returns false on any transport or protocol failure.</summary>
    public Task<bool> ConnectAsync(string host, int port = 1883, int timeoutMs = 5000) =>
        ConnectWithCredentialsAsync(host, port, username: null, password: null, timeoutMs);

    /// <summary>Opens an authenticated session. Returns false when the broker refuses the credentials.</summary>
    public async Task<bool> ConnectWithCredentialsAsync(
        string host, int port, string? username, string? password, int timeoutMs = 5000)
    {
        bool connected = await _session
            .ConnectAsync(ClientId, host, port, username, password, timeoutMs)
            .ConfigureAwait(false);

        if (!connected) return false;

        await DrainQueueAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Marks the session down without closing the socket, so reconnect handling can be exercised.
    /// </summary>
    public void SimulateDisconnect() => _session.SimulateDisconnect();

    /// <summary>
    /// Re-establishes the session. A simulated outage is simply cleared; a real one reconnects
    /// to the broker recorded by the last successful <see cref="ConnectAsync"/>.
    /// </summary>
    public async Task<bool> ReconnectAsync(int timeoutMs = 5000)
    {
        if (_session.TryClearSimulatedDisconnect())
        {
            await DrainQueueAsync().ConfigureAwait(false);
            return true;
        }

        if (!_session.HasEndpoint) return false;

        bool connected = await _session.ReconnectAsync(ClientId, timeoutMs).ConfigureAwait(false);
        if (!connected) return false;

        await DrainQueueAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>Publishes at QoS 0, queueing the message when the link is down.</summary>
    public async Task PublishAsync(string topic, string payload)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("MQTT topic must not be empty.", nameof(topic));
        }

        if (!IsConnected)
        {
            EnqueuePayload(topic, payload);
            return;
        }

        try
        {
            NetworkStream? stream = _session.GetStream();
            if (stream is null) return;

            await stream.WriteAsync(MqttWireProtocol.BuildPublishPacket(topic, payload ?? string.Empty)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            EnqueuePayload(topic, payload);
        }
    }

    /// <summary>Queues a message. Returns false when the queue is full and the message is refused.</summary>
    /// <remarks>
    /// The bound check and the enqueue are one critical section. <see cref="ConcurrentQueue{T}"/>
    /// makes each operation individually safe but not the pair: two publishers can both observe a
    /// count one below the limit and both enqueue, so the queue grows past the bound it exists to
    /// enforce. That bound is what stops a disconnected broker from consuming memory until the
    /// process dies, so exceeding it is the failure this queue was built to prevent.
    /// </remarks>
    public bool EnqueuePayload(string topic, string payload)
    {
        lock (_queueGate)
        {
            if (_queue.Count >= MaxQueueSize) return false;

            _queue.Enqueue((topic, payload ?? string.Empty));
            return true;
        }
    }

    private async Task DrainQueueAsync()
    {
        while (IsConnected && _queue.TryDequeue(out (string Topic, string Payload) message))
        {
            await PublishAsync(message.Topic, message.Payload).ConfigureAwait(false);
        }
    }

    public void Disconnect() => _session.Disconnect();

    public void Dispose() => _session.Dispose();
}
