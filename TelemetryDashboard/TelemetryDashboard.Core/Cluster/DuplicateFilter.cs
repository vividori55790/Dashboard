using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// Refuses a sample this host has already taken, so a replayed link cannot inflate a total.
/// </summary>
/// <remarks>
/// ARCHITECTURE §4: "Exchange must also be idempotent. A reconnect that replays a buffer must not
/// double-count, so samples carry a per-node sequence and the receiver deduplicates. Otherwise a
/// flaky link inflates every total, and the totals are what the operator trusts."
/// <para>
/// Observed rather than anticipated. While driving the backfill work, the test peer's connection
/// ended and <c>SseTelemetrySource</c> reconnected — which is what it is built to do — and the peer
/// replayed its sequence from the start. The receiving host ingested the same four-hour-old sample
/// twice and reported it twice, with nothing anywhere able to notice.
/// </para>
/// <para>
/// The sequence is assigned by whoever sends the frame, per node, inside that process's epoch.
/// That makes the guarantee hop-by-hop, which is exactly the shape of the failure: it is a
/// <em>link</em> that reconnects and replays. An end-to-end sequence, surviving relays and
/// deduplicating against the original observer, is a different and stronger property, and it is not
/// this.
/// </para>
/// <para>
/// The epoch is why a sender restarting does not lock itself out. A counter that resets to zero
/// after a restart would look like a replay of everything, and the receiver would silently discard
/// a healthy peer's entire stream — a far worse failure than the one being prevented.
/// </para>
/// </remarks>
public sealed class DuplicateFilter
{
    /// <summary>How many recent sequence numbers are remembered per node and epoch.</summary>
    /// <remarks>
    /// A replayed buffer is bounded by what the sender held, and this has to be larger than that to
    /// catch it. Beyond the window a genuine duplicate is admitted rather than dropped, which is
    /// the safe direction to fail: admitting a duplicate inflates a total, and dropping a real
    /// sample destroys an observation nobody can recover.
    /// </remarks>
    public const int DefaultWindow = 4096;

    /// <summary>How many (node, epoch) pairs are tracked before the oldest is evicted.</summary>
    public const int DefaultSenders = 256;

    private sealed class Window
    {
        public readonly HashSet<long> Seen = [];
        public readonly Queue<long> Order = new();
    }

    private readonly BoundedChannelRegistry<Window> _senders;
    private readonly int _window;
    private readonly object _gate = new();

    private long _admitted;
    private long _duplicates;
    private long _unsequenced;

    public DuplicateFilter(int window = DefaultWindow, int senders = DefaultSenders)
    {
        _window = Math.Max(1, window);
        _senders = new BoundedChannelRegistry<Window>(Math.Max(1, senders));
    }

    /// <summary>Samples admitted because they had not been seen before.</summary>
    public long Admitted { get { lock (_gate) return _admitted; } }

    /// <summary>Samples refused because this host already had them.</summary>
    public long Duplicates { get { lock (_gate) return _duplicates; } }

    /// <summary>
    /// Samples that carried no sequence, and so were admitted without being checked.
    /// </summary>
    /// <remarks>
    /// Counted separately and deliberately. Without it, a link whose sender emits no sequence at
    /// all reports zero duplicates — indistinguishable from a link that is being checked and is
    /// clean. That is the same "silence looks like health" failure this product is organised
    /// around, and an operator reading a duplicate count needs to know whether anything was
    /// actually watching.
    /// </remarks>
    public long Unsequenced { get { lock (_gate) return _unsequenced; } }

    /// <summary>How many senders are being tracked, and how many fell off the end.</summary>
    public int TrackedSenders => _senders.Count;

    /// <inheritdoc cref="BoundedChannelRegistry{TState}.Evictions"/>
    public long SenderEvictions => _senders.Evictions;

    /// <summary>Whether this sample should be taken. False when it has already been taken.</summary>
    /// <param name="nodeId">The node the sample describes.</param>
    /// <param name="epoch">The sending process's epoch, or null when it sent none.</param>
    /// <param name="sequence">The sender's per-node counter, or null when it sent none.</param>
    public bool Admit(string nodeId, string? epoch, long? sequence)
    {
        if (string.IsNullOrEmpty(epoch) || sequence is not { } seq)
        {
            lock (_gate) _unsequenced++;
            return true;
        }

        Window window = _senders.GetOrAdd($"{nodeId}{epoch}", _ => new Window(), out _);

        lock (_gate)
        {
            if (!window.Seen.Add(seq))
            {
                _duplicates++;
                return false;
            }

            window.Order.Enqueue(seq);
            while (window.Order.Count > _window)
            {
                window.Seen.Remove(window.Order.Dequeue());
            }

            _admitted++;
            return true;
        }
    }
}
