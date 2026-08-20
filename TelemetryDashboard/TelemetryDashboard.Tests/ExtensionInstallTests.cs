using System.Security.Cryptography;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves an extension can be installed, and that every way of failing verification is refused with
/// a reason instead of being accepted and discovered later.
/// </summary>
/// <remarks>
/// Installing runs a third party's code inside the host process, so the property that matters is
/// not "a good package installs" — it is "a bad package does not, and the store is untouched when
/// it does not". Each refusal test therefore asserts the reason names the actual fault and that the
/// store still holds nothing.
/// <para>
/// Real assemblies on disk throughout: the sample plugin for a package that must be accepted, and
/// Core for one that loads perfectly and exports no <c>IPlugin</c>. A mocked loader would prove
/// only that the code branches, not that a .NET assembly behaves the way the branch assumes.
/// </para>
/// </remarks>
public class ExtensionInstallTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The built sample plugin: a real assembly exporting a real IPlugin.</summary>
    private static string PluginAssembly =>
        typeof(Plugins.SamplePlugins.SampleTelemetryPlugin).Assembly.Location;

    /// <summary>Core: a real managed assembly that exports no IPlugin at all.</summary>
    private static string AssemblyWithoutPlugins =>
        typeof(Core.Models.TelemetryPacket).Assembly.Location;

    private string StoreDirectory => Path.Combine(_workspace.Root, "store");

    /// <summary>Builds a package directory holding an assembly and the manifest text given.</summary>
    private string Package(string name, string assemblyPath, string manifestJson)
    {
        string directory = Path.Combine(_workspace.Root, name);
        Directory.CreateDirectory(directory);
        File.Copy(assemblyPath, Path.Combine(directory, Path.GetFileName(assemblyPath)), overwrite: true);
        File.WriteAllText(Path.Combine(directory, ExtensionPackageManifest.FileName), manifestJson);
        return directory;
    }

    private static string Manifest(string id, string entryAssembly, string? sha256 = null) =>
        $$"""
        {
          "id": "{{id}}",
          "name": "Test Extension",
          "version": "1.0.0",
          "minApiVersion": "1.0.0",
          "entryAssembly": "{{entryAssembly}}"
          {{(sha256 is null ? string.Empty : $", \"sha256\": \"{sha256}\"")}}
        }
        """;

    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_AcceptsAVerifiedPackageAndRecordsWhatItActuallyHashedTo()
    {
        string package = Package("good", PluginAssembly, Manifest("test.good", Path.GetFileName(PluginAssembly)));
        var store = new ExtensionStore(StoreDirectory);

        ExtensionInstallOutcome outcome = new ExtensionInstaller(store).InstallFromPath(package);

        outcome.Succeeded.Should().BeTrue(outcome.Reason);
        outcome.Installed!.Sha256.Should().Be(Sha256Of(PluginAssembly),
            "the store must record the hash of the bytes it accepted, not the one a manifest claimed");
        File.Exists(store.AssemblyPathFor(outcome.Installed)).Should().BeTrue(
            "an install that records an extension without copying its assembly is a lie the next start discovers");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAManifestThatDoesNotParse_AndWritesNothing()
    {
        string package = Package("broken", PluginAssembly, "{ \"id\": \"x\", ");
        var store = new ExtensionStore(StoreDirectory);

        ExtensionInstallOutcome outcome = new ExtensionInstaller(store).InstallFromPath(package);

        outcome.Succeeded.Should().BeFalse();
        outcome.Reason.Should().Contain("not valid JSON");
        store.Extensions.Should().BeEmpty("a refused package must leave the store exactly as it was");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAManifestWithNoEntryAssembly()
    {
        string package = Package("no-entry", PluginAssembly,
            """{ "id": "test.noentry", "name": "No Entry", "version": "1.0.0" }""");

        ExtensionInstallOutcome outcome =
            new ExtensionInstaller(new ExtensionStore(StoreDirectory)).InstallFromPath(package);

        outcome.Succeeded.Should().BeFalse();
        outcome.Reason.Should().Contain("entryAssembly");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAnEntryAssemblyThatEscapesThePackageDirectory()
    {
        string package = Package("traversal", PluginAssembly,
            Manifest("test.traversal", "../TelemetryDashboard.Core.dll"));

        ExtensionInstallOutcome outcome =
            new ExtensionInstaller(new ExtensionStore(StoreDirectory)).InstallFromPath(package);

        outcome.Succeeded.Should().BeFalse(
            "a manifest that can name a path outside its package can overwrite the host on install");
        outcome.Reason.Should().Contain("bare file name");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAnAssemblyThatExportsNoPlugin()
    {
        string package = Package("empty", AssemblyWithoutPlugins,
            Manifest("test.empty", Path.GetFileName(AssemblyWithoutPlugins)));

        ExtensionInstallOutcome outcome =
            new ExtensionInstaller(new ExtensionStore(StoreDirectory)).InstallFromPath(package);

        outcome.Succeeded.Should().BeFalse(
            "an extension that loads and exports nothing installs cleanly and then does nothing forever");
        outcome.Reason.Should().Contain("exports no public IPlugin");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAFileThatIsNotAManagedAssembly()
    {
        string directory = Path.Combine(_workspace.Root, "garbage");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Fake.dll"), "not a PE image");
        File.WriteAllText(Path.Combine(directory, ExtensionPackageManifest.FileName),
            Manifest("test.fake", "Fake.dll"));

        ExtensionInstallOutcome outcome =
            new ExtensionInstaller(new ExtensionStore(StoreDirectory)).InstallFromPath(directory);

        outcome.Succeeded.Should().BeFalse();
        outcome.Reason.Should().Contain("will not load");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAPayloadThatDisagreesWithTheHashItsCatalogueEntryPublished()
    {
        string package = Package("tampered", PluginAssembly,
            Manifest("test.tampered", Path.GetFileName(PluginAssembly), new string('0', 64)));
        var store = new ExtensionStore(StoreDirectory);

        ExtensionInstallOutcome outcome = new ExtensionInstaller(store).InstallFromPath(package);

        outcome.Succeeded.Should().BeFalse(
            "a hash that is checked only after installation protects nothing");
        outcome.Reason.Should().Contain("integrity check").And.Contain(Sha256Of(PluginAssembly));
        store.Extensions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Install_RefusesAPackageDirectoryWithNoManifest()
    {
        string directory = Path.Combine(_workspace.Root, "bare");
        Directory.CreateDirectory(directory);
        File.Copy(PluginAssembly, Path.Combine(directory, Path.GetFileName(PluginAssembly)));

        ExtensionInstallOutcome outcome =
            new ExtensionInstaller(new ExtensionStore(StoreDirectory)).InstallFromPath(directory);

        outcome.Succeeded.Should().BeFalse();
        outcome.Reason.Should().Contain(ExtensionPackageManifest.FileName);
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InstallFromCatalogue_ResolvesTheEntryAndCarriesItsPublishedHash()
    {
        string payload = Package("cat", PluginAssembly, "{}");
        string index = Path.Combine(payload, "catalogue.json");
        File.WriteAllText(index,
            "[" + Manifest("test.cat", Path.GetFileName(PluginAssembly), Sha256Of(PluginAssembly)) + "]");

        bool resolved = ExtensionCatalogueSource.TryResolve(
            index, "test.cat", out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal);

        resolved.Should().BeTrue(refusal?.Reason);
        source!.ExpectedSha256.Should().Be(Sha256Of(PluginAssembly));
        new ExtensionInstaller(new ExtensionStore(StoreDirectory)).Install(source).Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InstallFromCatalogue_RefusesAnHttpIndex_NamingWhyRatherThanFailingToConnect()
    {
        bool resolved = ExtensionCatalogueSource.TryResolve(
            "https://example.invalid/catalogue.json", "anything", out _, out ExtensionInstallOutcome? refusal);

        resolved.Should().BeFalse();
        refusal!.Reason.Should().Contain("http(s) catalogue is not supported",
            "a listing may come over http; executing a payload on the same server's word may not");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InstallFromCatalogue_NamesTheIdsTheIndexDoesHoldWhenTheWantedOneIsAbsent()
    {
        string payload = Package("cat2", PluginAssembly, "{}");
        string index = Path.Combine(payload, "catalogue.json");
        File.WriteAllText(index, "[" + Manifest("present.one", Path.GetFileName(PluginAssembly)) + "]");

        ExtensionCatalogueSource.TryResolve(index, "absent.one", out _, out ExtensionInstallOutcome? refusal);

        refusal!.Reason.Should().Contain("present.one",
            "'not found' alone cannot be told apart from an entry the parser rejected");
    }
}
