using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Rules about the markup, which the compiler does not check.
/// </summary>
public partial class ArchitectureRuleTests
{
    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(SolutionRoot, "TelemetryDashboard.UI"), "*.xaml", SearchOption.AllDirectories);

    /// <summary>
    /// Every resource key the markup asks for has to exist.
    /// </summary>
    /// <remarks>
    /// A <c>StaticResource</c> naming a key that is not defined is not a build error. It throws
    /// while the XAML is being parsed, at run time, and WPF's failure mode is that the element's
    /// whole content tree fails to load — so the window opens, sizes itself, paints its chrome, and
    /// is empty inside. Nothing is logged where anyone looks.
    /// <para>
    /// Written after doing exactly that: a new button in the settings tab was styled
    /// <c>{StaticResource SecondaryButton}</c>, which does not exist here — the neighbouring
    /// buttons use <c>RibbonCommand</c>. The build was clean, the tests were green, and the running
    /// application had a blank window. It was noticed only because a UI Automation sweep that had
    /// found thirty-one buttons a few minutes earlier suddenly reported three elements in total.
    /// </para>
    /// <para>
    /// Keys defined anywhere in the project's markup count, wherever they are used: WPF resolves up
    /// the element tree and then the application, so a key defined in one control's Resources and
    /// used in another is a lookup this rule cannot judge — that would need the visual tree. What
    /// it does catch is the whole class of typos and renames, which is what actually happens.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryResourceKeyTheMarkupAsksForIsDefinedSomewhere()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (string file in XamlFiles())
        {
            string markup = File.ReadAllText(file);

            foreach (System.Text.RegularExpressions.Match key in Regex.Matches(markup, @"x:Key=""([^""]+)"""))
            {
                defined.Add(key.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match use in Regex.Matches(markup, @"\{(?:Static|Dynamic)Resource\s+([^}\s]+)\}"))
            {
                string name = use.Groups[1].Value;
                if (!referenced.TryGetValue(name, out SortedSet<string>? where))
                {
                    referenced[name] = where = new SortedSet<string>(StringComparer.Ordinal);
                }

                where.Add(Path.GetFileName(file));
            }
        }

        defined.Should().NotBeEmpty("the markup was not found, so this rule would pass vacuously");
        referenced.Should().NotBeEmpty("no resource references were parsed, so this rule proves nothing");

        string[] missing = referenced
            .Where(pair => !defined.Contains(pair.Key))
            .Select(pair => $"{pair.Key} (used in {string.Join(", ", pair.Value)})")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        missing.Should().BeEmpty(
            "a StaticResource with no definition throws while the XAML loads and leaves the window "
            + "empty rather than failing the build:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Every handler the markup names has to exist in the code-behind.
    /// </summary>
    /// <remarks>
    /// This one the compiler does catch — for a handler on a type it generates a partial class for.
    /// It is here for the same reason as the rule above: the failure is a window that does not
    /// load, and the cost of checking is one pass over files already being read.
    /// </remarks>
    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryEventHandlerTheMarkupNamesExistsInTheCodeBehind()
    {
        var problems = new List<string>();

        foreach (string file in XamlFiles())
        {
            // Code-behind for Foo.xaml is Foo.xaml.cs plus any partial beside it. The window is
            // split across several files here, so the whole directory is the search space.
            string? directory = Path.GetDirectoryName(file);
            if (directory is null) continue;

            string source = string.Join("\n", Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

            if (source.Length == 0) continue;

            string markup = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match handler in Regex.Matches(markup, @"\b(?:Click|Loaded|KeyDown|PreviewKeyDown|TextChanged|SelectionChanged|Closing|MouseDown|MouseDoubleClick|IsVisibleChanged|Checked|Unchecked)=""([A-Za-z_][A-Za-z0-9_]*)"""))
            {
                string name = handler.Groups[1].Value;
                if (!Regex.IsMatch(source, @"\b(?:void|Task)\s+" + Regex.Escape(name) + @"\s*\("))
                {
                    problems.Add($"{Path.GetFileName(file)} names {name}, which no file beside it defines");
                }
            }
        }

        problems.Should().BeEmpty(string.Join("\n", problems));
    }
}
