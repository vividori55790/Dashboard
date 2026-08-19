using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// One connected consumer of the live telemetry feed, independent of transport.
/// </summary>
/// <remarks>
/// Adding a transport (WebRTC data channel, MQTT, gRPC stream) means implementing this interface
/// and registering it with the hub — the broadcast path itself never changes.
/// </remarks>
public interface ITelemetrySubscriber : IAsyncDisposable
{
    string Id { get; }

    /// <summary>Transport label surfaced in operator diagnostics, e.g. "websocket" or "sse".</summary>
    string Transport { get; }

    bool IsConnected { get; }

    /// <summary>Delivers one UTF-8 encoded telemetry frame.</summary>
    Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken);
}

/// <summary>
/// WebSocket subscriber. Sends are serialised through a semaphore because concurrent
/// <c>SendAsync</c> calls on one WebSocket throw and tear the connection down — the previous
/// implementation fired sends without awaiting them, so any two overlapping packets raced.
/// </summary>
public sealed class WebSocketSubscriber : ITelemetrySubscriber
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public WebSocketSubscriber(string id, WebSocket socket)
    {
        Id = id;
        _socket = socket;
    }

    public string Id { get; }

    public string Transport => "websocket";

    public bool IsConnected => _socket.State == WebSocketState.Open;

    public async Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open) return;
            await _socket.SendAsync(utf8Payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                         .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", timeout.Token)
                             .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // A client that already vanished cannot be closed politely; nothing to recover.
        }
        finally
        {
            _socket.Dispose();
            _sendGate.Dispose();
        }
    }
}

/// <summary>
/// Server-Sent Events subscriber over a long-lived HTTP response, per the specification's
/// <c>/stream</c> endpoint. SSE reaches browsers and dashboards that cannot open a WebSocket.
/// </summary>
public sealed class ServerSentEventSubscriber : ITelemetrySubscriber
{
    private static readonly byte[] FramePrefix = "data: "u8.ToArray();
    private static readonly byte[] FrameSuffix = "\n\n"u8.ToArray();

    private readonly Stream _output;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private bool _faulted;

    public ServerSentEventSubscriber(string id, Stream output)
    {
        Id = id;
        _output = output;
    }

    public string Id { get; }

    public string Transport => "sse";

    public bool IsConnected => !_faulted;

    public async Task SendAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_faulted) return;

            await _output.WriteAsync(FramePrefix, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(utf8Payload, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(FrameSuffix, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException)
        {
            _faulted = true; // client navigated away mid-stream
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _faulted = true;
        try { _output.Dispose(); } catch (ObjectDisposedException) { }
        _sendGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
