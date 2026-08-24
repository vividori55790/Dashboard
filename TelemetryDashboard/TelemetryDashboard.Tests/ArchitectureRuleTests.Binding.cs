using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What the console binds to, pinned so the claim and the code cannot drift apart again.
/// </summary>
/// <remarks>
/// <c>TelemetryStreamingServer</c> takes an <c>acceptRemoteConnections</c> argument, documents at
/// length why it stays off by default, and <em>nothing in this repository ever passes it</em> —
/// not the headless host, not the desktop shell, not a single test. There is no command-line flag
/// for it and no environment variable. The browser console is loopback-only in every configuration
/// this product ships, and no operator can change that.
/// <para>
/// The start-up banner says so plainly. The Platform reach table said "Reaches the hub: any
/// browser — desktop, tablet, Android, iOS", which is true only of a browser running on the same
/// machine as the host, and a tablet never is. That is the whole cross-platform argument resting
/// on a capability that is not wired.
/// </para>
/// <para>
/// This rule does not forbid remote binding. It records that it is absent, and fails the moment
/// somebody enables it — because enabling it is not a wiring change, it is the point at which
/// somebody has to decide about authentication. The endpoint streams live plant telemetry and
/// accepts commands over its WebSocket, so opening it to a network without deciding that first
/// would publish a plant to whatever shares the subnet. The refusal to wire it was deliberate, and
/// this keeps it deliberate rather than forgotten.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void TheConsoleBindsLoopbackOnlyInEveryProductionConstruction()
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

                // Positional 'true' as the second argument, or the parameter named explicitly.
                bool named = Regex.IsMatch(arguments, @"acceptRemoteConnections\s*:\s*true");
                bool positional = Regex.IsMatch(arguments, @"^\s*[^,]+,\s*true\s*(,|$)");

                if (named || positional)
                {
                    offenders.Add($"{Relative(file)}: {match.Value.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "binding beyond loopback is not a wiring change -- it is the moment somebody has to "
            + "decide how this endpoint authenticates, because it streams plant telemetry and "
            + "accepts commands over its WebSocket. If that decision has now been made, delete this "
            + "rule and say what was decided; until then the Platform reach table must not promise "
            + "a browser on another device:\n" + string.Join("\n", offenders));
    }
}
