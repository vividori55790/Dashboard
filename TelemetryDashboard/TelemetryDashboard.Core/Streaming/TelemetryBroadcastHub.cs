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
    private readonly ConcurrentDictionary<string, TelemetrySubscription> _subscriptions = new();
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
            _subscriptions.TryRemove(subscriberId, out _);
            await subscriber.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Clients receiving a reduced feed of the channels they asked for.</summary>
    public int SubscriptionCount => _subscriptions.Count;

    /// <summary>Records what one client asked to be sent, replacing any previous request.</summary>
    public void Subscribe(string subscriberId, SubscriptionOptions options, double nowSec)
    {
        ArgumentNullException.ThrowIfNull(subscriberId);
        ArgumentNullException.ThrowIfNull(options);
        _subscriptions[subscriberId] = new TelemetrySubscription(options, nowSec);
    }

    /// <summary>Returns a client to the unfiltered feed.</summary>
    public bool Unsubscribe(string subscriberId) =>
        subscriberId is not null && _subscriptions.TryRemove(subscriberId, out _);

    /// <summary>This client's standing request, or <c>null</c> when it made none.</summary>
    public TelemetrySubscription? SubscriptionFor(string subscriberId) =>
        subscriberId is not null && _subscriptions.TryGetValue(subscriberId, out TelemetrySubscription? s) ? s : null;

    /// <summary>Every subscriber that has stated what it wants, paired with that statement.</summary>
    public IEnumerable<(ITelemetrySubscriber Subscriber, TelemetrySubscription Subscription)> SubscribedClients()
    {
        foreach (KeyValuePair<string, TelemetrySubscription> entry in _subscriptions)
        {
            if (_subscribers.TryGetValue(entry.Key, out ITelemetrySubscriber? subscriber))
            {
                yield return (subscriber, entry.Value);
            }
        }
    }

    /// <summary>Delivers one frame to a single subscriber, evicting it if it fails or stalls.</summary>
    public Task SendToAsync(
        ITelemetrySubscriber subscriber,
        ReadOnlyMemory<byte> utf8Payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        return DeliverAsync(subscriber, utf8Payload, cancellationToken);
    }

    /// <summary>
    /// Delivers a frame to every unfiltered subscriber concurrently, dropping any that fail or stall.
    /// </summary>
    /// <remarks>
    /// A client that stated what it wants is skipped here and served by the reduction pump at the
    /// rate it asked for. Sending it the raw feed as well would defeat the request: it asked for
    /// 10 Hz precisely so it would not be handed the ingest rate.
    /// </remarks>
    public async Task BroadcastAsync(ReadOnlyMemory<byte> utf8Payload, CancellationToken cancellationToken = default)
    {
        if (_subscribers.IsEmpty) return;

        var deliveries = new List<Task>(_subscribers.Count);
        foreach (KeyValuePair<string, ITelemetrySubscriber> entry in _subscribers)
        {
            if (!_subscriptions.IsEmpty && _subscriptions.ContainsKey(entry.Key)) continue;
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
