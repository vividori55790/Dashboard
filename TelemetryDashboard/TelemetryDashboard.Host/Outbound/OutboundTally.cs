using System.Threading;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// What one outbound relay actually did, and the sentence that reports it.
/// </summary>
/// <remarks>
/// Separate from the queue because the counting is the part that has to be right. A relay that
/// silently loses samples and a relay that delivers everything look identical from the outside;
/// the only thing that tells them apart is a tally somebody kept and printed. Keeping it here means
/// the wording can be asserted without standing up a queue and a network service behind it.
/// </remarks>
public sealed class OutboundTally
{
    private readonly string _name;
    private long _sent;
    private long _failed;
    private long _dropped;

    public OutboundTally(string name) => _name = name;

    /// <summary>Items the sender accepted.</summary>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>Items the sender rejected or threw on.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Items refused before they were ever sent, because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>True when shutdown gave up waiting for the sender rather than it finishing.</summary>
    public bool AbandonedOnShutdown { get; set; }

    public void CountSent() => Interlocked.Increment(ref _sent);

    public void CountFailed() => Interlocked.Increment(ref _failed);

    public void CountDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>One line for the shutdown report, or null when there is nothing to say.</summary>
    /// <remarks>
    /// Abandonment is reportable on its own. A relay that delivered nothing and then refused to
    /// stop is the most alarming of these states, and it is the one whose counters are all zero.
    /// </remarks>
    public string? Summary()
    {
        long sent = Sent, failed = Failed, dropped = Dropped;
        if (sent == 0 && failed == 0 && dropped == 0 && !AbandonedOnShutdown) return null;

        string line = $"{_name}: {sent} delivered";
        if (failed > 0) line += $", {failed} refused by the service";
        if (dropped > 0) line += $", {dropped} dropped locally (queue full)";
        line += ".";

        if (AbandonedOnShutdown)
        {
            line += " The sender did not stop when asked; shutdown continued without it.";
        }

        return line;
    }
}
