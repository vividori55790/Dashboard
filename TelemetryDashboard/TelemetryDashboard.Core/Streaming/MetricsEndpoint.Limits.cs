using System.Collections.Generic;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// The declared engineering limits: the only per-channel judgement this endpoint exports.
/// </summary>
/// <remarks>
/// A limit is the baseline in the sense that matters to a monitoring system. A rolling z-score asks
/// how unusual a reading is against the channel's own recent history, so a bus that settles at 460 V
/// and stays there becomes normal to it within a minute; a limit asks whether the reading is safe,
/// which does not move.
///
/// <para>
/// <b>The anomaly verdict is deliberately not exported, and this is a defect found rather than a
/// scope cut.</b> The only per-channel verdict reachable from this server is the one on the DVR
/// timeline, and <c>TelemetryFrameRecorder</c> stamps a frame's single <c>anomalyScore</c> onto
/// <em>every</em> numeric leaf of that frame -- so the recorded verdict for
/// <c>NODE.bus_voltage.predicted</c> is the score of <c>NODE.bus_voltage</c>, and the same again for
/// <c>lateBySec</c> and every other field beside it. Exporting that would publish a forecast's
/// anomaly score as the forecast's own verdict: ARCHITECTURE §7's "anomaly score of an anomaly
/// score", in a new place and addressed to somebody else's alerting rules. The DVR is also stamped
/// on a timeline counting from year one rather than from 1970, so nothing derived from it can be
/// exported as a timestamp without conversion. Saying nothing is the house answer until the
/// attribution is fixed, and the fix belongs in the recorder rather than here.
/// </para>
/// </remarks>
public static partial class MetricsEndpoint
{
    private static void WriteLimits(Document document, TelemetryStreamingServer server)
    {
        if (server.Limits is not { } monitor) return;

        IReadOnlyList<LimitMonitor.RuleState> rules = monitor.Snapshot();

        document.Open("limits_declared", "gauge",
            "Engineering limits in force on this host.")
            .Sample(monitor.Rules.Count);

        // The number an operator needs before trusting a quiet alarm list. A host with four limits
        // and four of them unarmed is not a calm plant.
        int unarmed = 0;
        foreach (LimitMonitor.RuleState rule in rules)
        {
            if (!IsArmed(rule)) unarmed++;
        }

        document.Open("limits_unarmed", "gauge",
            "Limits that cannot fire: nothing matches the channel they name, or the samples "
            + "arriving report a unit the limit was not written in.")
            .Sample(unarmed);

        Family armed = document.Open("limit_armed", "gauge",
            "1 when a limit is actually watching a channel. 0 means it is declared and cannot "
            + "fire -- the channel label is then the name the operator wrote, which may be a "
            + "channel that does not exist on this host.");

        foreach (LimitMonitor.RuleState rule in rules)
        {
            armed.Sample(IsArmed(rule) ? 1.0 : 0.0, "limit", rule.Declaration, "channel", rule.Channel);
        }

        WriteArmedRules(document, rules);
    }

    /// <summary>
    /// The three families that only an armed limit may appear in.
    /// </summary>
    /// <remarks>
    /// This is where the rule bites hardest in this file, and where the mistake is easiest. A limit
    /// on a misspelled channel has evaluated nothing and is silent; a limit on a healthy converter
    /// is also silent. Reporting the first as <c>limit_breached 0</c> tells an alerting system that
    /// an unprotected machine is inside its band, which is not a weaker claim than a false alarm --
    /// it is the claim that suppresses the true one.
    /// <para>
    /// So an unarmed rule appears only in <c>limit_armed</c>, at 0, and a consumer wanting the full
    /// picture reads the two families together. That is the same split <c>/api/limits</c> draws
    /// between <c>Never</c>, <c>Unarmed</c> and <c>Watching</c>.
    /// </para>
    /// </remarks>
    private static void WriteArmedRules(Document document, IReadOnlyList<LimitMonitor.RuleState> rules)
    {
        Family breached = document.Open("limit_breached", "gauge",
            "1 when the last sample a limit evaluated was outside its band. Absent for a limit "
            + "that has evaluated nothing or cannot fire, because a zero there reports an "
            + "unprotected channel as a safe one.");

        foreach (LimitMonitor.RuleState rule in rules)
        {
            if (IsArmed(rule)) breached.Sample(rule.InBreach ? 1.0 : 0.0, "limit", rule.Declaration, "channel", rule.Channel);
        }

        Family evaluated = document.Open("limit_evaluated_total", "counter",
            "Samples a limit has actually checked on a channel. Its rate is the evidence behind "
            + "the gauge above.");

        foreach (LimitMonitor.RuleState rule in rules)
        {
            if (IsArmed(rule)) evaluated.Sample(rule.Evaluated, "limit", rule.Declaration, "channel", rule.Channel);
        }

        Family entries = document.Open("limit_entries_total", "counter",
            "Times a channel crossed from inside a limit's band to outside it. Counts excursions "
            + "rather than samples, so one long breach is one, not a rate.");

        foreach (LimitMonitor.RuleState rule in rules)
        {
            if (IsArmed(rule)) entries.Sample(rule.Entries, "limit", rule.Declaration, "channel", rule.Channel);
        }
    }

    /// <summary>Whether a rule is in a position to fire at all.</summary>
    /// <remarks>
    /// Both refusals in one predicate, because both produce the same silence and neither means the
    /// machine is protected: a rule nothing has ever matched, and a rule disarmed by a unit it does
    /// not understand.
    /// </remarks>
    private static bool IsArmed(LimitMonitor.RuleState rule) =>
        rule.UnitMismatch is null && rule.Evaluated > 0;
}
