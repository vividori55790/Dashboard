namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Definition of a telemetry variable emitted by an embedded sensor node.
/// </summary>
public sealed class VariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string DataType { get; set; } = "float";
}
