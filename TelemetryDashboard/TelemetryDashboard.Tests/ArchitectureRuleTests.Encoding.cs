using System.Text;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Nothing invisible in the source.
/// </summary>
/// <remarks>
/// Written after a literal <c>0x1F</c> was found committed inside a string literal in
/// <c>DuplicateFilter</c>, where <c>''</c> was meant. It worked — 0x1F is a perfectly good
/// separator — and that is the problem: it is invisible in an editor, invisible in a diff, and
/// invisible in review. It survives exactly until something normalises the file, at which point a
/// key silently changes shape and a deduplicating filter stops recognising its own history.
/// <para>
/// This was the second time in one working session. The first was caught by eye in the input
/// inventory, whose key comment says to use the escape; the second went in anyway, two commits
/// deep, because a comment in one file is not a check on another.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>Control characters that legitimately appear in a source file.</summary>
    /// <remarks>
    /// Tab, carriage return and line feed. Everything else in the C0 range is either a typo, a
    /// paste from a terminal, or an escape somebody meant to write and did not.
    /// </remarks>
    private static bool IsAllowedControl(char c) => c is '\t' or '\r' or '\n';

    [Fact]
    [Trait("Category", "Tier1")]
    public void NoSourceFileCarriesARawControlCharacter()
    {
        var offenders = new List<string>();

        foreach (string file in ProductionSourceFiles())
        {
            string text = File.ReadAllText(file);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsControl(c) || IsAllowedControl(c)) continue;

                // Reported with the code point and the line, because the whole difficulty is that
                // an operator cannot see it. "Something is wrong on line 103" is not actionable
                // when line 103 looks correct.
                int line = text.Take(i).Count(ch => ch == '\n') + 1;
                offenders.Add($"{Relative(file)}:{line} contains U+{(int)c:X4}");
                break;
            }
        }

        offenders.Should().BeEmpty(
            "a control character in source is invisible in an editor, in a diff and in review, and "
            + "it survives until something normalises the file -- at which point a string literal "
            + "silently changes shape. Write the escape ('\\u001f') instead:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void EverySourceFileIsValidUtf8()
    {
        // The other way a file becomes unreadable without looking unreadable. This codebase carries
        // Korean console text and em dashes in comments, so a file saved in a code page rather than
        // UTF-8 turns those into replacement characters -- which then get committed and are, again,
        // only visible to whoever happens to read that line.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var offenders = new List<string>();

        foreach (string file in ProductionSourceFiles())
        {
            try
            {
                strict.GetString(File.ReadAllBytes(file));
            }
            catch (DecoderFallbackException ex)
            {
                offenders.Add($"{Relative(file)}: {ex.Message}");
            }
        }

        offenders.Should().BeEmpty(
            "a file that is not UTF-8 will read back as replacement characters:\n"
            + string.Join("\n", offenders));
    }
}
