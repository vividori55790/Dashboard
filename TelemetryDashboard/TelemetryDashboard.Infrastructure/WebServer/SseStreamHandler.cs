using System;
using System.Collections.Concurrent;
using System.Linq;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>
/// Tracks Server-Sent Events subscribers and validates inbound request lines.
/// </summary>
/// <remarks>
/// The client cap is enforced by refusing the connection rather than accepting it and degrading
/// everyone: an SSE connection is long-lived, so silently admitting an unbounded number of them
/// starves the subscribers already being served.
/// </remarks>
public sealed class SseStreamHandler
{
    private static readonly string[] SupportedMethods = { "GET", "HEAD", "OPTIONS" };

    private readonly ConcurrentDictionary<string, DateTime> _clients = new();

    public SseStreamHandler(int maxClients = 256)
    {
        MaxClients = Math.Max(1, maxClients);
    }

    public int MaxClients { get; }

    public int ActiveClientCount => _clients.Count;

    /// <summary>Client identifiers currently subscribed.</summary>
    public string[] ActiveClientIds => _clients.Keys.ToArray();

    /// <summary>
    /// Registers a subscriber and returns its identifier.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection cap is already reached.</exception>
    public string RegisterClient()
    {
        if (_clients.Count >= MaxClients)
        {
            throw new InvalidOperationException(
                $"SSE client limit reached ({MaxClients}). Refusing the connection rather than degrading existing streams.");
        }

        string id = Guid.NewGuid().ToString("N");
        _clients[id] = DateTime.UtcNow;
        return id;
    }

    public bool UnregisterClient(string clientId) =>
        !string.IsNullOrEmpty(clientId) && _clients.TryRemove(clientId, out _);

    public void Clear() => _clients.Clear();

    /// <summary>
    /// Validates a raw HTTP request line and returns the status code that should be sent.
    /// </summary>
    /// <returns>200 when acceptable, 400 when malformed, 405 for an unsupported method.</returns>
    public int ProcessRawRequest(string rawRequest)
    {
        if (string.IsNullOrWhiteSpace(rawRequest)) return 400;

        string requestLine = rawRequest.Split('\n')[0].Trim('\r', ' ');
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A request line is "METHOD SP target SP HTTP/x.y" — anything else is malformed.
        if (parts.Length != 3) return 400;
        if (!parts[2].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)) return 400;
        if (!parts[1].StartsWith('/')) return 400;

        return SupportedMethods.Contains(parts[0], StringComparer.Ordinal) ? 200 : 405;
    }
}
