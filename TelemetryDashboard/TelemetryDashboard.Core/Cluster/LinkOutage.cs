using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>An interval during which this host was not connected to its upstream.</summary>
/// <param name="BeganUtc">When the connection was lost.</param>
/// <param name="EndedUtc">When it was re-established, or null while it is still down.</param>
/// <param name="Fault">What ended it, or null when the peer simply closed the stream.</param>
public readonly record struct LinkOutage(DateTime BeganUtc, DateTime? EndedUtc, string? Fault)
{
    /// <summary>How long it lasted, measured against <paramref name="now"/> while still open.</summary>
    public TimeSpan Duration(DateTime now) => (EndedUtc ?? now) - BeganUtc;

    /// <summary>Whether the link is still down.</summary>
    public bool Open => EndedUtc is null;
}

/// <summary>
/// What this host missed while it could not reach its upstream, and for how long.
/// </summary>
/// <remarks>
/// ARCHITECTURE §4 is titled "A node must survive alone, <em>and say so when it was</em>", and only
/// the first half was here. <c>SseTelemetrySource</c> reconnects and counts reconnections — its own
/// summary says why: "A feed that drops every thirty seconds and silently resumes looks identical
/// to a healthy one from the chart, and the gaps it leaves are exactly the intervals an operator
/// would otherwise read as quiet." It then wrote that to stderr, where a browser cannot see it and
/// a service running under a manager loses it.
/// <para>
/// A count is also not the fact. "Reconnected 4 times" and "was disconnected for four hours" are
/// different situations with the same counter, and the second is the one that puts a hole in a
/// chart. So the intervals are kept, not just tallied.
/// </para>
/// <para>
/// This deliberately does not claim data was lost. The peer may have had nothing to send. What it
/// claims is narrower and always true: for this window, nothing could have reached this host, so
/// anything the peer observed in it is absent from this host's history — which is exactly what an
/// operator needs before reading a flat stretch of chart as a quiet plant.
/// </para>
/// </remarks>
public sealed class LinkOutageLedger
{
    /// <summary>How many intervals are kept.</summary>
    /// <remarks>
    /// A flapping link produces one of these every few seconds, and the useful facts are the recent
    /// ones and the totals. <see cref="Count"/> and <see cref="Total"/> are cumulative and survive
    /// the window, so a link that dropped a thousand times still reports a thousand.
    /// </remarks>
    public const int Kept = 32;

    private readonly Queue<LinkOutage> _recent = new();
    private readonly object _gate = new();
    private LinkOutage? _open;
    private int _count;
    private TimeSpan _total;

    /// <summary>How many times the link has gone down.</summary>
    public int Count { get { lock (_gate) return _count; } }

    /// <summary>How long it has been down in total, closed intervals only.</summary>
    public TimeSpan Total { get { lock (_gate) return _total; } }

    /// <summary>Whether the link is down right now.</summary>
    public bool IsDown { get { lock (_gate) return _open is not null; } }

    /// <summary>Records that the connection has been lost.</summary>
    /// <remarks>
    /// Idempotent. A source that reports a fault and then reports the stream ending would otherwise
    /// open two intervals for one outage and double the total.
    /// </remarks>
    public void Dropped(DateTime whenUtc, string? fault)
    {
        lock (_gate)
        {
            if (_open is not null) return;
            _open = new LinkOutage(whenUtc, null, fault);
            _count++;
        }
    }

    /// <summary>Records that the connection is back, and returns the interval that just closed.</summary>
    /// <remarks>
    /// Returns it rather than only recording it, so a caller that wants to go and ask for what it
    /// missed has the window without racing the ledger to read it back.
    /// </remarks>
    public LinkOutage? Restored(DateTime whenUtc)
    {
        lock (_gate)
        {
            if (_open is not { } outage) return null;

            var closed = outage with { EndedUtc = whenUtc };
            _total += closed.Duration(whenUtc);
            _open = null;

            _recent.Enqueue(closed);
            while (_recent.Count > Kept) _recent.Dequeue();
            return closed;
        }
    }

    /// <summary>The recent intervals, newest last, with the open one included if there is one.</summary>
    public IReadOnlyList<LinkOutage> Recent()
    {
        lock (_gate)
        {
            var all = new List<LinkOutage>(_recent);
            if (_open is { } open) all.Add(open);
            return all;
        }
    }
}
