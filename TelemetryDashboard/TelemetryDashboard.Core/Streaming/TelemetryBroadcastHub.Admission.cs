using System;
using System.Threading;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Who is let in, and who is turned away.
/// </summary>
/// <remarks>
/// Kept apart from the fan-out because it is a policy rather than a mechanism, and because it did
/// not exist at all until it had to: the register admitted whatever arrived. See
/// <see cref="MaxSubscribers"/> for who pays for that.
/// </remarks>
public partial class TelemetryBroadcastHub
{
    /// <summary>
    /// Most concurrent subscribers this hub will carry.
    /// </summary>
    /// <remarks>
    /// There was no ceiling at all, which matters more here than it looks. Every subscriber is a
    /// long-lived connection and every frame is fanned out to all of them, each with its own send
    /// timeout — so the cost of an extra client is paid by the clients already being served, not by
    /// the one arriving. A tab left reloading, a script with no back-off, or anything malicious
    /// reaching a host started with remote connections enabled degrades the operator who is
    /// actually watching the plant, and does it silently.
    /// <para>
    /// 256 is the number <c>SseStreamHandler</c> chose for this and never got to enforce, and it is
    /// far above any real operator count while still bounding the damage.
    /// </para>
    /// </remarks>
    public int MaxSubscribers { get; init; } = DefaultMaxSubscribers;

    /// <summary>Default for <see cref="MaxSubscribers"/>.</summary>
    public const int DefaultMaxSubscribers = 256;

    /// <summary>Connections refused because the hub was already full.</summary>
    /// <remarks>
    /// Counted so a refusal is a fact an operator can see on the status endpoint. A cap that turns
    /// clients away and says nothing looks, from the outside, exactly like a network that drops
    /// connections.
    /// </remarks>
    public long RefusedConnections => Interlocked.Read(ref _refused);

    /// <summary>
    /// Admits a subscriber, or refuses it because the hub is full.
    /// </summary>
    /// <remarks>
    /// Refusing rather than accepting-and-degrading is the whole point: a client told "no" retries
    /// or shows an error, and one admitted into a starved hub sees a feed that stutters for reasons
    /// it cannot diagnose — along with everybody else's.
    /// <para>
    /// A subscriber replacing itself under an id already present is not a new connection and is
    /// never refused, so a reconnect cannot be locked out by its own stale entry.
    /// </para>
    /// </remarks>
    public bool TryAdd(ITelemetrySubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (!_subscribers.ContainsKey(subscriber.Id) && _subscribers.Count >= MaxSubscribers)
        {
            Interlocked.Increment(ref _refused);
            return false;
        }

        _subscribers[subscriber.Id] = subscriber;
        return true;
    }
}
