using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TelemetryDashboard.Core.Analytics;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/limits</c>: what every declared engineering limit has seen.
/// </summary>
/// <remarks>
/// The alarm a rolling detector structurally cannot raise. A z-score asks how unusual a reading is
/// against the channel's own recent history, so a bus that settles at 460 V and stays there becomes
/// normal to it within a minute — the baseline follows the fault in. A limit asks whether the
/// reading is safe, which does not change with what the channel has been doing lately.
/// <para>
/// Every declared rule is listed, including ones nothing has ever matched. A limit on a misspelled
/// channel is silent, and so is a limit on a healthy one; only this list separates them.
/// </para>
/// </remarks>
public static class LimitsEndpoint
{
    public sealed record LimitRow
    {
        public string Declaration { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;

        /// <summary>Never, Watching, Breached, or Unarmed.</summary>
        /// <remarks>
        /// <c>Never</c> means no sample has ever matched this rule — usually a name that does not
        /// exist. <c>Unarmed</c> means samples arrive but the unit disagrees, so the rule cannot
        /// fire. Both read as a quiet alarm and neither means the machine is protected.
        /// </remarks>
        public string Status { get; init; } = "Never";

        public bool InBreach { get; init; }
        public long Evaluated { get; init; }
        public long Breaches { get; init; }
        public long Entries { get; init; }

        public double? LastValue { get; init; }
        public string? LastSeenUtc { get; init; }
        public string? BreachSinceUtc { get; init; }
        public string? Reason { get; init; }
    }

    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string? Reason { get; init; }

        public int Declared { get; init; }

        /// <summary>Rules currently outside their band.</summary>
        public int Breached { get; init; }

        /// <summary>Rules that cannot fire: nothing matches them, or the unit disagrees.</summary>
        /// <remarks>
        /// Counted at the top level because it is the number an operator needs before trusting a
        /// quiet alarm list. A host with four limits and four of them unarmed is not a calm plant.
        /// </remarks>
        public int Unarmed { get; init; }

        public IReadOnlyList<LimitRow> Rules { get; init; } = Array.Empty<LimitRow>();
    }

    public static Result Query(LimitMonitor? monitor)
    {
        if (monitor is null)
        {
            return new Result
            {
                Status = "Error",
                Reason = "this host checks no engineering limits; declare one with " +
                         "--limit \"channel[unit] in lo..hi\""
            };
        }

        List<LimitRow> rows = monitor.Snapshot().Select(Row).ToList();

        return new Result
        {
            Declared = monitor.Rules.Count,
            Breached = rows.Count(r => r.InBreach),
            Unarmed = rows.Count(r => r.Status is "Never" or "Unarmed"),
            Rules = rows
        };
    }

    private static LimitRow Row(LimitMonitor.RuleState state) => new()
    {
        Declaration = state.Declaration,
        Channel = state.Channel,
        Status = state.UnitMismatch is not null ? "Unarmed"
               : state.Evaluated == 0 ? "Never"
               : state.InBreach ? "Breached"
               : "Watching",
        InBreach = state.InBreach,
        Evaluated = state.Evaluated,
        Breaches = state.Breaches,
        Entries = state.Entries,
        LastValue = state.LastValue,
        LastSeenUtc = Iso(state.LastSeenUtc),
        BreachSinceUtc = Iso(state.BreachSinceUtc),
        Reason = state.UnitMismatch ?? state.Reason
    };

    private static string? Iso(DateTime? at) =>
        at?.ToString("o", CultureInfo.InvariantCulture);
}
