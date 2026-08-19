namespace TelemetryDashboard.Core.Models;

using System.Collections.Generic;

/// <summary>
/// Configuration object for an embedded telemetry sensor node used in C/C++ code generation.
/// </summary>
public sealed class SensorNodeConfig
{
    public string NodeId { get; set; } = "MCU_NODE_1";
    public string TargetPlatform { get; set; } = "STM32";
    public int BaudRate { get; set; } = 115200;
    public int BufferSize { get; set; } = 1024;
    public string TagPrefix { get; set; } = "TELE";
    public List<VariableDefinition> Variables { get; set; } = new();
}
