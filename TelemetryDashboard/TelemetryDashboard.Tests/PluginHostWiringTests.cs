using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Plugins;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves a plugin is handed the host's live services, not a mock and not nothing.
/// </summary>
/// <remarks>
/// <c>PluginHostContext</c> and its <c>IPluginContext</c> contract both existed and were referenced
/// by no production code: every call to <c>IPlugin.Initialize</c> came from a test holding
/// <c>Mock.Of&lt;IPluginContext&gt;()</c>, so nothing verified that a plugin loaded from disk could
/// reach the host at all. These tests assert reference identity against the same objects the host
/// is running, because a context carrying freshly built stand-ins would satisfy every type check
/// and still leave the plugin looking at a system nobody is driving.
/// </remarks>
public class PluginHostWiringTests : IDisposable
{
    private readonly string _storePath = Path.Combine(
        Path.GetTempPath(), $"plugin-wiring-{Guid.NewGuid():N}.db");

    private readonly List<string> _log = new();
    private readonly DataRouter _router = new();
    private readonly Mock<ISerialManager> _serial = new();
    private SqliteDataLogger? _logger;

    public void Dispose()
    {
        _logger?.Dispose();
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    private PluginHostServices Services()
    {
        _logger = new SqliteDataLogger(_storePath);
        return new PluginHostServices(_router, _serial.Object, _logger, (line, _) => _log.Add(line));
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InitializePlugin_GivesThePluginTheHostsOwnRouterSerialManagerAndLogger()
    {
        PluginHostServices services = Services();
        var manager = new PluginManager(services);
        var plugin = new ContextCapturingPlugin();

        manager.InitializePlugin(plugin);

        plugin.Context.Should().BeOfType<PluginHostContext>(
            "the host's own context type is what a plugin must receive, not a stand-in");
        plugin.Context!.Router.Should().BeSameAs(_router);
        plugin.Context.SerialManager.Should().BeSameAs(_serial.Object);
        plugin.Context.Logger.Should().BeSameAs(_logger);
        manager.ActivePlugins.Should().Contain(plugin);
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InitializePlugin_RoutesPluginLogLinesToTheHostSinkTaggedWithThePluginId()
    {
        var manager = new PluginManager(Services());

        manager.InitializePlugin(new ContextCapturingPlugin());

        _log.Should().ContainSingle()
            .Which.Should().Be("[plugin:capture.plugin] initialised",
                "an operator must be able to tell a plugin's claim from the host's own");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void InitializePlugin_WithoutHostServices_ThrowsRatherThanSubstitutingAnInertContext()
    {
        var manager = new PluginManager();
        var plugin = new ContextCapturingPlugin();

        Action act = () => manager.InitializePlugin(plugin);

        act.Should().Throw<InvalidOperationException>();
        plugin.Context.Should().BeNull("a plugin must never be told it started against nothing");
        manager.ActivePlugins.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public async Task InitializePlugin_LogWritesReachTheRealDataLogger()
    {
        PluginHostServices services = Services();
        var manager = new PluginManager(services);
        var plugin = new ContextCapturingPlugin();
        manager.InitializePlugin(plugin);

        await plugin.Context!.Logger.WriteAsync(new TelemetryPacket("NODE_1", "TEMP", 21.5, "C"));

        _logger!.WrittenCount.Should().Be(1, "the plugin's logger is the host's durable store");
    }

    [Fact]
    [Trait("Category", "Wiring")]
    public void PluginHostSession_InitialisesAPluginDiscoveredOnDiskAgainstTheLiveRouter()
    {
        string directory = StageSamplePluginAssembly();
        var options = new HostOptions { PluginDirectory = directory };

        using PluginHostSession session = PluginHostSession.Start(options, _router, _serial.Object);

        session.FailedPlugins.Should().BeEmpty();
        session.ActivePlugins.Should().Contain(p => p.Id == "sample.plugin",
            "the assembly the build stages into plugins/ must actually come up");

        // The drain calls this explicitly, before the port closes.
        session.Dispose();
        session.ActivePlugins.Should().BeEmpty("shutdown must release the plugins, not just stop");
    }

    /// <summary>
    /// Copies the built sample plugin into a private directory, the way a deployment would.
    /// </summary>
    /// <remarks>
    /// A copy rather than the staged folder itself: the assembly is loaded from its bytes into a
    /// collectible context, and pointing several tests at one file invites a lock nobody owns.
    /// </remarks>
    private static string StageSamplePluginAssembly()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"plugin-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        string source = typeof(Plugins.SamplePlugins.SampleTelemetryPlugin).Assembly.Location;
        File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: true);
        return directory;
    }

    /// <summary>A plugin that keeps whatever context it was handed, so the test can inspect it.</summary>
    private sealed class ContextCapturingPlugin : IPlugin
    {
        public string Id => "capture.plugin";

        public string Name => "Context Capturing Plugin";

        public string Version => "1.0.0";

        public IPluginContext? Context { get; private set; }

        public void Initialize(IPluginContext context)
        {
            Context = context;
            context.Log("initialised");
        }

        public void OnPacketReceived(TelemetryPacket packet)
        {
        }

        public bool TryCustomParse(RawPacket rawPacket, out IEnumerable<TelemetryPacket> parsedPackets)
        {
            parsedPackets = Array.Empty<TelemetryPacket>();
            return false;
        }

        public void Shutdown()
        {
        }
    }
}
