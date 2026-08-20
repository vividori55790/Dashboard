using System;
using System.Threading;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// One client's standing request, plus the clock that keeps it to the rate it asked for.
/// </summary>
public sealed class TelemetrySubscription
{
    private long _framesSent;
    private double _nextDueSec;

    public TelemetrySubscription(SubscriptionOptions options, double nowSec)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _nextDueSec = nowSec;
    }

    public SubscriptionOptions Options { get; }

    /// <summary>Reduced frames delivered to this client.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Wall-clock instant the next frame becomes due.</summary>
    public double NextDueSec => Volatile.Read(ref _nextDueSec);

    /// <summary>
    /// Takes the right to send one frame now, or refuses because the interval has not elapsed.
    /// </summary>
    /// <remarks>
    /// The next due instant is advanced from now rather than from the previous one. Advancing from
    /// the previous one lets a client that stalled accrue a backlog and then receive a burst at
    /// many times the rate it asked for, which is the failure this whole path exists to prevent.
    /// </remarks>
    public bool TryClaimSendSlot(double nowSec)
    {
        lock (this)
        {
            if (nowSec < _nextDueSec) return false;
            _nextDueSec = nowSec + Options.IntervalSec;
            return true;
        }
    }

    /// <summary>Records a frame that actually reached the transport.</summary>
    public void MarkSent() => Interlocked.Increment(ref _framesSent);
}
