using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves the parts of an extension's life after installation: it can be switched off and on across
/// a restart, removed for real, and — whatever state it is in — reported rather than silently
/// omitted.
/// </summary>
/// <remarks>
/// The reporting assertions are the point of this file. An extension can be missing from a running
/// host for four unrelated reasons, and a report that lists only what loaded is indistinguishable
/// from a host with nothing installed. Each test below checks that the reason survives all the way
/// to the text an operator reads.
/// </remarks>
public class ExtensionLifecycleTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string PluginAssembly =>
        typeof(Plugins.SamplePlugins.SampleTelemetryPlugin).Assembly.Location;

    private string StoreDirectory => Path.Combine(_workspace.Root, "store");

    /// <summary>Installs the real sample plugin into a private store, as an operator would.</summary>
    private ExtensionStore InstallSample(string id = "test.sample", string minApiVersion = "1.0.0")
    {
        string package = Path.Combine(_workspace.Root, id);
        Directory.CreateDirectory(package);
        File.Copy(PluginAssembly, Path.Combine(package, Path.GetFileName(PluginAssembly)), overwrite: true);
        File.WriteAllText(Path.Combine(package, ExtensionPackageManifest.FileName),
            $$"""
            {
              "id": "{{id}}", "name": "Sample", "version": "1.0.0",
              "minApiVersion": "{{minApiVersion}}",
              "entryAssembly": "{{Path.GetFileName(PluginAssembly)}}"
            }
            """);

        var store = new ExtensionStore(StoreDirectory);
        ExtensionInstallOutcome outcome = new ExtensionInstaller(store).InstallFromPath(package);
        outcome.Succeeded.Should().BeTrue(outcome.Reason);
        return store;
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Disable_SurvivesARestart_AndKeepsTheFilesInPlace()
    {
        ExtensionStore store = InstallSample();

        store.SetEnabled("test.sample", false).Should().BeTrue();

        // A second store over the same directory is what the next process start does.
        var reopened = new ExtensionStore(StoreDirectory);
        reopened.Find("test.sample")!.Enabled.Should().BeFalse(
            "an operator debugging overnight must not find the extension back on in the morning");
        File.Exists(reopened.AssemblyPathFor(reopened.Find("test.sample")!)).Should().BeTrue(
            "disabling is not deleting; the files must still be there to re-enable");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Reinstalling_DoesNotSilentlyReEnableAnExtensionTheOperatorTurnedOff()
    {
        ExtensionStore store = InstallSample();
        store.SetEnabled("test.sample", false);

        InstallSample();

        new ExtensionStore(StoreDirectory).Find("test.sample")!.Enabled.Should().BeFalse(
            "an upgrade is not a retraction of the decision to switch something off");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Remove_DeletesTheFilesAndForgetsTheRecord()
    {
        ExtensionStore store = InstallSample();
        string directory = store.DirectoryFor("test.sample");

        store.Remove("test.sample", out string failure).Should().BeTrue(failure);

        Directory.Exists(directory).Should().BeFalse(
            "a removal that forgets the record but leaves the files is a half-completion nothing reports");
        new ExtensionStore(StoreDirectory).Extensions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Remove_RefusesAnIdThatIsNotInstalled()
    {
        InstallSample();

        new ExtensionStore(StoreDirectory).Remove("never.installed", out string failure).Should().BeFalse();
        failure.Should().Contain("never.installed");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Loader_LoadsAnEnabledExtensionAndProducesItsPluginInstances()
    {
        InstallSample();

        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, PluginHostSession.HostApiVersion);

        loader.Plugins.Should().NotBeEmpty("an installed, enabled, compatible extension must actually load");
        loader.OwnerOf(loader.Plugins[0]).Should().Be("test.sample",
            "a failure must be attributable to the extension it came from");
        loader.UnloadAll();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Loader_SkipsADisabledExtensionAndSaysSo()
    {
        ExtensionStore store = InstallSample();
        store.SetEnabled("test.sample", false);

        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, PluginHostSession.HostApiVersion);

        loader.Plugins.Should().BeEmpty();
        loader.Skipped.Should().ContainSingle().Which.Value.Should().Contain("disabled");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Loader_RefusesAnExtensionRequiringANewerHostApi_AndNamesBothVersions()
    {
        InstallSample("test.future", minApiVersion: "99.0.0");

        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, "1.0.0");

        loader.Plugins.Should().BeEmpty();
        loader.Skipped.Should().ContainSingle().Which.Value.Should()
            .Contain("99.0.0").And.Contain("1.0.0");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void Loader_ReportsAnExtensionWhoseAssemblyHasGoneMissing()
    {
        ExtensionStore store = InstallSample();
        File.Delete(store.AssemblyPathFor(store.Find("test.sample")!));

        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, PluginHostSession.HostApiVersion);

        loader.Skipped.Should().ContainSingle().Which.Value.Should().Contain("missing",
            "an extension that vanishes between installs must not simply be absent from the report");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void StartupReport_NamesAFailedExtensionAndItsReason()
    {
        InstallSample("test.future", minApiVersion: "99.0.0");
        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, "1.0.0");

        string rendered = string.Join(Environment.NewLine, ExtensionStartupReport.RenderLines(loader));

        rendered.Should().Contain("test.future").And.Contain("failed").And.Contain("99.0.0");
        rendered.Should().Contain("1 installed -- 0 loaded, 0 disabled, 1 failed",
            "counts printed every time are what give a non-zero one its meaning");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void StartupReport_AnEmptyStoreSaysHowToAddOneRatherThanNothingAtAll()
    {
        ExtensionLoader loader = ExtensionLoader.Load(StoreDirectory, PluginHostSession.HostApiVersion);

        string rendered = string.Join(Environment.NewLine, ExtensionStartupReport.RenderLines(loader));

        rendered.Should().Contain("none installed").And.Contain("extensions install");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void StateFile_ThatCannotBeParsedIsReportedAndNeverSilentlyReplaced()
    {
        InstallSample();
        string statePath = Path.Combine(StoreDirectory, ExtensionStateFile.FileName);
        File.WriteAllText(statePath, "{ this is not a state file");

        var store = new ExtensionStore(StoreDirectory);

        store.StateFailure.Should().NotBeNull(
            "reading a damaged state file as 'nothing is installed' un-installs everything silently");
        File.ReadAllText(statePath).Should().StartWith("{ this is not",
            "the operator's enable/disable choices must survive a read failure");
    }

    [Theory]
    [Trait("Category", "Wiring")]
    [InlineData(new[] { "extensions" }, "action is required")]
    [InlineData(new[] { "extensions", "install" }, "needs a path")]
    [InlineData(new[] { "extensions", "enable" }, "needs an extension id")]
    [InlineData(new[] { "extensions", "frobnicate", "x" }, "unknown action")]
    public void CommandLine_RefusesAnActionThatCouldNotDoAnything(string[] args, string expected)
    {
        ExtensionCommandLine.Parse(args).Error.Should().Contain(expected);
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void CommandLine_ParsesAnInstallWithAnExplicitStoreDirectory()
    {
        ExtensionCommandLine command = ExtensionCommandLine.Parse(
            new[] { "extensions", "install", "./package", "--extension-dir", _workspace.Root });

        command.Error.Should().BeNull();
        command.Action.Should().Be("install");
        command.Target.Should().Be("./package");
        command.Directory.Should().Be(Path.GetFullPath(_workspace.Root));
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void CommandLine_TheServerParserAcceptsTheSameStoreDirectory()
    {
        HostOptions options = CommandLineParser.Parse(
            new[] { "--extension-dir", _workspace.Root }, new HostOptions());

        options.Error.Should().BeNull();
        options.ExtensionDirectory.Should().Be(Path.GetFullPath(_workspace.Root),
            "the directory the subcommand writes to must be the one the running host reads");
    }
}
