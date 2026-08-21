using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the browser pages and the wire format from drifting apart.
/// </summary>
/// <remarks>
/// This rule exists because the same defect was found four times in one sweep, in four separate
/// files, each written at a different time: a page reading a telemetry field the hub does not send.
/// <list type="bullet">
/// <item><c>stream_client.html</c>, the console the host serves by default, read <c>data.temp</c>,
/// <c>data.vin</c>, <c>data.vout</c> and seven more. Its general mechanism —
/// <c>fetch('/api/config')</c> — had no server side, so every load fell into a path that assigned
/// <c>latestPSFB.vout = 48.0</c> to a device that had reported nothing.</item>
/// <item><c>power_ups_psfb_dashboard.html</c> read four fields and typed the rest into the markup.</item>
/// <item>All three <c>starter_*</c> pages, which are what a reader copies.</item>
/// <item><c>custom_widget.html</c>, which showed a dash forever while looking like it worked.</item>
/// </list>
/// None of it failed loudly. A page reading an absent field renders a placeholder, which is
/// indistinguishable from a hub that has not sent anything yet — so the fault could sit in the most
/// visible surface of the product for as long as nobody happened to compare it against the frame.
/// <para>
/// The allowed set is read out of <c>TelemetryFrame.cs</c> rather than written here, so adding a
/// field to the wire is enough to let the pages use it and removing one fails this test instead of
/// silently blanking a display.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Identifiers read off a frame-shaped variable in the browser assets.</summary>
    /// <remarks>
    /// <c>data</c>, <c>packet</c> and <c>pkt</c> are the conventional names for a decoded frame in
    /// these files. The convention is what makes the rule checkable without a JavaScript parser,
    /// and it is worth keeping for that reason alone.
    /// </remarks>
    private static readonly Regex FrameAccess =
        new(@"\b(?:data|packet|pkt)\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>
    /// Members that belong to JavaScript or the DOM rather than to a telemetry frame.
    /// </summary>
    /// <remarks>
    /// A page may legitimately hold an array or a DVR response in a variable called <c>data</c>.
    /// Listing what those can offer is narrower than exempting the files, and each addition here is
    /// a deliberate statement that the name is not a telemetry field.
    /// </remarks>
    private static readonly HashSet<string> NotTelemetryFields = new(StringComparer.Ordinal)
    {
        // JavaScript values and arrays
        "length", "forEach", "map", "filter", "slice", "push", "shift", "join", "sort",
        "toFixed", "indexOf", "includes", "split", "trim", "toString", "concat", "reverse",
        "every", "some", "reduce", "find", "keys", "values", "entries",

        // The DVR replay envelope, which is a different shape from a telemetry frame
        "frames", "totalFrames", "timelineStartSec", "maxDurationSec", "windowSec",
        "scrubPrecisionSec", "playbackSpeed", "requestedCenterSec",

        // The spectrum answer, likewise
        "Status", "Channel", "Reason", "Samples", "SampleRateHz", "NyquistHz", "BinHz",
        "Frequencies", "Magnitudes", "PeakHz", "PeakMagnitude", "WindowSec",

        // The SSE handshake line the stream opens with
        "event"
    };

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoWebAssetReadsATelemetryFieldTheHubDoesNotSend()
    {
        HashSet<string> wireFields = WireFieldNames();

        wireFields.Should().NotBeEmpty(
            "the allowed set is read out of TelemetryFrame.cs; an empty set means the parse broke "
            + "and this rule would pass by accident");

        var offenders = new List<string>();

        foreach (string file in WebAssetFiles())
        {
            // Comments in these files quote the retired names on purpose, to record what went
            // wrong. Reading the record must not fail the rule that the record is about.
            string source = StripJsComments(File.ReadAllText(file));

            foreach (System.Text.RegularExpressions.Match match in FrameAccess.Matches(source))
            {
                string member = match.Groups[1].Value;
                if (wireFields.Contains(member) || NotTelemetryFields.Contains(member)) continue;

                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        offenders.Distinct().Should().BeEmpty(
            "a page reading a field the hub never sends renders a placeholder forever, which looks "
            + "exactly like a hub that has sent nothing yet. Add the field to TelemetryFrame, read "
            + "the one that exists, or name it in NotTelemetryFields if it is not a frame at all:\n"
            + string.Join("\n", offenders.Distinct()));
    }

    /// <summary>The JSON names the wire contract actually declares.</summary>
    private static HashSet<string> WireFieldNames()
    {
        string path = Path.Combine(SolutionRoot, "TelemetryDashboard.Host", "Ingest", "TelemetryFrame.cs");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);

        return Regex.Matches(File.ReadAllText(path), @"JsonPropertyName\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Removes <c>//</c> and <c>/* */</c> comments and HTML comments.</summary>
    /// <remarks>
    /// The same lesson as the wiring rule, which counted a type as reachable because a doc comment
    /// still mentioned it: around abandoned code the prose outlives the call, so a rule that reads
    /// comments measures the prose.
    /// </remarks>
    private static string StripJsComments(string source)
    {
        source = Regex.Replace(source, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"(?m)^\s*//.*$", string.Empty);
    }
}
