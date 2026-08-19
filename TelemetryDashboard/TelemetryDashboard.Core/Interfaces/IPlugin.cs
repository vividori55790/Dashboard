namespace TelemetryDashboard.Core.Interfaces;

using TelemetryDashboard.Core.Models;

public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IPluginContext
{
    IDataRouter Router { get; }
    ISerialManager SerialManager { get; }
    IDataLogger Logger { get; }
    void Log(string message, PluginLogLevel level = PluginLogLevel.Info);
}

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    
    void Initialize(IPluginContext context);
    void OnPacketReceived(TelemetryPacket packet);
    bool TryCustomParse(RawPacket rawPacket, out IEnumerable<TelemetryPacket> parsedPackets);
    void Shutdown();
}
