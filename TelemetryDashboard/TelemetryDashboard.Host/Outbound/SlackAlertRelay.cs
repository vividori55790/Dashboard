using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Posts anomalies and engineering-limit events to a Slack webhook, throttled per subject.
/// </summary>
/// <remarks>
/// The client this drives has had retry-on-429 for a while and no caller at all, which meant the
/// headless host — the one deployment that runs unattended and most needs to shout — had no way to
/// tell anyone anything. It relays only judged anomalies: a warm-up sample carries no verdict, and
/// paging someone about a channel the host has not finished learning is how an alert channel gets
/// muted.
/// </remarks>
public sealed partial class SlackAlertRelay : IAsyncDisposable
{
    /// <summary>Line break inside a Slack message body.</summary>
    private const string NewLine = "\n";

    /// <summary>Default quiet period per channel.</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    private readonly ISlackClient _slack;
    private readonly string _webhookUrl;
    private readonly AlertThrottle _throttle;
    private readonly OutboundQueue<string> _queue;

    private long _considered;
    private long _throttled;

    public SlackAlertRelay(ISlackClient slack, string webhookUrl, TimeSpan? cooldown = null)
    {
        _slack = slack ?? throw new ArgumentNullException(nameof(slack));
        _webhookUrl = webhookUrl ?? throw new ArgumentNullException(nameof(webhookUrl));
        _throttle = new AlertThrottle(cooldown ?? DefaultCooldown);
        _queue = new OutboundQueue<string>("slack", capacity: 256, SendAsync);
    }

    /// <summary>Anomalies seen, before throttling.</summary>
    public long Considered => Interlocked.Read(ref _considered);

    /// <summary>Anomalies held back by the cooldown.</summary>
    public long Throttled => Interlocked.Read(ref _throttled);

    /// <summary>Handler for the publisher's scored-sample event.</summary>
    /// <remarks>
    /// Limits first, and on their own throttle. They were not relayed at all until now, so a
    /// converter sitting steadily outside its safe band told nobody: the rolling detector does not
    /// find a steady value unusual — measured at |z| never above 1.94 across 107 samples held
    /// 42–119 V past a hard limit — and this relay only forwarded what that detector flagged.
    /// <para>
    /// A separate throttle key, because sharing the channel's would let an ordinary anomaly's
    /// quiet period swallow the one message that says a machine is outside what it may safely do.
    /// </para>
    /// </remarks>
    public void OnSampleScored(object? sender, ScoredSample sample)
    {
        foreach (BreachedLimit outcome in sample.LimitTransitions)
        {
            // Crossings and recoveries only. A sustained breach sends nothing further: the
            // crossing already said it, /api/limits carries how long it has lasted, and a message
            // per sample is how an alert channel gets muted.
            Interlocked.Increment(ref _considered);

            if (outcome.Transition == Core.Analytics.LimitTransition.Cleared)
            {
                if (!SendRecovery(sample, outcome)) Interlocked.Increment(ref _throttled);
                continue;
            }

            if (!_throttle.ShouldSend(LimitKey(sample, outcome), out int held))
            {
                Interlocked.Increment(ref _throttled);
                continue;
            }

            lock (_announced) _announced.Add(LimitKey(sample, outcome));
            _queue.Offer(ComposeLimit(sample, outcome, held));
        }

        if (sample.IsAnomaly is not true) return;

        Interlocked.Increment(ref _considered);

        if (!_throttle.ShouldSend(sample.Channel, out int suppressed))
        {
            Interlocked.Increment(ref _throttled);
            return;
        }

        _queue.Offer(Compose(sample, suppressed));
    }

    /// <summary>Throttle key for a limit event: the rule on the channel, not the channel.</summary>
    private static string LimitKey(ScoredSample sample, BreachedLimit outcome) =>
        $"limit|{sample.Channel}|{outcome.Rule.Declaration}";

    /// <summary>Rules whose breach was announced and whose recovery has not been.</summary>
    private readonly System.Collections.Generic.HashSet<string> _announced = new(StringComparer.Ordinal);

    /// <summary>
    /// Sends a recovery, but only for a breach this relay actually announced.
    /// </summary>
    /// <remarks>
    /// Not throttled, and that is deliberate: a recovery can never be more frequent than the
    /// crossings that were sent, because it is only sent when one is outstanding. Putting it
    /// behind the same quiet period as the crossing is what the first version did, and a live run
    /// showed the consequence — the host logged four crossings and four recoveries, and the
    /// webhook received one message. An alert channel that says "it broke" and never "it is fine"
    /// leaves an operator believing a machine is still out of band hours after it recovered, which
    /// is worse than not alerting at all.
    /// </remarks>
    private bool SendRecovery(ScoredSample sample, BreachedLimit outcome)
    {
        string key = LimitKey(sample, outcome);

        lock (_announced)
        {
            // Nothing outstanding: the breach was itself suppressed, so announcing its end would
            // be the first this reader hears of either.
            if (!_announced.Remove(key)) return false;
        }

        _queue.Offer(ComposeLimit(sample, outcome, suppressedSinceLast: 0));
        return true;
    }


    private Task SendAsync(string message, CancellationToken cancellationToken) =>
        _slack.SendAlertAsync(_webhookUrl, message);

    /// <summary>One line for the shutdown report, or null when nothing was ever relayed.</summary>
    public string? Summary()
    {
        string? queue = _queue.Summary();
        if (queue is null && Considered == 0) return null;

        string line = queue ?? "slack: nothing delivered";
        if (Throttled > 0) line += $" {Throttled} further anomalies were throttled.";
        return line;
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();
}
