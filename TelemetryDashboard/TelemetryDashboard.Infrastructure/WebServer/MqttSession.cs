using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Owns the broker socket and the CONNECT/CONNACK handshake behind <see cref="MqttPublisher"/>.
/// </summary>
/// <remarks>
/// Separated from the publisher so the transport state — socket, lock, last known endpoint — has
/// a single owner and the publisher is left holding only its public contract and its outbound
/// queue. The endpoint is recorded before the handshake is attempted, which is what lets
/// <see cref="ReconnectAsync"/> retry an endpoint whose first handshake failed.
/// </remarks>
internal sealed class MqttSession : IDisposable
{
    private readonly object _lock = new();

    private TcpClient? _client;
    private string _host = string.Empty;
    private int _port = 1883;
    private string? _username;
    private string? _password;
    private bool _disconnectSimulated;

    /// <summary>True once a connection attempt has recorded an endpoint to reconnect to.</summary>
    internal bool HasEndpoint => !string.IsNullOrWhiteSpace(_host);

    internal bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                if (_disconnectSimulated) return false;
                return _client?.Connected == true;
            }
        }
    }

    /// <summary>Opens a session with the broker. Returns false on any transport or protocol failure.</summary>
    internal async Task<bool> ConnectAsync(
        string clientId, string host, int port, string? username, string? password, int timeoutMs)
    {
        Disconnect();

        _host = host ?? string.Empty;
        _port = port;
        _username = username;
        _password = password;
        _disconnectSimulated = false;

        try
        {
            var client = new TcpClient();
            using var timeout = new CancellationTokenSource(Math.Max(1, timeoutMs));

            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);

            NetworkStream stream = client.GetStream();
            byte[] connect = MqttWireProtocol.BuildConnectPacket(clientId, username, password);
            await stream.WriteAsync(connect, timeout.Token).ConfigureAwait(false);

            var response = new byte[4];
            int read = await stream.ReadAsync(response, timeout.Token).ConfigureAwait(false);

            if (!MqttWireProtocol.IsConnAckAccepted(response, read))
            {
                client.Dispose();
                return false;
            }

            lock (_lock) _client = client;
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Reopens the endpoint recorded by the last connection attempt.</summary>
    internal Task<bool> ReconnectAsync(string clientId, int timeoutMs) =>
        ConnectAsync(clientId, _host, _port, _username, _password, timeoutMs);

    /// <summary>Marks the session down without closing the socket.</summary>
    internal void SimulateDisconnect()
    {
        lock (_lock) _disconnectSimulated = true;
    }

    /// <summary>Clears a simulated outage. Returns false when the outage was a real one.</summary>
    internal bool TryClearSimulatedDisconnect()
    {
        bool simulated;
        lock (_lock) simulated = _disconnectSimulated;

        if (!simulated) return false;

        lock (_lock) _disconnectSimulated = false;
        return true;
    }

    /// <summary>The live stream, or null when no socket is open.</summary>
    internal NetworkStream? GetStream()
    {
        lock (_lock) return _client?.GetStream();
    }

    internal void Disconnect()
    {
        lock (_lock)
        {
            _client?.Dispose();
            _client = null;
            _disconnectSimulated = false;
        }
    }

    public void Dispose() => Disconnect();
}
