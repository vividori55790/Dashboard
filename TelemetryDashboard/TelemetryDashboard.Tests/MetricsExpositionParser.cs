using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A strict reader for the Prometheus text exposition format, used to check <c>/metrics</c>.
/// </summary>
/// <remarks>
/// Deliberately not the endpoint's own formatter run backwards. A test that asserted the output
/// matched what the writer would write proves the writer agrees with itself, which it always does;
/// the question is whether a scraper agrees with it, and the only way to ask that here is to read
/// the document with a grammar taken from the specification rather than from the code under test.
/// <para>
/// Stricter than a real scraper on purpose. It refuses timestamps, exemplars and blank lines
/// because this endpoint emits none of them, so anything that turns up is a change nobody meant
/// rather than a tolerated variation.
/// </para>
/// </remarks>
public static class MetricsExpositionParser
{
    /// <summary>A quoted label value: any byte but a quote or backslash, or one of three escapes.</summary>
    /// <remarks>
    /// The three the format defines and no others -- <c>\\</c>, <c>\"</c>, <c>\n</c> -- so a
    /// <c>\r</c> or a <c>\t</c> emitted by a well-meaning future change fails to parse here rather
    /// than reaching a scraper that has no rule for it.
    /// </remarks>
    private const string LabelValue = @"(?:[^""\\]|\\[\\""n])*";

    private const string LabelName = "[a-zA-Z_][a-zA-Z0-9_]*";

    private static readonly Regex Sample = new(
        "^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)"
        + $"(?:\\{{(?<labels>{LabelName}=\"{LabelValue}\"(?:,{LabelName}=\"{LabelValue}\")*)\\}})?"
        + " (?<value>NaN|[+-]Inf|[+-]?(?:[0-9]+\\.?[0-9]*|\\.[0-9]+)(?:[eE][+-]?[0-9]+)?)$",
        RegexOptions.Compiled);

    private static readonly Regex Header = new(
        "^# (?<kind>HELP|TYPE) (?<name>[a-zA-Z_:][a-zA-Z0-9_:]*) (?<rest>.*)$", RegexOptions.Compiled);

    /// <summary>Every sample line, keyed by its full identity, in the order it appeared.</summary>
    public sealed record Document(
        IReadOnlyDictionary<string, string> Samples,
        IReadOnlyDictionary<string, string> Types,
        IReadOnlyDictionary<string, string> Help,
        IReadOnlyList<string> Order);

    /// <summary>Reads a document, throwing on the first line a scraper would not accept.</summary>
    public static Document Parse(string text)
    {
        if (text.Length > 0 && !text.EndsWith('\n'))
        {
            throw new FormatException("the last line must end with a line feed");
        }

        var samples = new Dictionary<string, string>(StringComparer.Ordinal);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var help = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        var finished = new HashSet<string>(StringComparer.Ordinal);
        string? open = null;

        foreach (string line in text.Split('\n')[..^1])
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                System.Text.RegularExpressions.Match header = Header.Match(line);
                if (!header.Success) throw new FormatException($"unreadable header: {line}");

                string family = header.Groups["name"].Value;

                // "The optional HELP and TYPE lines first." A header arriving after its own
                // samples, or after the family was closed, is a group that is not one group.
                if (open == family || finished.Contains(family))
                {
                    throw new FormatException($"header for {family} came after its samples");
                }

                (header.Groups["kind"].Value == "HELP" ? help : types)[family] = header.Groups["rest"].Value;
                continue;
            }

            System.Text.RegularExpressions.Match match = Sample.Match(line);
            if (!match.Success) throw new FormatException($"unreadable sample line: {line}");

            string name = match.Groups["name"].Value;

            // "All lines for a given metric must be provided as one single group." A family that
            // resumes after another has begun is the failure this catches, and it is invisible in
            // a diff.
            if (open != name)
            {
                if (finished.Contains(name)) throw new FormatException($"family {name} appears in two groups");
                if (open is not null) finished.Add(open);
                open = name;
            }

            string identity = line[..line.LastIndexOf(' ')];
            if (!samples.TryAdd(identity, match.Groups["value"].Value))
            {
                throw new FormatException($"duplicate series: {identity}");
            }

            order.Add(identity);
        }

        return new Document(samples, types, help, order);
    }

    /// <summary>The family name inside a sample's identity.</summary>
    public static string Family(string identity)
    {
        int brace = identity.IndexOf('{', StringComparison.Ordinal);
        return brace < 0 ? identity : identity[..brace];
    }
}
