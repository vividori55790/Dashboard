using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Streaming;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves the plugin sandbox transforms telemetry rather than returning it untouched,
/// and that generic frame recording is not tied to the demo channel names.
/// </summary>
public class PluginSandboxExecutionTests : IDisposable
{
    private readonly string _pluginDirectory;

    public PluginSandboxExecutionTests()
    {
        _pluginDirectory = Path.Combine(Path.GetTempPath(), "td-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pluginDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pluginDirectory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string WritePlugin(string fileName, string contents)
    {
        string path = Path.Combine(_pluginDirectory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Sandbox_ExecutesFormulaFilterAndTransformsPacket()
    {
        WritePlugin("units.formula", "[to_fahrenheit]\nvalue * 1.8 + 32\n");

        using var sandbox = new ScriptPluginSandbox();
        sandbox.SetPluginsDirectory(_pluginDirectory);
        sandbox.ReloadAllPlugins();

        var packet = new TelemetryPacket("COM3", "temp", 100.0, "C");
        object result = sandbox.ExecuteFilter("to_fahrenheit", packet);

        result.Should().BeOfType<TelemetryPacket>();
        ((TelemetryPacket)result).Value.Should().BeApproximately(212.0, 1e-9);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Sandbox_ResolvesMultipleNamedFieldsInOneExpression()
    {
        WritePlugin("power.formula", "[watts]\nvoltage * current\n");

        using var sandbox = new ScriptPluginSandbox();
        sandbox.SetPluginsDirectory(_pluginDirectory);
        sandbox.ReloadAllPlugins();

        var reading = new Dictionary<string, double> { ["voltage"] = 48.0, ["current"] = 2.5 };
        object result = sandbox.ExecuteFilter("watts", reading);

        result.Should().BeOfType<double>();
        ((double)result).Should().BeApproximately(120.0, 1e-9);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Sandbox_HotReloadPicksUpEditedRule()
    {
        string path = WritePlugin("scale.formula", "[scale]\nvalue * 2\n");

        using var sandbox = new ScriptPluginSandbox();
        sandbox.SetPluginsDirectory(_pluginDirectory);
        sandbox.ReloadAllPlugins();

        var packet = new TelemetryPacket("COM3", "v", 10.0, "V");
        ((TelemetryPacket)sandbox.ExecuteFilter("scale", packet)).Value.Should().Be(20.0);

        File.WriteAllText(path, "[scale]\nvalue * 10\n");
        sandbox.LoadPlugin(path);

        ((TelemetryPacket)sandbox.ExecuteFilter("scale", packet)).Value.Should().Be(100.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Sandbox_ReportsFilesNoEngineCanRunInsteadOfIgnoringThem()
    {
        WritePlugin("legacy.py", "def filter(p): return p\n");

        using var sandbox = new ScriptPluginSandbox();
        sandbox.SetPluginsDirectory(_pluginDirectory);
        sandbox.ReloadAllPlugins();

        // An operator must be able to tell that this plugin is not running.
        sandbox.LoadedPlugins.Should().BeEmpty();
        sandbox.UnsupportedPlugins.Should().ContainKey("legacy.py");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Sandbox_UnknownFunctionLeavesPacketUnchanged()
    {
        WritePlugin("units.formula", "[to_fahrenheit]\nvalue * 1.8 + 32\n");

        using var sandbox = new ScriptPluginSandbox();
        sandbox.SetPluginsDirectory(_pluginDirectory);
        sandbox.ReloadAllPlugins();

        var packet = new TelemetryPacket("COM3", "temp", 100.0, "C");
        object result = sandbox.ExecuteFilter("does_not_exist", packet);

        result.Should().BeSameAs(packet);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FrameRecorder_DiscoversChannelsFromArbitrarySchema()
    {
        // A Modbus power meter frame: none of the bundled demo field names appear.
        var dvr = new TimeTravelDvrPlayer(capacity: 256);
        TelemetryFrameRecorder.Record(dvr,
            "{\"nodeId\":\"METER_1\",\"busVoltage\":48.2,\"loadCurrent\":12.5,\"anomalyScore\":3.9}");

        var frames = dvr.GetFramesInRange(double.MinValue, double.MaxValue);

        frames.Select(f => f.ChannelName)
              .Should().Contain(new[] { "METER_1.busVoltage", "METER_1.loadCurrent" });
        frames.Should().OnlyContain(f => f.IsAnomaly);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void FrameRecorder_WalksNestedFramesIntoDistinctChannels()
    {
        var dvr = new TimeTravelDvrPlayer(capacity: 256);
        TelemetryFrameRecorder.Record(dvr,
            "{\"nodeId\":\"HUB\",\"dab\":{\"nodeId\":\"DAB\",\"temp\":40.5},\"psfb\":{\"nodeId\":\"PSFB\",\"temp\":44.1}}");

        var names = dvr.GetFramesInRange(double.MinValue, double.MaxValue)
                       .Select(f => f.ChannelName).ToList();

        names.Should().Contain("HUB.DAB.temp");
        names.Should().Contain("HUB.PSFB.temp");
    }
}
