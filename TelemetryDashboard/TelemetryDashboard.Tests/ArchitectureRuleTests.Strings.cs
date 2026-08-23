using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Rules about the captions the interface shows, which the compiler does not check.
/// </summary>
/// <remarks>
/// Every caption on the ribbon used to be a literal in MainWindow.xaml, so the language switch
/// changed the culture, raised an event nobody handled, wrote "Language switched to en-US" into the
/// log and left every word on screen as it was. They live in per-culture dictionaries now, and the
/// failure mode moves with them: a key present in one language and absent from another renders as
/// nothing at all, which is a blank button rather than an untranslated one.
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Keys defined in each shipped string dictionary, by culture.</summary>
    private static Dictionary<string, SortedSet<string>> StringDictionaries()
    {
        var byCulture = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(
            Path.Combine(SolutionRoot, "TelemetryDashboard.UI", "Resources"), "Strings.*.xaml"))
        {
            string culture = Path.GetFileNameWithoutExtension(path)["Strings.".Length..];
            byCulture[culture] = new SortedSet<string>(
                Regex.Matches(File.ReadAllText(path), @"<sys:String x:Key=""([^""]+)""")
                    .Select(m => m.Groups[1].Value),
                StringComparer.Ordinal);
        }

        return byCulture;
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryLanguageShipsTheSameKeys()
    {
        Dictionary<string, SortedSet<string>> byCulture = StringDictionaries();

        byCulture.Should().HaveCountGreaterThan(1, "a switch needs somewhere to switch to");

        SortedSet<string> reference = byCulture.Values.First();
        foreach ((string culture, SortedSet<string> keys) in byCulture)
        {
            keys.Should().BeEquivalentTo(reference,
                $"{culture} must define every caption the others do, or a button in it renders blank");
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryCaptionTheMarkupAsksForExistsInEveryLanguage()
    {
        // The existing key rule only asks whether a key is defined somewhere, which one dictionary
        // satisfies on behalf of all of them.
        Dictionary<string, SortedSet<string>> byCulture = StringDictionaries();

        var asked = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in XamlFiles())
        {
            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(File.ReadAllText(file), @"\{DynamicResource (Ui_\w+)\}"))
            {
                asked.Add(match.Groups[1].Value);
            }
        }

        asked.Should().NotBeEmpty("the ribbon reads its captions from these");

        foreach ((string culture, SortedSet<string> keys) in byCulture)
        {
            asked.Except(keys).Should().BeEmpty($"{culture} is missing captions the markup asks for");
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryCaptionSetFromCodeBehindExistsInEveryLanguage()
    {
        // Two ribbon buttons change caption as they toggle, and they do it with a resource
        // reference so the caption follows a language change. A key named only there is invisible
        // to the markup rule above.
        Dictionary<string, SortedSet<string>> byCulture = StringDictionaries();

        var asked = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(SolutionRoot, "TelemetryDashboard.UI"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            foreach (System.Text.RegularExpressions.Match match in Regex.Matches(File.ReadAllText(file), @"""(Ui_\w+)"""))
            {
                asked.Add(match.Groups[1].Value);
            }
        }

        foreach ((string culture, SortedSet<string> keys) in byCulture)
        {
            asked.Except(keys).Should().BeEmpty($"{culture} is missing a caption the code behind sets");
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void NoCaptionIsBlank()
    {
        foreach (string path in Directory.EnumerateFiles(
            Path.Combine(SolutionRoot, "TelemetryDashboard.UI", "Resources"), "Strings.*.xaml"))
        {
            Regex.Matches(File.ReadAllText(path), @"<sys:String x:Key=""[^""]+"">\s*</sys:String>")
                .Should().BeEmpty($"{Path.GetFileName(path)} has an entry that would render as nothing");
        }
    }
}
