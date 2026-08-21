using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// An engineering limit on a channel: the band a quantity is allowed to occupy.
/// </summary>
/// <remarks>
/// This is the alarm a statistical detector structurally cannot raise. A z-score measures how
/// unusual a reading is against the channel's own recent history, so a bus that settles at 460 V
/// and stays there becomes <em>normal</em> to it within a minute — the baseline follows the fault
/// in. Every rolling detector in this codebase has that property, and no amount of tuning removes
/// it, because "unusual" and "unsafe" are different questions.
/// <para>
/// A limit answers the second one. It comes from a datasheet or a commissioning document, it does
/// not move, and it fires on the reading itself: a transformer above 110 °C is a fault at the
/// first sample and at the ten-thousandth.
/// </para>
/// <para>
/// The declared unit is checked against what arrives rather than assumed. A limit written in kV
/// and applied to a channel reporting volts is silent for every reading that matters, and there is
/// no symptom — the alarm simply never fires, which looks exactly like a healthy machine.
/// </para>
/// </remarks>
public sealed class ChannelLimit
{
    /// <summary>
    /// <c>channel[unit] in lo..hi</c>, or <c>channel[unit] &gt; x</c> and the other comparisons.
    /// </summary>
    private static readonly Regex Syntax = new(
        @"^\s*(?<channel>[A-Za-z_][A-Za-z0-9_.]*|\[[^\]]+\]\.[A-Za-z_][A-Za-z0-9_.]*)\s*" +
        @"(?:\[(?<unit>[^\]]*)\])?\s*" +
        @"(?:in\s+(?<lo>[-+0-9.eE]+)\s*\.\.\s*(?<hi>[-+0-9.eE]+)" +
        @"|(?<op>>=|<=|>|<)\s*(?<bound>[-+0-9.eE]+))\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private ChannelLimit(string declaration, string channel, string unit, double? min, double? max)
    {
        Declaration = declaration;
        Channel = channel;
        Unit = unit;
        Minimum = min;
        Maximum = max;
    }

    /// <summary>The rule as written, which is what an operator recognises in an alarm list.</summary>
    public string Declaration { get; }

    /// <summary>Channel this constrains, spelled as an expression input is.</summary>
    public string Channel { get; }

    /// <summary>Unit the limit is written in, or empty when none was stated.</summary>
    public string Unit { get; }

    /// <summary>Lowest permitted value, or null when the rule only has an upper bound.</summary>
    public double? Minimum { get; }

    /// <summary>Highest permitted value, or null when the rule only has a lower bound.</summary>
    public double? Maximum { get; }

    /// <summary>Parses a declaration, or explains why it is not one.</summary>
    /// <exception cref="FormatException">The declaration is malformed or describes no band.</exception>
    public static ChannelLimit Parse(string declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            throw new FormatException(
                "A limit needs a declaration, for example \"dab.bus_voltage[V] in 380..420\".");
        }

        Match match = Syntax.Match(declaration);
        if (!match.Success)
        {
            throw new FormatException(
                $"'{declaration}' is not a limit. Write it as channel[unit] in lo..hi, " +
                "or channel[unit] followed by >, >=, < or <= and a number.");
        }

        string channel = match.Groups["channel"].Value.Trim();
        string unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.Trim() : string.Empty;

        double? min = null, max = null;

        if (match.Groups["lo"].Success)
        {
            min = Number(match.Groups["lo"].Value, declaration);
            max = Number(match.Groups["hi"].Value, declaration);

            if (min > max)
            {
                // Refused rather than reordered. An inverted band is a typo in a safety limit, and
                // guessing which number the author meant as the ceiling is a guess about what is
                // dangerous.
                throw new FormatException(
                    $"'{declaration}' has a lower bound above its upper bound. " +
                    "Which one is the ceiling is not something this can decide for you.");
            }
        }
        else
        {
            double bound = Number(match.Groups["bound"].Value, declaration);
            switch (match.Groups["op"].Value)
            {
                case ">": case ">=": min = bound; break;
                default: max = bound; break;
            }
        }

        return new ChannelLimit(declaration.Trim(), channel, unit, min, max);
    }

    /// <summary>Whether <paramref name="value"/> lies outside the band.</summary>
    /// <remarks>
    /// A non-finite reading is not reported as a breach. NaN and infinity are decode faults, which
    /// the parser layer surfaces as what they are; calling them process excursions sends an
    /// operator to the wrong end of the problem.
    /// </remarks>
    public bool IsBreached(double value) =>
        double.IsFinite(value) && ((Minimum is { } lo && value < lo) || (Maximum is { } hi && value > hi));

    /// <summary>
    /// Whether a sample's unit agrees with the one this limit was written in.
    /// </summary>
    /// <remarks>
    /// True when the limit stated no unit, because a rule that says nothing about units cannot
    /// disagree with one. Comparison ignores case and surrounding space and nothing else: a limit
    /// in kV against a channel in V is a disagreement, and converting between them here would mean
    /// this file knowing what every engineering unit means.
    /// </remarks>
    public bool UnitAgrees(string? sampleUnit) =>
        Unit.Length == 0 ||
        string.Equals(Unit, (sampleUnit ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Which side was crossed, for a reading that breached.</summary>
    public string Explain(double value) =>
        Minimum is { } lo && value < lo
            ? string.Create(CultureInfo.InvariantCulture, $"{value:G6} is below the {lo:G6} floor")
            : Maximum is { } hi && value > hi
                ? string.Create(CultureInfo.InvariantCulture, $"{value:G6} is above the {hi:G6} ceiling")
                : string.Create(CultureInfo.InvariantCulture, $"{value:G6} is within the band");

    private static double Number(string raw, string declaration) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value)
            ? value
            : throw new FormatException($"'{raw}' in '{declaration}' is not a finite number.");
}
