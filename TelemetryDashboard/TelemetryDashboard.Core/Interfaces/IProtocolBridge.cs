namespace TelemetryDashboard.Core.Interfaces;

public interface IProtocolBridge
{
    string ProtocolName { get; }
    byte[] ConvertToStandardPacket(byte[] rawPayload);
    byte[] ConvertFromStandardPacket(object standardTelemetry);
}
