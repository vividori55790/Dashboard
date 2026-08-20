using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// The set of channels a detector was pointed at, as wildcard patterns.
/// </summary>
/// <remarks>
/// Patterns rather than exact names because channel names are discovered at runtime — a host does
/// not know that <c>NODE_7.TEMP</c> exists until the device says so. An operator who has to
/// enumerate every channel to protect it will protect the ones they remembered.
///
/// <para>Deliberately narrow: <c>*</c> matches any run of characters and <c>?</c> matches one.
/// A full regex in a configuration file is a way for a typo to become a catastrophic backtrack on
/// the ingest thread, and nothing here needs the expressiveness.</para>
///
/// <para>An empty selector matches nothing, not everything. A detector whose channel list failed to
/// parse would otherwise silently attach itself to every channel in the plant.</para>
/// </remarks>
public sealed class ChannelSelector
{
    /// <summary>The pattern an operator writes to mean "every channel".</summary>
    public const string MatchAll = "*";

    private readonly string[] _patterns;

    public ChannelSelector(IEnumerable<string>? patterns)
    {
        _patterns = (patterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>A selector matching every channel.</summary>
    public static ChannelSelector All { get; } = new(new[] { MatchAll });

    /// <summary>The patterns as configured, for the detector identity and the startup report.</summary>
    public IReadOnlyList<string> Patterns => _patterns;

    /// <summary>True when no pattern was supplied, so this selector matches nothing.</summary>
    public bool IsEmpty => _patterns.Length == 0;

    /// <summary>Whether any configured pattern covers <paramref name="channelName"/>.</summary>
    public bool Matches(string channelName)
    {
        if (channelName is null) return false;

        foreach (string pattern in _patterns)
        {
            if (IsMatch(channelName, pattern)) return true;
        }
        return false;
    }

    /// <summary>The patterns joined for use inside a detector id.</summary>
    public string Describe() => _patterns.Length == 0 ? "none" : string.Join("|", _patterns);

    /// <summary>
    /// Glob match, iterative with a single backtrack point.
    /// </summary>
    /// <remarks>
    /// The recursive form of this is the classic exponential blow-up on input like
    /// <c>a*a*a*a*b</c>. This runs on the ingest path for every sample of every channel, so it is
    /// written to have no worst case worth mentioning.
    /// </remarks>
    private static bool IsMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, resume = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], text[t])))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool Same(char a, char b) =>
        char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
