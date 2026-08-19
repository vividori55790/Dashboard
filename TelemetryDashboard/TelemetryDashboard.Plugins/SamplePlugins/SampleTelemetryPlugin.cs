namespace TelemetryDashboard.Plugins.SamplePlugins;

using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

public class SampleTelemetryPlugin : IPlugin
{
    public string Id => "sample.plugin";
    public string Name => "Sample Telemetry Plugin";
    public string Version => "1.0.0";

    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;
        _context.Log("SampleTelemetryPlugin initialized.");
    }

    public void OnPacketReceived(TelemetryPacket packet)
    {
        // Sample plugin packet processing hook
    }

    public bool TryCustomParse(RawPacket rawPacket, out IEnumerable<TelemetryPacket> parsedPackets)
    {
        parsedPackets = Enumerable.Empty<TelemetryPacket>();
        return false;
    }

    public void Shutdown()
    {
        _context?.Log("SampleTelemetryPlugin shutdown.");
    }
}
