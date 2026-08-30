using System;
using System.Globalization;
using System.Text;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What the exposition format says a token looks like: escapes, and how a double is spelled.
/// </summary>
/// <remarks>
/// Its own file because every rule in it is quoted from a specification rather than chosen, and the
/// citation is the whole justification. Written from the text exposition format and the OpenMetrics
/// ABNF rather than from memory: the escape sets are small, they differ between the two contexts
/// below, and getting either wrong produces a document that parses into the wrong series instead of
/// failing.
/// </remarks>
public static partial class MetricsEndpoint
{
    /// <summary>Escapes a label value: three sequences, and deliberately no others.</summary>
    /// <remarks>
    /// From the text exposition format specification: the backslash, double-quote and line feed
    /// characters have to be escaped as <c>\\</c>, <c>\"</c> and <c>\n</c> respectively. The
    /// OpenMetrics ABNF defines the same three and no more.
    /// <para>
    /// A carriage return is therefore left as a literal byte. The specification says a label value
    /// may be any sequence of UTF-8 characters and defines no <c>\r</c>, so emitting one would be
    /// inventing an escape a conforming parser has no rule for -- and rewriting the character into
    /// something printable would silently change a channel's identity, which is worse than the
    /// cosmetic hazard of a bare CR inside a quoted value.
    /// </para>
    /// <para>
    /// Channel names reach here unfiltered and they are not tame: a node prefix makes
    /// <c>SIM:COM3.dab.bus_voltage</c>, and both the colon and the dot are illegal in a label name
    /// and the dot in a metric name. That is why every name in this endpoint is a fixed literal and
    /// everything variable is a label <em>value</em>. The naming conventions ask for that anyway --
    /// "do not put the name of a label in the metric name" -- and it also means no input this host
    /// has received can produce a syntactically invalid document.
    /// </para>
    /// </remarks>
    private static void AppendEscaped(StringBuilder text, string raw)
    {
        foreach (char character in raw)
        {
            switch (character)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                default: text.Append(character); break;
            }
        }
    }

    /// <summary>Escapes HELP text: backslash and line feed only.</summary>
    /// <remarks>
    /// Two sequences, not three, and this is the one an implementation written from memory gets
    /// wrong. The specification escapes a double quote in a label value and not in HELP, because
    /// HELP runs to the end of the line and was never quoted to begin with; escaping it anyway
    /// would put a literal backslash into every operator's metric documentation.
    /// <para>
    /// Backslash first, or the escape introduced by the line-feed pass would be escaped again by
    /// the backslash pass and arrive as <c>\\n</c>.
    /// </para>
    /// </remarks>
    private static string EscapeHelp(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
           .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Formats a value the way the format's parser reads one.</summary>
    /// <remarks>
    /// "NaN, +Inf, and -Inf are valid values representing not a number, positive infinity, and
    /// negative infinity". .NET spells the last two "Infinity", which a scraper rejects, and under
    /// some cultures spells all three differently again -- hence the invariant culture and the
    /// three cases written out.
    /// <para>
    /// Non-finite readings are exported rather than withheld, and that is not a breach of the rule
    /// this endpoint is built around. A sensor that reported NaN <em>was</em> read, and the format
    /// has a representation for exactly that; absent means nobody measured. They are different
    /// facts, and a document that collapsed them would be making this product's own mistake in the
    /// other direction -- discarding a measurement because it is inconvenient.
    /// </para>
    /// <para>
    /// "R" is shortest-round-trip on .NET Core 3.0 and later, so a double survives the trip
    /// unchanged. Its exponent form (<c>1E-05</c>) is accepted by Go's ParseFloat, which is what
    /// reads this on the other end.
    /// </para>
    /// </remarks>
    private static void AppendNumber(StringBuilder text, double value)
    {
        if (double.IsNaN(value)) { text.Append("NaN"); return; }
        if (double.IsPositiveInfinity(value)) { text.Append("+Inf"); return; }
        if (double.IsNegativeInfinity(value)) { text.Append("-Inf"); return; }

        text.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }
}
