namespace TelemetryDashboard.Core.Interfaces;

public interface IPluginSandbox
{
    void LoadPlugin(string scriptFilePath);
    object ExecuteFilter(string functionName, object telemetryPacket);
    void ReloadAllPlugins();
}
