using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Fails when the shipped package would behave differently from a developer build.
/// </summary>
/// <remarks>
/// These rules exist because a whole class of defect is invisible to every other test in this
/// suite: the code is correct, the unit tests pass, the publish succeeds, and the application dies
/// on the user's first double-click because packaging changed an assumption underneath it. Nothing
/// short of launching the package catches that after the fact — so the assumption is checked here
/// instead, where it costs a second.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Extensions WPF loads through an <c>ImageSource</c> converter.</summary>
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff" };

    [Fact]
    [Trait("Category", "Architecture")]
    public void ImagesUsedByXamlAreResources_NotContentFiles()
    {
        var offenders = new List<string>();

        foreach (string project in ProductionProjects)
        {
            string directory = Path.Combine(SolutionRoot, project);
            if (!Directory.Exists(directory)) continue;

            string? csproj = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj is null) continue;

            HashSet<string> contentFiles = DeclaredAs(File.ReadAllText(csproj), "Content");

            foreach (string xaml in Directory.EnumerateFiles(directory, "*.xaml", SearchOption.AllDirectories))
            {
                foreach (string image in ImagesReferencedBy(File.ReadAllText(xaml)))
                {
                    if (contentFiles.Contains(image))
                    {
                        offenders.Add($"{Relative(xaml)} uses '{image}', which {Path.GetFileName(csproj)} declares as <Content>");
                    }
                }
            }
        }

        // WPF resolves a Content image through a ContentFilePart, which asks the assembly where it
        // lives on disk. A single-file publish has no answer, so the lookup reaches
        // Path.Combine(null, ..) and the process dies with an XamlParseException before its first
        // window is shown — while the same XAML works perfectly in every normal build, which is
        // what makes this worth a rule rather than a code review.
        offenders.Should().BeEmpty(
            "an image a window loads must be a <Resource>, compiled into the assembly, or the "
            + "published single-file build crashes on startup:\n" + string.Join("\n", offenders));
    }

    /// <summary>File names a project declares under the given item type, by their logical name.</summary>
    private static HashSet<string> DeclaredAs(string csproj, string itemType)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var item = new Regex($@"<{itemType}\s+Include=""([^""]+)""(.*?)(?:/>|</{itemType}>)",
            RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in item.Matches(csproj))
        {
            string include = match.Groups[1].Value;
            System.Text.RegularExpressions.Match link =
                Regex.Match(match.Groups[2].Value, @"<Link>([^<]+)</Link>");

            // A linked file is addressed by its Link, which is the name XAML would use.
            declared.Add(Path.GetFileName(link.Success ? link.Groups[1].Value : include));
        }

        return declared;
    }

    /// <summary>Image file names a XAML file references from any attribute.</summary>
    private static IEnumerable<string> ImagesReferencedBy(string xaml)
    {
        foreach (System.Text.RegularExpressions.Match match in Regex.Matches(xaml, @"=""([^""]+)"""))
        {
            string value = match.Groups[1].Value;
            if (!ImageExtensions.Any(e => value.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;

            // A pack:// or absolute URI names its own resolution and is not the failure mode here.
            if (value.Contains("://")) continue;

            yield return Path.GetFileName(value);
        }
    }
}
