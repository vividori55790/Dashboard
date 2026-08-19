using System;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Posts an anomaly to a Slack webhook, throttled per channel.
/// </summary>
/// <remarks>
/// The client this drives has had retry-on-429 for a while and no caller at all, which meant the
/// headless host — the one deployment that runs unattended and most needs to shout — had no way to
/// tell anyone anything. It relays only judged anomalies: a warm-up sample carries no verdict, and
/// paging someone about a channel the host has not finished learning is how an alert channel gets
/// muted.
/// </remarks>
public sealed class SlackAlertRelay : IAsyncDisposable
{
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
    public void OnSampleScored(object? sender, ScoredSample sample)
    {
        if (sample.IsAnomaly is not true) return;

        Interlocked.Increment(ref _considered);

        if (!_throttle.ShouldSend(sample.Channel, out int suppressed))
        {
            Interlocked.Increment(ref _throttled);
            return;
        }

        _queue.Offer(Compose(sample, suppressed));
    }

    /// <summary>Builds the alert text. Public so its wording can be asserted directly.</summary>
    public static string Compose(ScoredSample sample, int suppressedSinceLast)
    {
        string body = $"*Anomaly* {sample.Describe()} at {sample.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC";

        if (sample.IsSimulated)
        {
            body += "\n_Source is the simulator, not measured hardware._";
        }

        if (suppressedSinceLast > 0)
        {
            body += $"\n{suppressedSinceLast} further anomalies on this channel were held back during "
                    + "the last quiet period; the condition did not clear.";
        }

        return body;
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
