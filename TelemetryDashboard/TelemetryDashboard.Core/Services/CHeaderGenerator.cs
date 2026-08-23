namespace TelemetryDashboard.Core.Services;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Models;

/// <summary>
/// Service for exporting C/C++ firmware header (telemetry_config.h) and transmission driver source (telemetry_driver.c)
/// for STM32, ESP32, and Arduino target platforms.
/// </summary>
public class CHeaderGenerator
{
    public string GenerateHeader(SensorNodeConfig? config)
    {
        config ??= new SensorNodeConfig();
        string nodeId = string.IsNullOrWhiteSpace(config.NodeId) ? "MCU_NODE_1" : config.NodeId;
        string safeTag = string.IsNullOrWhiteSpace(config.TagPrefix) ? "TELE" : config.TagPrefix;

        StringBuilder sb = new();
        sb.AppendLine($"/* Auto-Generated Telemetry Config Header for {nodeId} */");
        sb.AppendLine("#ifndef TELEMETRY_CONFIG_H");
        sb.AppendLine("#define TELEMETRY_CONFIG_H");
        sb.AppendLine();
        sb.AppendLine("#include <stdint.h>");
        sb.AppendLine("#include <stdbool.h>");
        sb.AppendLine("#include <stddef.h>");
        sb.AppendLine();
        sb.AppendLine($"#define TELEMETRY_NODE_ID \"{nodeId}\"");
        sb.AppendLine($"#define TELEMETRY_BAUDRATE {config.BaudRate}");
        sb.AppendLine($"#define TELEMETRY_BUFFER_SIZE {config.BufferSize}");
        sb.AppendLine($"#define TELEMETRY_TAG \"{safeTag}\"");
        sb.AppendLine();
        sb.AppendLine("/* XOR Checksum Calculation Macro */");
        sb.AppendLine("#define CALCULATE_XOR_CHECKSUM(b, len) \\");
        sb.AppendLine("    ({ uint8_t cs = 0; for (size_t i = 0; i < (len); i++) cs ^= ((const uint8_t*)(b))[i]; cs; })");
        sb.AppendLine();
        sb.AppendLine("/* Telemetry Data Struct */");
        sb.AppendLine("typedef struct {");

        // Through CFieldNames, so the driver's data->field references and these declarations
        // cannot drift: the two files read the same list rather than each sanitising its own.
        IReadOnlyList<CField> fields = CFieldNames.For(config);
        if (fields.Count > 0)
        {
            // No comment naming the original channel. It would carry the raw name into the header,
            // and a raw name is arbitrary text: "°C" makes the file non-ASCII, and a name holding
            // "*/" closes the comment early and breaks the build. The driver states the mapping
            // exactly where it has to exist anyway -- Telemetry_SendField("psfb.output_voltage",
            // data->psfb_output_voltage, "V") -- so nothing is lost by keeping this side plain.
            foreach (CField field in fields)
            {
                sb.AppendLine($"    {field.DataType} {field.Name};");
            }
        }
        else
        {
            sb.AppendLine("    float temperature;");
            sb.AppendLine("    float vibration;");
        }

        sb.AppendLine("    uint32_t timestamp;");
        sb.AppendLine("} TelemetryData_t;");
        sb.AppendLine();
        sb.AppendLine("#endif // TELEMETRY_CONFIG_H");

        return sb.ToString();
    }

    /// <summary>Generates the driver source for <paramref name="platformOrTemplatePath"/>.</summary>
    /// <remarks>Kept so callers that only know a platform still work; it configures nothing.</remarks>
    public string GenerateDriverCode(string? platformOrTemplatePath) =>
        CDriverGenerator.Generate(null, platformOrTemplatePath);

    /// <summary>Generates the driver source that transmits <paramref name="config"/>'s channels.</summary>
    public string GenerateDriverCode(SensorNodeConfig config, string? platformOrTemplatePath = null) =>
        CDriverGenerator.Generate(config, platformOrTemplatePath);

    public static string GenerateTelemetryConfigHeader(string platform)
    {
        var gen = new CHeaderGenerator();
        var config = new SensorNodeConfig { TargetPlatform = platform, BaudRate = 115200 };
        return gen.GenerateHeader(config);
    }

    public static string GenerateTelemetryDriverSource(string platform)
    {
        var gen = new CHeaderGenerator();
        return gen.GenerateDriverCode(platform);
    }

    public static string SanitizeIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "var_default";
        string sanitized = Regex.Replace(input, @"[^\u0000-\u007F]", "_");
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');
        if (string.IsNullOrEmpty(sanitized)) return "var_default";
        if (char.IsDigit(sanitized[0])) sanitized = "var_" + sanitized;
        return sanitized;
    }
}
