using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// Checking a rule set against the profile it is meant to feed.
/// </summary>
/// <remarks>
/// Every fault this reports has the same shape: the mapping is accepted, the data arrives, and
/// something downstream that was supposed to judge it quietly matches nothing. A band declared on
/// <c>psfb.output_voltage</c> against readings still arriving as <c>Vout</c> never fires, and a
/// rule that never fires is indistinguishable from a machine that is behaving. So these are said
/// at start-up, where somebody is still looking, rather than discovered weeks later by the alarm
/// that did not sound.
/// <para>
/// Warnings, not refusals. A rig is often commissioned channel by channel, and a hub that refused
/// to start until every name was mapped would be one nobody could use on the first day.
/// </para>
/// </remarks>
public static class RoutingRuleAudit
{
    /// <summary>What the rules and the profile disagree about, in the order a reader needs it.</summary>
    public static IReadOnlyList<string> Check(
        IReadOnlyList<RoutingRule>? rules, MonitoringProfile? profile)
    {
        var findings = new List<string>();
        if (rules is null || profile is null) return findings;

        Dictionary<string, ProfileChannel> declared =
            profile.Channels.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        foreach (RoutingRule rule in rules)
        {
            foreach ((string wireName, ChannelAlias alias) in rule.NameMap)
            {
                if (!declared.TryGetValue(alias.Channel, out ProfileChannel? channel))
                {
                    findings.Add(
                        $"'{wireName}' is mapped to {alias.Channel}, which profile "
                        + $"{profile.Id} does not declare — no band, computed channel or twin "
                        + "placement will act on it.");
                    continue;
                }

                // The unit check is the one that hides. LimitMonitor refuses to judge a channel
                // whose unit disagrees with the band's, so the reading arrives, charts, and is
                // never compared against anything.
                string effective = string.IsNullOrEmpty(alias.Unit) ? channel.Unit : alias.Unit;
                if (!string.IsNullOrEmpty(channel.Unit)
                    && !string.Equals(effective, channel.Unit, StringComparison.Ordinal))
                {
                    findings.Add(
                        $"'{wireName}' arrives as {alias.Channel} in {effective}, and the profile "
                        + $"declares it in {channel.Unit} — a band written in {channel.Unit} will "
                        + "refuse to judge it. Set \"unit\" and \"gain\" on the mapping.");
                }
            }
        }

        return findings;
    }

    /// <summary>Channels the profile declares that no rule maps anything onto.</summary>
    /// <remarks>
    /// The other half of the same question, and the one an operator asks first: which of the things
    /// this rig is supposed to report is nothing arriving for? Reported separately from the
    /// mistakes above because it is not necessarily one — a rig may be commissioned in stages, and
    /// a device that already speaks the declared ids needs no mapping at all.
    /// </remarks>
    public static IReadOnlyList<string> Unmapped(
        IReadOnlyList<RoutingRule>? rules, MonitoringProfile? profile)
    {
        if (profile is null) return [];

        var mapped = new HashSet<string>(
            (rules ?? []).SelectMany(r => r.NameMap.Values).Select(a => a.Channel),
            StringComparer.OrdinalIgnoreCase);

        return profile.Channels
            .Select(c => c.Id)
            .Where(id => !mapped.Contains(id))
            .ToList();
    }
}
