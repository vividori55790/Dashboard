using System;
using System.Collections.Generic;
using System.Globalization;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// Reads a retention policy written the way an operator would type one.
/// </summary>
/// <remarks>
/// <c>raw=7d,minute=90d,hour=2y</c>. Each clause names a tier and how long it is kept; a tier left
/// out is kept forever, which is the safe direction for a setting whose mistakes are permanent.
/// <para>
/// Everything here refuses rather than guesses. A policy is the only thing in this product that
/// destroys data, so a clause nobody can read is not something to interpret generously: "1w" and
/// "seven" and "7 days" all fail and say what was expected, because the alternative is a host that
/// starts up having quietly decided a different number of days than the person typing meant.
/// </para>
/// </remarks>
public static class RetentionSpec
{
    /// <summary>Units a duration may be written in.</summary>
    private static readonly (char Suffix, Func<double, TimeSpan> Build)[] Units =
    {
        ('s', seconds => TimeSpan.FromSeconds(seconds)),
        ('m', minutes => TimeSpan.FromMinutes(minutes)),
        ('h', hours => TimeSpan.FromHours(hours)),
        ('d', days => TimeSpan.FromDays(days)),
        ('w', weeks => TimeSpan.FromDays(weeks * 7)),
        ('y', years => TimeSpan.FromDays(years * 365))
    };

    /// <summary>Parses <paramref name="spec"/>, or explains why it cannot be read.</summary>
    public static bool TryParse(string? spec, out RetentionPolicy policy, out string? error)
    {
        policy = RetentionPolicy.Disabled;
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "a retention policy is required, e.g. \"raw=7d,minute=90d\".";
            return false;
        }

        var kept = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        foreach (string clause in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] halves = clause.Split('=', StringSplitOptions.TrimEntries);
            if (halves.Length != 2)
            {
                error = $"'{clause}' is not a clause; write <tier>=<duration>, e.g. raw=7d.";
                return false;
            }

            if (!IsKnownTier(halves[0]))
            {
                error = $"'{halves[0]}' is not a tier. Known: raw, second, minute, hour.";
                return false;
            }

            if (!kept.TryAdd(halves[0], TimeSpan.Zero))
            {
                // Two clauses for one tier is a typo with a destructive outcome, and picking either
                // one silently means the operator's file and the host's behaviour disagree.
                error = $"'{halves[0]}' is given twice; one duration per tier.";
                return false;
            }

            if (!TryDuration(halves[1], out TimeSpan duration, out string? why))
            {
                error = $"'{clause}': {why}";
                return false;
            }

            kept[halves[0]] = duration;
        }

        policy = new RetentionPolicy
        {
            Enabled = true,
            RawRetention = kept.TryGetValue("raw", out TimeSpan raw) ? raw : TimeSpan.MaxValue,
            SecondRetention = kept.TryGetValue("second", out TimeSpan second) ? second : null,
            MinuteRetention = kept.TryGetValue("minute", out TimeSpan minute) ? minute : null,
            HourRetention = kept.TryGetValue("hour", out TimeSpan hour) ? hour : null
        }.Validated();

        return true;
    }

    private static bool IsKnownTier(string tier) =>
        tier.Equals("raw", StringComparison.OrdinalIgnoreCase)
        || tier.Equals("second", StringComparison.OrdinalIgnoreCase)
        || tier.Equals("minute", StringComparison.OrdinalIgnoreCase)
        || tier.Equals("hour", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads <c>7d</c>, <c>90m</c>, <c>2y</c>.</summary>
    /// <remarks>
    /// A duration of zero is refused rather than treated as "keep nothing". Somebody writing
    /// <c>raw=0d</c> almost certainly means to disable retention for that tier, and the reading
    /// that destroys everything on the first prune is the wrong one to guess.
    /// </remarks>
    private static bool TryDuration(string text, out TimeSpan duration, out string? error)
    {
        duration = default;
        error = null;

        if (text.Length < 2)
        {
            error = "a duration needs a number and a unit, e.g. 7d.";
            return false;
        }

        char suffix = char.ToLowerInvariant(text[^1]);
        foreach ((char unit, Func<double, TimeSpan> build) in Units)
        {
            if (unit != suffix) continue;

            if (!double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
                || !double.IsFinite(amount))
            {
                error = $"'{text[..^1]}' is not a number.";
                return false;
            }

            if (amount <= 0)
            {
                error = "a duration must be greater than zero; leave the tier out to keep it forever.";
                return false;
            }

            duration = build(amount);
            return true;
        }

        error = $"'{suffix}' is not a unit. Known: s, m, h, d, w, y.";
        return false;
    }
}
