namespace TelemetryDashboard.Core.Interfaces;

using TelemetryDashboard.Core.Models;

public interface IDataRouter
{
    event EventHandler<TelemetryPacket>? PacketRouted;
    void RegisterNode(SensorNode node);
    SensorNode GetNode(string nodeId);
    bool RegisterRule(RoutingRule rule);
    bool UnregisterRule(string ruleId);
    IEnumerable<TelemetryPacket> Route(RawPacket rawPacket);
}
