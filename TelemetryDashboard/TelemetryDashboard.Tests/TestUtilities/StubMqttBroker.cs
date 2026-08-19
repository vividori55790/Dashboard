using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Tests.TestUtilities;

/// <summary>One PUBLISH as it arrived on the wire.</summary>
public sealed record MqttPublication(string Topic, string Payload);

/// <summary>
/// A minimal MQTT 3.1.1 broker that accepts a connection and decodes QoS 0 PUBLISH packets.
/// </summary>
/// <remarks>
/// Deliberately decodes the wire format itself rather than reusing anything from the publisher
/// under test. A stub written in terms of the code it verifies proves only that the code agrees
/// with itself; this one agrees or disagrees with the MQTT specification, which is the question.
/// </remarks>
public sealed class StubMqttBroker : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _accepting;

    public StubMqttBroker()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(() => AcceptAsync(_cts.Token));
    }

    /// <summary>Ephemeral port the broker is listening on.</summary>
    public int Port { get; }

    /// <summary>Everything published so far, in arrival order.</summary>
    public ConcurrentQueue<MqttPublication> Received { get; } = new();

    /// <summary>Waits until at least <paramref name="count"/> publications have arrived.</summary>
    public async Task<bool> WaitForAsync(int count, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Received.Count >= count) return true;
            await Task.Delay(20).ConfigureAwait(false);
        }
        return Received.Count >= count;
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (Exception) { /* Listener stopped. */ }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int header = stream.ReadByte();
                    if (header < 0) return;

                    int remaining = ReadRemainingLength(stream);
                    byte[] body = new byte[remaining];
                    int read = 0;
                    while (read < remaining)
                    {
                        int got = await stream.ReadAsync(body.AsMemory(read, remaining - read), cancellationToken).ConfigureAwait(false);
                        if (got <= 0) return;
                        read += got;
                    }

                    switch (header >> 4)
                    {
                        case 1:  // CONNECT -> CONNACK, session not present, accepted
                            await stream.WriteAsync(new byte[] { 0x20, 0x02, 0x00, 0x00 }, cancellationToken).ConfigureAwait(false);
                            break;
                        case 3:  // PUBLISH (QoS 0: no packet identifier)
                            Received.Enqueue(Decode(body));
                            break;
                        case 12: // PINGREQ -> PINGRESP
                            await stream.WriteAsync(new byte[] { 0xD0, 0x00 }, cancellationToken).ConfigureAwait(false);
                            break;
                        case 14: // DISCONNECT
                            return;
                    }
                }
            }
            catch (Exception) { /* Client went away. */ }
        }
    }

    private static MqttPublication Decode(byte[] body)
    {
        int topicLength = (body[0] << 8) | body[1];
        string topic = Encoding.UTF8.GetString(body, 2, topicLength);
        string payload = Encoding.UTF8.GetString(body, 2 + topicLength, body.Length - 2 - topicLength);
        return new MqttPublication(topic, payload);
    }

    private static int ReadRemainingLength(NetworkStream stream)
    {
        int multiplier = 1, value = 0, digit;
        do
        {
            digit = stream.ReadByte();
            if (digit < 0) return 0;
            value += (digit & 127) * multiplier;
            multiplier *= 128;
        } while ((digit & 128) != 0);
        return value;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _accepting.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
