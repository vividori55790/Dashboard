using System.Collections.Generic;
using System.Threading;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// Every way a model can fail to answer, counted separately.
/// </summary>
/// <remarks>
/// One "errors" counter would be cheaper and useless. "The endpoint refused the connection",
/// "the endpoint took longer than we allow" and "the endpoint answered with something that is not a
/// score" call for three different people and three different fixes, and an operator staring at a
/// dashboard with no verdicts needs to know which one they have.
///
/// <para>The counters exist because the detector's honest behaviour — reporting no verdict — looks
/// identical to a detector that was never configured. These numbers are the difference between
/// "the model is down" and "the model was never asked".</para>
/// </remarks>
public sealed class InferenceTally
{
    private long _offered;
    private long _dropped;
    private long _accepted;
    private long _timedOut;
    private long _refused;
    private long _unusable;
    private long _stale;

    /// <summary>Windows handed to the dispatch queue.</summary>
    public long Offered => Interlocked.Read(ref _offered);

    /// <summary>Windows refused because the queue was full — the model is slower than the feed.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Scores that came back and were usable.</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Requests abandoned at the configured timeout.</summary>
    public long TimedOut => Interlocked.Read(ref _timedOut);

    /// <summary>Requests the transport or the server rejected outright.</summary>
    public long Refused => Interlocked.Read(ref _refused);

    /// <summary>Answers that arrived but carried no readable score.</summary>
    public long Unusable => Interlocked.Read(ref _unusable);

    /// <summary>Samples where the newest score was too old to be quoted about them.</summary>
    public long Stale => Interlocked.Read(ref _stale);

    internal void CountOffered() => Interlocked.Increment(ref _offered);
    internal void CountDropped() => Interlocked.Increment(ref _dropped);
    internal void CountAccepted() => Interlocked.Increment(ref _accepted);
    internal void CountTimedOut() => Interlocked.Increment(ref _timedOut);
    internal void CountRefused() => Interlocked.Increment(ref _refused);
    internal void CountUnusable() => Interlocked.Increment(ref _unusable);
    internal void CountStale() => Interlocked.Increment(ref _stale);

    /// <summary>True when the model has answered usefully at least once.</summary>
    public bool EverAnswered => Accepted > 0;

    /// <summary>One line for the shutdown report, or null when the model was never consulted.</summary>
    public string? Summary(string endpointId)
    {
        if (Offered == 0 && Dropped == 0) return null;

        var parts = new List<string> { $"{Accepted} scored of {Offered} offered" };
        if (Dropped > 0) parts.Add($"{Dropped} dropped (queue full)");
        if (TimedOut > 0) parts.Add($"{TimedOut} timed out");
        if (Refused > 0) parts.Add($"{Refused} refused");
        if (Unusable > 0) parts.Add($"{Unusable} unreadable");
        if (Stale > 0) parts.Add($"{Stale} samples left unjudged on a stale score");

        return $"inference {endpointId}: " + string.Join(", ", parts);
    }
}
