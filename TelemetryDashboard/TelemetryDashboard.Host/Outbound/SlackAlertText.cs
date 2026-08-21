using System;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// What an alert says, separated from when it is sent.
/// </summary>
/// <remarks>
/// The two change for different reasons and are checked differently: the throttling rules need a
/// clock and a sequence of samples, and the wording needs neither. Everything here is pure and
/// public, so a message can be asserted character by character without a webhook, a queue or a
/// delay.
/// </remarks>
public sealed partial class SlackAlertRelay
{
    /// <summary>
    /// Builds the message for a limit crossing or recovery. Public so its wording can be asserted.
    /// </summary>
    /// <remarks>
    /// Names the rule and which side was crossed rather than a severity word. "413 V is above the
    /// 300 V ceiling (grid.voltage[V] &lt; 300)" tells an operator what to do; "CRITICAL" tells
    /// them to open the dashboard and work it out.
    /// </remarks>
    public static string ComposeLimit(ScoredSample sample, BreachedLimit outcome, int suppressedSinceLast)
    {
        string body = outcome.Transition == Core.Analytics.LimitTransition.Cleared
            ? $"*Limit cleared* {sample.Channel} is back inside `{outcome.Rule.Declaration}` "
              + $"at {sample.Value:0.###}{UnitSuffix(sample)}"
            : $"*Outside limit* {sample.Channel}: {outcome.Rule.Explain(sample.Value)} "
              + $"(`{outcome.Rule.Declaration}`)";

        body += $"\n{sample.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC";

        // Said explicitly, because a limit fires with or without a verdict and an operator reading
        // an alert with no sigma in it would otherwise wonder what the detector thought.
        if (sample.ZScore is null && outcome.Transition != Core.Analytics.LimitTransition.Cleared)
        {
            body += "\n_No anomaly verdict: the detector has no baseline for this channel yet. "
                    + "A limit does not need one._";
        }

        if (sample.IsSimulated)
        {
            body += "\n_Source is the simulator, not measured hardware._";
        }

        if (suppressedSinceLast > 0)
        {
            body += $"\n{suppressedSinceLast} further events on this rule were held back during the "
                    + "last quiet period.";
        }

        return body;
    }

    private static string UnitSuffix(ScoredSample sample) =>
        string.IsNullOrEmpty(sample.Unit) ? string.Empty : " " + sample.Unit;


    /// <summary>Builds the alert text. Public so its wording can be asserted directly.</summary>
    public static string Compose(ScoredSample sample, int suppressedSinceLast)
    {
        string body = $"*Anomaly* {sample.Describe()} at {sample.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC";

        // On its own line, and named as context rather than as the headline: the limit has already
        // sent its own message, and this only says the two are about the same channel.
        if (sample.DescribeLimits() is { Length: > 0 } outside)
        {
            body += NewLine + "_" + outside + ", which raised its own alert._";
        }

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
}
