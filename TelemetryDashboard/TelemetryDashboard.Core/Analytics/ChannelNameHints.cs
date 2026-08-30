using System.Collections.Generic;
using System.Text;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>One word in a channel name that is in the vocabulary, and what it proposes.</summary>
public readonly record struct NameHint(string Word, QuantityKind Kind);

/// <summary>
/// What a channel's name suggests it is — never what it proves.
/// </summary>
/// <remarks>
/// This is the file the governing rule was written about. A channel called <c>t</c> is not a
/// temperature; a channel called <c>current_state</c> is not an electric current; a channel called
/// <c>power_ratio</c> is not obviously either of the two things its name contains. So three
/// defences, and each of them exists because the alternative fails on a name a real rig has:
/// <list type="number">
/// <item><description>
/// <b>Whole words only.</b> Matching is against tokens, never substrings. Substring matching makes
/// <c>vibration</c> a match for <c>bra</c> and makes every name containing an <c>a</c> a candidate
/// ampere. Tokens come from the separators names actually use — dots, underscores, hyphens, slashes
/// — plus camelCase boundaries, so <c>busVoltage</c> and <c>bus_voltage</c> tokenise alike.
/// </description></item>
/// <item><description>
/// <b>Nothing shorter than three letters.</b> The single-letter case is the one ROADMAP W1 names:
/// a <c>t</c> read as temperature picks a Celsius axis and a 0–100 alarm band for a channel nobody
/// identified. No abbreviation this short is worth the failure, so <c>t</c>, <c>v</c>, <c>i</c> and
/// <c>p</c> are not in the vocabulary and could not match if they were.
/// </description></item>
/// <item><description>
/// <b>Several kinds means none.</b> A name proposing both power and ratio has proposed neither, and
/// the caller is told so rather than handed the first. The tempting shortcut is Prometheus's rule
/// that the unit is the last name component — <c>_seconds</c>, <c>_bytes</c>, <c>_ratio</c> — which
/// would pick <c>ratio</c> here and be right most of the time. It is not taken: that rule holds for
/// names authored under that convention, this hub receives names authored under none, and "right
/// most of the time" is the property this taxonomy exists to refuse. Making it opt-in per rig is a
/// product decision, recorded rather than made.
/// </description></item>
/// </list>
/// </remarks>
public static partial class ChannelNameHints
{
    /// <summary>Shortest token that may match. See the second defence above.</summary>
    public const int MinimumWordLength = 3;

    /// <summary>Every vocabulary word in the name, in the order it occurs.</summary>
    public static IReadOnlyList<NameHint> Read(string? channel)
    {
        var hints = new List<NameHint>();
        foreach (string token in Tokens(channel))
        {
            if (Words.TryGetValue(token, out QuantityKind kind)) hints.Add(new NameHint(token, kind));
        }

        return hints;
    }

    /// <summary>
    /// Lower-cased words from a channel name, split on separators and camelCase boundaries.
    /// </summary>
    /// <remarks>
    /// Digits terminate a token rather than joining it, so <c>field1</c> yields <c>field</c> — which
    /// is in no vocabulary, which is the correct answer for a channel the parser named positionally.
    /// <para>
    /// The camelCase break compares against the <em>preceding source character</em> rather than the
    /// last character accumulated, which is lower-cased and so would always look like the start of a
    /// new word. Getting that wrong splits <c>RPM</c> into three one-letter tokens, and the minimum
    /// length then discards all of them — a name saying exactly what the channel is, read as saying
    /// nothing.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Tokens(string? channel)
    {
        var word = new StringBuilder();
        char previous = '\0';

        foreach (char c in channel ?? string.Empty)
        {
            bool boundary = !char.IsLetter(c) || (char.IsUpper(c) && char.IsLower(previous));
            previous = c;

            if (boundary && word.Length > 0)
            {
                if (word.Length >= MinimumWordLength) yield return word.ToString();
                word.Clear();
            }

            if (char.IsLetter(c)) word.Append(char.ToLowerInvariant(c));
        }

        if (word.Length >= MinimumWordLength) yield return word.ToString();
    }
}
