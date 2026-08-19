using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Fans one telemetry frame out to every connected subscriber, across all transports.
/// </summary>
/// <remarks>
/// Delivery is per-subscriber serialised and failure-isolated: a stalled or vanished client is
/// evicted without blocking or corrupting delivery to the others. Frames are serialised once and
/// the same buffer is shared by every subscriber.
/// </remarks>
public sealed class TelemetryBroadcastHub : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ITelemetrySubscriber> _subscribers = new();
    private long _framesDelivered;

    /// <summary>How long a single subscriber may take before it is treated as stalled.</summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int SubscriberCount => _subscribers.Count;

    public long FramesDelivered => Interlocked.Read(ref _framesDelivered);

    /// <summary>Per-transport subscriber counts, for the operator status panel.</summary>
    public IReadOnlyDictionary<string, int> SubscribersByTransport =>
        _subscribers.Values
            .GroupBy(s => s.Transport, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    public void Add(ITelemetrySubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _subscribers[subscriber.Id] = subscriber;
    }

    public async Task RemoveAsync(string subscriberId)
    {
        if (subscriberId != null && _subscribers.TryRemove(subscriberId, out ITelemetrySubscriber? subscriber))
        {
            await subscriber.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Delivers a frame to all subscribers concurrently, dropping any that fail or stall.
    /// </summary>
    public async Task BroadcastAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken = default)
    {
        if (_subscribers.IsEmpty) return;

        var deliveries = new List<Task>(_subscribers.Count);
        foreach (KeyValuePair<string, ITelemetrySubscriber> entry in _subscribers)
        {
            deliveries.Add(DeliverAsync(entry.Value, utf8Payload, cancellationToken));
        }

        await Task.WhenAll(deliveries).ConfigureAwait(false);
        Interlocked.Increment(ref _framesDelivered);
    }

    private async Task DeliverAsync(
        ITelemetrySubscriber subscriber,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!subscriber.IsConnected)
        {
            await RemoveAsync(subscriber.Id).ConfigureAwait(false);
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);

            await subscriber.SendAsync(payload, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // One bad client must never interrupt the fan-out; evict it and carry on.
            await RemoveAsync(subscriber.Id).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string id in _subscribers.Keys.ToList())
        {
            await RemoveAsync(id).ConfigureAwait(false);
        }
    }
}
