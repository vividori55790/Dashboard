using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Turning declared limits into the monitor that enforces them.
/// </summary>
/// <remarks>
/// In Core because both front ends need the same answer from the same text. It lived in the host,
/// so the desktop shell — the thing an engineer is actually sitting in front of at a bench — could
/// not reach it and evaluated no limits at all: it loaded a profile that states the rig's safe
/// bands, drew every channel, and never once compared a reading against them.
/// <para>
/// A declaration that does not parse is a warning rather than a failure. One bad clause in a
/// profile must not take the other six with it, and the operator has to be told which one was
/// dropped — a limit silently missing has no symptom, because a rule that never fires looks
/// exactly like a machine that is behaving.
/// </para>
/// </remarks>
public static class LimitDeclarations
{
    /// <param name="Monitor">Null when nothing usable was declared.</param>
    /// <param name="Warnings">Declarations that did not parse, each with the reason.</param>
    public readonly record struct Resolution(LimitMonitor? Monitor, IReadOnlyList<string> Warnings);

    /// <summary>Parses <paramref name="declarations"/>, keeping what can be read.</summary>
    /// <remarks>
    /// Later wins on an identical declaration, so repeating one is not two rules watching the same
    /// band and announcing every excursion twice.
    /// </remarks>
    public static Resolution Resolve(IEnumerable<string>? declarations)
    {
        var warnings = new List<string>();
        var parsed = new List<ChannelLimit>();

        foreach (string declaration in declarations ?? Array.Empty<string>())
        {
            try
            {
                parsed.Add(ChannelLimit.Parse(declaration));
            }
            catch (FormatException ex)
            {
                warnings.Add($"limit '{declaration}' was skipped: {ex.Message}");
            }
        }

        List<ChannelLimit> rules = parsed
            .GroupBy(rule => rule.Declaration, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();

        return new Resolution(rules.Count == 0 ? null : new LimitMonitor(rules), warnings);
    }
}
