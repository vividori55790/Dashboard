using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Nothing may be put on the telemetry stream in a shape the pages cannot read.
/// </summary>
/// <remarks>
/// The existing wire rule watches the other direction: a page reading a field the hub does not
/// send. This is the same defect approached from the publisher, and it hid for longer because it
/// has no symptom at either end — the sender succeeds, the page discards the message, and the
/// screen looks like a rig with nothing to say.
/// <para>
/// Found by attaching a browser to the running desktop shell and counting: 214 frames received,
/// none of them readable. The tick published a flat {temp, humidity, rpm} frame and a nested
/// {grid, dab, psfb, alarm} one, forty a second, from the shape this product used before it had a
/// wire contract. A comment claimed the bundled consoles bound to those names; none of them had.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>What every published frame has to carry, as the pages read it.</summary>
    /// <remarks>
    /// Read off <c>TelemetryFrame</c>'s JSON names rather than written here, so a rename on the
    /// wire fails this rather than silently moving the goalposts.
    /// </remarks>
    private static IReadOnlyList<string> RequiredWireFields()
    {
        string frame = File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Host", "Ingest", "TelemetryFrame.cs"));

        string[] names = Regex.Matches(frame, @"JsonPropertyName\(""(\w+)""\)")
            .Select(m => m.Groups[1].Value).ToArray();

        names.Should().Contain(new[] { "nodeId", "variable", "value" });
        return new[] { "nodeId", "variable", "value" };
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryFramePutOnTheStreamCarriesTheFieldsThePagesRead()
    {
        IReadOnlyList<string> required = RequiredWireFields();
        var offenders = new List<string>();

        foreach (string file in ProductionSourceFiles())
        {
            string source = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match call in
                     // A call, not the declaration: the dot is what tells them apart.
                     Regex.Matches(source, @"\.BroadcastTelemetry\(\s*([^;]{0,120})"))
            {
                string argument = call.Groups[1].Value.Trim();

                // The typed frame carries the contract by construction; that is what it is for.
                if (argument.Contains("TelemetryFrame", StringComparison.Ordinal)) continue;

                string body = AnonymousBodyFor(source, call.Index, argument);
                if (body.Length == 0)
                {
                    offenders.Add($"{Relative(file)}: cannot tell what is broadcast ({argument})");
                    continue;
                }

                string[] missing = required.Where(f => !Regex.IsMatch(body, $@"\b{f}\s*=")).ToArray();
                if (missing.Length > 0)
                {
                    offenders.Add($"{Relative(file)}: published frame lacks {string.Join(", ", missing)}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a frame missing these is discarded by every page that ships, which looks exactly like "
            + "a rig with nothing to say:\n" + string.Join("\n", offenders));
    }

    /// <summary>The anonymous object a broadcast publishes, inline or from the local it names.</summary>
    private static string AnonymousBodyFor(string source, int callIndex, string argument)
    {
        if (argument.StartsWith("new", StringComparison.Ordinal)) return Braced(source, callIndex);

        // A local declared just above, which is how the serial path writes it. Nullable because
        // LastOrDefault answers null for a call whose argument names no local, and the check below
        // is the one that decides -- not a null slipping into a non-nullable and being dereferenced
        // somewhere further on. (Qualified: Moq.Match is a global using here.)
        System.Text.RegularExpressions.Match? local = Regex.Matches(
                source[..callIndex], $@"var\s+{Regex.Escape(argument.TrimEnd(')', ' '))}\s*=\s*new")
            .Cast<System.Text.RegularExpressions.Match>().LastOrDefault();

        return local is null ? string.Empty : Braced(source, local.Index);
    }

    /// <summary>Text of the first brace-balanced block at or after <paramref name="from"/>.</summary>
    private static string Braced(string source, int from)
    {
        int open = source.IndexOf('{', from);
        if (open < 0) return string.Empty;

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        return string.Empty;
    }
}
