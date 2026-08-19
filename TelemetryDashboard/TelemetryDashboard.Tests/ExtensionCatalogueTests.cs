using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves the extension catalogue can be reached from the product, and that it never overstates
/// what it found.
/// </summary>
/// <remarks>
/// <c>ManifestIndexMarketplace</c> implemented <c>IMarketplaceService</c> and was constructed
/// nowhere, so there was no way to list an extension from any host. <c>ExtensionCatalogueReport</c>
/// is its entry point, and these tests hold it to the two properties that make a listing worth
/// trusting: a rejected entry is counted out loud, and an unreachable catalogue never renders as an
/// empty one.
/// </remarks>
public class ExtensionCatalogueTests : IDisposable
{
    private readonly List<string> _files = new();

    public void Dispose()
    {
        foreach (string file in _files.Where(File.Exists)) File.Delete(file);
        GC.SuppressFinalize(this);
    }

    private string WriteIndex(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"catalogue-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _files.Add(path);
        return path;
    }

    /// <summary>Two usable manifests and one with no id, which the parser must refuse.</summary>
    private const string MixedIndex = """
        [
          { "id": "ext.pid", "name": "PID Controller Plugin", "version": "1.2.0" },
          { "name": "Nameless Extension", "version": "0.1.0" },
          { "id": "ext.fft", "name": "FFT Advanced", "version": "2.0.1" }
        ]
        """;

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task FetchAsync_ListsEveryUsableEntryAndCountsTheRejectedOnes()
    {
        ExtensionCatalogueReport report =
            await ExtensionCatalogueReport.FetchAsync(WriteIndex(MixedIndex), CancellationToken.None);

        report.Reachable.Should().BeTrue();
        report.Extensions.Select(e => e.Id).Should().Equal("ext.pid", "ext.fft");
        report.RejectedCount.Should().Be(1,
            "one manifest was dropped, and a catalogue that hides that looks complete");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task RenderLines_StateTheRejectedCountAndClaimNoInstall()
    {
        ExtensionCatalogueReport report =
            await ExtensionCatalogueReport.FetchAsync(WriteIndex(MixedIndex), CancellationToken.None);

        string rendered = string.Join(Environment.NewLine, report.RenderLines());

        rendered.Should().Contain("2 listed, 1 rejected");
        rendered.Should().Contain("ext.pid").And.Contain("ext.fft");
        rendered.Should().Contain("nothing was installed",
            "listing is the honest first step; installing is a separate decision");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task FetchAsync_MissingIndex_ReportsUnreachableRatherThanAnEmptyCatalogue()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json");

        ExtensionCatalogueReport report =
            await ExtensionCatalogueReport.FetchAsync(absent, CancellationToken.None);

        report.Reachable.Should().BeFalse();
        report.Failure.Should().Contain(nameof(FileNotFoundException));

        string rendered = string.Join(Environment.NewLine, report.RenderLines());
        rendered.Should().Contain("UNREACHABLE");
        rendered.Should().NotContain("0 listed",
            "'no extensions published' and 'I could not read the catalogue' lead an operator to "
            + "opposite conclusions, so the failure must never borrow the wording of an empty list");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task RenderLines_MalformedIndex_DoNotClaimTheCatalogueIsEmpty()
    {
        // A truncated document is a broken catalogue, not a catalogue with nothing in it. The
        // splitter cannot tell the host which it read, so the host must not pick the flattering one.
        ExtensionCatalogueReport report =
            await ExtensionCatalogueReport.FetchAsync(WriteIndex("[ { \"id\": "), CancellationToken.None);

        report.Extensions.Should().BeEmpty();
        string.Join(Environment.NewLine, report.RenderLines())
            .Should().Contain("look identical here",
                "reporting a broken index as an empty one tells the operator nothing is published "
                + "when in fact nothing could be read");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task RenderLines_EmptyIndex_StillPrintTheZeroCounts()
    {
        ExtensionCatalogueReport report =
            await ExtensionCatalogueReport.FetchAsync(WriteIndex("[]"), CancellationToken.None);

        report.Reachable.Should().BeTrue();
        string.Join(Environment.NewLine, report.RenderLines())
            .Should().Contain("0 listed, 0 rejected");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void CommandLine_NamesTheCatalogueAndThePluginDirectory()
    {
        string directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        HostOptions options = CommandLineParser.Parse(
            new[] { "--extensions", "https://example.invalid/index.json", "--plugin-dir", directory },
            new HostOptions());

        options.Error.Should().BeNull();
        options.ExtensionCatalogue.Should().Be("https://example.invalid/index.json");
        options.PluginDirectory.Should().Be(Path.GetFullPath(directory));
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void CommandLine_RejectsAPluginDirectoryThatDoesNotExist()
    {
        HostOptions options = CommandLineParser.Parse(
            new[] { "--plugin-dir", Path.Combine(Path.GetTempPath(), "no-such-plugin-dir-9f2a") },
            new HostOptions());

        options.Error.Should().NotBeNull(
            "a typo in a plugin directory would otherwise report a clean start with no plugins");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void UsageText_DocumentsBothNewOptionsAndTheirEnvironmentVariables()
    {
        string usage = UsageText.Render();

        usage.Should().Contain("--extensions").And.Contain(EnvironmentVariables.Extensions);
        usage.Should().Contain("--plugin-dir").And.Contain(EnvironmentVariables.PluginDir);
        usage.Should().Contain("installs nothing");
    }
}
