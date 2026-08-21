using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Applies engineering limits to arriving samples and remembers what each rule has seen.
/// </summary>
/// <remarks>
/// A limit is declared against a channel the way an expression names one — <c>dab.bus_voltage</c> —
/// while samples arrive keyed by node, as <c>SIM:COM3.dab.bus_voltage</c>. A rule matches every
/// channel whose name ends with it, which is the right reading for a safety limit: a ceiling on bus
/// voltage constrains every converter that reports one, and a rule meant for a single device is
/// written with the node in it.
/// <para>
/// State is per rule <em>and per channel</em>. One rule watching four converters has to be able to
/// say which of them breached, and a shared flag would clear the alarm on one node because another
/// recovered.
/// </para>
/// </remarks>
public sealed partial class LimitMonitor
{
    private sealed class Tracked
    {
        public bool InBreach;
        public long Evaluated;
        public long Breaches;
        public long Entries;
        public double? LastValue;
        public DateTime? LastSeenUtc;
        public DateTime? BreachSinceUtc;
        public string? Reason;
        public string? UnitMismatch;
    }

    private readonly IReadOnlyList<ChannelLimit> _rules;
    private readonly ConcurrentDictionary<(string Rule, string Channel), Tracked> _state = new();

    /// <summary>Rules whose channel resolved to nothing, cached so the match runs once per channel.</summary>
    private readonly ConcurrentDictionary<string, ChannelLimit[]> _matches = new(StringComparer.Ordinal);

    public LimitMonitor(IReadOnlyList<ChannelLimit>? rules)
    {
        _rules = rules ?? Array.Empty<ChannelLimit>();
    }

    /// <summary>Rules this monitor was given.</summary>
    public IReadOnlyList<ChannelLimit> Rules => _rules;

    /// <summary>Whether any rule is currently breached.</summary>
    public bool AnyBreached => _state.Values.Any(t => t.InBreach);

    /// <summary>
    /// Evaluates one sample and reports what changed.
    /// </summary>
    /// <param name="channel">Full channel name as the wire spells it, node included.</param>
    /// <returns>
    /// One outcome per matching rule. Empty when no rule watches this channel, which is the
    /// ordinary case and is not an error: limits are declared for the quantities somebody has
    /// agreed a number for.
    /// </returns>
    public IReadOnlyList<(ChannelLimit Rule, LimitTransition Transition)> Evaluate(
        string channel, double value, string? unit, DateTime atUtc)
    {
        ChannelLimit[] matching = _matches.GetOrAdd(channel ?? string.Empty, Match);
        if (matching.Length == 0) return Array.Empty<(ChannelLimit, LimitTransition)>();

        var outcomes = new List<(ChannelLimit, LimitTransition)>(matching.Length);

        foreach (ChannelLimit rule in matching)
        {
            Tracked tracked = _state.GetOrAdd((rule.Declaration, channel!), _ => new Tracked());

            lock (tracked)
            {
                tracked.LastValue = value;
                tracked.LastSeenUtc = atUtc;

                if (!rule.UnitAgrees(unit))
                {
                    // Recorded rather than skipped. A limit that cannot fire is the one failure
                    // mode of an alarm that has no symptom at all.
                    tracked.UnitMismatch =
                        $"limit is written in {rule.Unit}; this channel reports " +
                        (string.IsNullOrWhiteSpace(unit) ? "no unit" : unit);
                    outcomes.Add((rule, LimitTransition.UnitMismatch));
                    continue;
                }

                tracked.UnitMismatch = null;
                tracked.Evaluated++;

                bool breached = rule.IsBreached(value);
                LimitTransition transition;

                if (breached)
                {
                    tracked.Breaches++;
                    tracked.Reason = rule.Explain(value);

                    if (tracked.InBreach)
                    {
                        transition = LimitTransition.Sustained;
                    }
                    else
                    {
                        tracked.InBreach = true;
                        tracked.Entries++;
                        tracked.BreachSinceUtc = atUtc;
                        transition = LimitTransition.Entered;
                    }
                }
                else if (tracked.InBreach)
                {
                    tracked.InBreach = false;
                    tracked.BreachSinceUtc = null;
                    transition = LimitTransition.Cleared;
                }
                else
                {
                    transition = LimitTransition.None;
                }

                outcomes.Add((rule, transition));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Every rule, with what it has seen — including rules no sample has ever reached.
    /// </summary>
    /// <remarks>
    /// A rule that has evaluated nothing is reported with zero counts rather than omitted. Leaving
    /// it out would make a limit on a misspelled channel indistinguishable from one that is
    /// watching quietly, and the whole point of writing the limit down was to be protected.
    /// </remarks>
    public IReadOnlyList<RuleState> Snapshot()
    {
        var rows = new List<RuleState>();

        foreach (ChannelLimit rule in _rules)
        {
            var seen = _state
                .Where(kv => kv.Key.Rule == rule.Declaration)
                .OrderBy(kv => kv.Key.Channel, StringComparer.Ordinal)
                .ToArray();

            if (seen.Length == 0)
            {
                rows.Add(new RuleState { Declaration = rule.Declaration, Channel = rule.Channel });
                continue;
            }

            foreach (var kv in seen)
            {
                Tracked t = kv.Value;
                lock (t)
                {
                    rows.Add(new RuleState
                    {
                        Declaration = rule.Declaration,
                        Channel = kv.Key.Channel,
                        InBreach = t.InBreach,
                        Evaluated = t.Evaluated,
                        Breaches = t.Breaches,
                        Entries = t.Entries,
                        LastValue = t.LastValue,
                        LastSeenUtc = t.LastSeenUtc,
                        BreachSinceUtc = t.BreachSinceUtc,
                        Reason = t.Reason,
                        UnitMismatch = t.UnitMismatch
                    });
                }
            }
        }

        return rows;
    }

    /// <summary>Rules that apply to a channel: an exact name, or a suffix after a dot.</summary>
    private ChannelLimit[] Match(string channel) =>
        _rules.Where(r =>
                string.Equals(r.Channel, channel, StringComparison.Ordinal)
                || channel.EndsWith("." + r.Channel, StringComparison.Ordinal))
            .ToArray();
}
