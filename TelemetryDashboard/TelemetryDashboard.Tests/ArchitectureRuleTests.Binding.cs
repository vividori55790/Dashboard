using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What the console binds to, pinned so the claim and the code cannot drift apart again.
/// </summary>
/// <remarks>
/// This rule used to record that <em>nothing in this repository ever passes</em>
/// <c>acceptRemoteConnections</c> — no flag, no environment variable, no test — and it failed the
/// moment anything did. Not because remote binding was forbidden, but because wiring it is not a
/// wiring change: it is the point at which somebody has to decide how an endpoint that streams
/// live telemetry and takes commands over its WebSocket authenticates. The rule's own instruction
/// was to delete it once that decision was made, and say what was decided.
/// <para>
/// <strong>What was decided.</strong> <c>--listen network</c> binds every interface and cannot be
/// asked for without <c>--credential</c>; the parser refuses the pair and
/// <c>TelemetryStreamingServer.Start</c> refuses it again at the socket, so the unsafe state has no
/// construction path rather than a convention against it. That is pinned by
/// <see cref="ConsoleBindingTests"/> and <see cref="ListenScopeTests"/> against running objects,
/// which is a stronger check than reading the source, so the source scan those replace is gone.
/// </para>
/// <para>
/// <strong>What was rejected, and why it matters here.</strong> The other candidate was to allow
/// wide binding only behind a TLS-terminating reverse proxy. A process cannot verify that a proxy
/// is in front of it — <c>X-Forwarded-Proto</c> is written by whoever connects — so that flag's
/// safety would have rested on a sentence in a document. Proxying also needs no flag: it works
/// against the loopback binding today, unchanged. So the honest half was taken instead: the link
/// is plain HTTP, Basic is base64, the password crosses the segment readable, and the banner and
/// <c>/api/status</c> both say so at every launch rather than once in a README.
/// </para>
/// <para>
/// What survives here is the narrower thing the decision does not license: wide binding must stay
/// something an operator asks for, never something a call site chose on their behalf.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void NoProductionCallSiteHardcodesAWideBinding()
    {
        var construction = new Regex(@"new\s+TelemetryStreamingServer\s*\(([^;]*?)\)", RegexOptions.Singleline);

        var offenders = new List<string>();

        foreach (string file in ProductionSourceFiles())
        {
            string text = StripComments(File.ReadAllText(file));

            // Qualified: Moq.Match is a global using in this project.
            foreach (System.Text.RegularExpressions.Match match in construction.Matches(text))
            {
                string arguments = match.Groups[1].Value;

                // A literal 'true', named or as the second positional argument. An option read
                // from the command line is the intended spelling and does not match either.
                bool named = Regex.IsMatch(arguments, @"acceptRemoteConnections\s*:\s*true");
                bool positional = Regex.IsMatch(arguments, @"^\s*[^,]+,\s*true\s*(,|$)");

                if (named || positional)
                {
                    offenders.Add($"{Relative(file)}: {match.Value.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "exposing this console is the operator's decision to make and their segment to judge: "
            + "the password crosses it readable, so 'is this network mine' is a question only they "
            + "can answer. A call site that hardcodes the wide binding has answered it for every "
            + "installation, including the ones on a plant network:\n" + string.Join("\n", offenders));
    }
}
