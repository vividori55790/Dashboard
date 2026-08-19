using System.Text.RegularExpressions;

// Moq.Match arrives through the global usings and collides with the regex type of the same name.
using RegexMatch = System.Text.RegularExpressions.Match;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Rules that keep the product's portable backbone portable.
/// </summary>
/// <remarks>
/// Portability was previously an assertion in a document. The test project targeted
/// <c>net8.0-windows</c> because it referenced the WPF shell, so all 590 tests were Windows-only and
/// nothing ever compiled Core, Infrastructure or Plugins for a non-Windows target — the claim that
/// they were portable could not have been falsified. These rules make the claim executable: one
/// guards the three libraries' target frameworks, the other guards the test project that proves it.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>
    /// The three libraries that must compile and run on any .NET 8 host, not just Windows.
    /// </summary>
    private static readonly string[] PortableBackboneProjects =
    {
        "TelemetryDashboard.Core",
        "TelemetryDashboard.Infrastructure",
        "TelemetryDashboard.Plugins"
    };

    /// <summary>Matches a declared target framework, singular or plural form.</summary>
    private static readonly Regex TargetFrameworkDeclaration =
        new(@"<TargetFrameworks?>(?<value>[^<]*)</TargetFrameworks?>", RegexOptions.IgnoreCase);

    /// <summary>Matches an XML comment block.</summary>
    private static readonly Regex XmlComment = new(@"<!--.*?-->", RegexOptions.Singleline);

    /// <summary>
    /// A project file's markup with its comments removed.
    /// </summary>
    /// <remarks>
    /// A rule that greps raw csproj text will trip over the comment explaining the rule. This one
    /// did, immediately: the note in TelemetryDashboard.Tests.csproj recording <em>why</em>
    /// Xunit.StaFact must stay out names the package, and the check below read that as the package
    /// being present. Documentation that cannot mention what it documents is not documentation, so
    /// the comments come out before the assertions go in.
    /// </remarks>
    private static string MarkupWithoutComments(string csprojPath) =>
        XmlComment.Replace(File.ReadAllText(csprojPath), string.Empty);

    /// <summary>
    /// None of Core, Infrastructure or Plugins declares a platform-suffixed target framework.
    /// </summary>
    /// <remarks>
    /// A single character — the <c>-windows</c> in one <c>&lt;TargetFramework&gt;</c> element — is
    /// enough to make the whole product Windows-only, and it does so silently. Everything downstream
    /// of a platform-specific library inherits the restriction: the UI already targets
    /// <c>net8.0-windows</c> so it would keep building, the test suite would keep passing on a
    /// developer's Windows machine, and the failure would surface only when someone tried to run the
    /// data path on a Linux CI agent or a Mac. The suffix is also the easy fix for the wrong problem:
    /// it makes a CA1416 warning disappear by promising Windows rather than by removing the
    /// Windows-only call. This rule refuses that trade.
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void PortableBackboneProjectsTargetNoSpecificPlatform()
    {
        var offenders = new List<string>();

        foreach (string project in PortableBackboneProjects)
        {
            string csproj = Path.Combine(SolutionRoot, project, project + ".csproj");
            File.Exists(csproj).Should().BeTrue($"{project} is part of the portable backbone");

            foreach (RegexMatch declaration in TargetFrameworkDeclaration.Matches(MarkupWithoutComments(csproj)))
            {
                string[] platformSuffixed = declaration.Groups["value"].Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(tfm => tfm.Contains('-'))
                    .ToArray();

                offenders.AddRange(platformSuffixed.Select(tfm => $"{project} targets '{tfm}'"));
            }
        }

        offenders.Should().BeEmpty(
            "Core, Infrastructure and Plugins are the portable backbone — every host the product "
            + "has or will have is built on top of them. A platform suffix on any one of them makes "
            + "the entire product Windows-only, without a build error to say so, and the three "
            + "libraries stop being verifiable on a Linux CI agent:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The portable test project stays portable, so the rule above is actually exercised off Windows.
    /// </summary>
    /// <remarks>
    /// This is the regression that created the problem in the first place. A single
    /// <c>&lt;ProjectReference&gt;</c> to the WPF shell forces <c>net8.0-windows</c> on the test
    /// project, and from that moment nothing verifies the backbone on a non-Windows machine — the
    /// rule above would still be green, and still be untested where it matters. WPF-dependent tests
    /// belong in TelemetryDashboard.Tests.Desktop.
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void PortableTestProjectDoesNotDependOnThePresentationLayer()
    {
        string csproj = Path.Combine(
            SolutionRoot, "TelemetryDashboard.Tests", "TelemetryDashboard.Tests.csproj");
        string content = MarkupWithoutComments(csproj);

        foreach (RegexMatch declaration in TargetFrameworkDeclaration.Matches(content))
        {
            declaration.Groups["value"].Value.Should().NotContain("-",
                "a platform-suffixed test project can only run on that platform");
        }

        content.Should().NotContain("TelemetryDashboard.UI.csproj",
            "referencing the WPF shell drags this project onto net8.0-windows and takes the whole "
            + "suite with it — that reference is what made all 590 tests unrunnable on Linux. Tests "
            + "that need a WPF type go in TelemetryDashboard.Tests.Desktop instead.");

        // Xunit.StaFact's [WpfFact] does not fail cleanly here — it loads WindowsBase, which is
        // absent on net8.0, and takes the test host process down mid-run. The remaining tests are
        // then never executed and the run still reports success. Without the package the attribute
        // is a compile error, which is the only failure mode that cannot be mistaken for a pass.
        content.Should().NotContain("Xunit.StaFact",
            "an STA/Dispatcher test attribute in a portable project aborts the whole assembly at "
            + "runtime and reports the partial run as green");
    }
}
