using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Both front ends serve the same console, so both have to give it the same instruments.
/// </summary>
/// <remarks>
/// The desktop shell runs the same streaming server as the headless host and serves the same
/// pages, and attached none of them. A browser pointed at the shell — the whole reason an engineer
/// at a bench can look at the rig from a phone — was told this host has no archive, no declared
/// limits and nothing that can be commanded, while the application behind it had an archive open,
/// seven bands under watch and a simulator taking setpoints.
/// <para>
/// It fails quietly in both directions: the endpoint answers honestly that it has nothing, and
/// that reads as a hub without the feature rather than a hub that forgot to hand it over.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>
    /// Instruments both front ends must attach.
    /// </summary>
    /// <remarks>
    /// Coverage and Computed are deliberately not here. The shell keeps no fleet ledger and runs no
    /// computed-channel pump, so it has nothing to attach — and an endpoint answering null for
    /// something that genuinely does not exist is the honest answer.
    /// </remarks>
    private static readonly string[] SharedInstruments = ["Archive", "Limits", "Control"];

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheShellHandsTheConsoleTheSameInstrumentsTheHostDoes()
    {
        string[] uiSources = ProductionSourceFiles()
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}TelemetryDashboard.UI{Path.DirectorySeparatorChar}",
                                   StringComparison.Ordinal))
            .ToArray();

        uiSources.Should().NotBeEmpty();
        string ui = string.Concat(uiSources.Select(File.ReadAllText));

        var missing = new List<string>();
        foreach (string instrument in SharedInstruments)
        {
            if (!ui.Contains($"Server.{instrument} = ", StringComparison.Ordinal)
                && !ui.Contains($"_streamingServer.{instrument} = ", StringComparison.Ordinal))
            {
                missing.Add(instrument);
            }
        }

        missing.Should().BeEmpty(
            "a browser watching the shell would be told it has none of: " + string.Join(", ", missing));
    }
}
