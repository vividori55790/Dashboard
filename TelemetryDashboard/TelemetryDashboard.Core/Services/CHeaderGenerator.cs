namespace TelemetryDashboard.Core.Services;

using System;
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

        if (config.Variables != null && config.Variables.Count > 0)
        {
            foreach (var v in config.Variables)
            {
                string fieldName = SanitizeIdentifier(v.Name);
                string dataType = string.IsNullOrWhiteSpace(v.DataType) ? "float" : v.DataType;
                sb.AppendLine($"    {dataType} {fieldName};");
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

    public string GenerateDriverCode(string? platformOrTemplatePath)
    {
        string platform = string.IsNullOrWhiteSpace(platformOrTemplatePath) ? "STM32" : platformOrTemplatePath.ToUpperInvariant();

        StringBuilder sb = new();
        sb.AppendLine($"/* Auto-Generated Driver Source for {platform} */");
        sb.AppendLine("#include \"telemetry_config.h\"");
        sb.AppendLine("#include <stdio.h>");
        sb.AppendLine("#include <string.h>");
        sb.AppendLine();

        if (platform.Contains("ESP32"))
        {
            sb.AppendLine("/* ESP32 Driver Implementation */");
            sb.AppendLine("#include \"driver/uart.h\"");
            sb.AppendLine();
            sb.AppendLine("void Telemetry_SendPacket(TelemetryData_t* data) {");
            sb.AppendLine("    /* telemetry_send_packet routine */");
            sb.AppendLine("    char buf[TELEMETRY_BUFFER_SIZE];");
            sb.AppendLine("    int len = snprintf(buf, sizeof(buf), \"$%s,%s,%.2f,%.2f*00\\r\\n\", TELEMETRY_TAG, TELEMETRY_NODE_ID, data->temperature, data->vibration);");
            sb.AppendLine("    uart_write_bytes(UART_NUM_1, (const char*)buf, len);");
            sb.AppendLine("}");
        }
        else if (platform.Contains("ARDUINO"))
        {
            sb.AppendLine("/* Arduino Driver Implementation */");
            sb.AppendLine("#include <Arduino.h>");
            sb.AppendLine();
            sb.AppendLine("void Telemetry_SendPacket(TelemetryData_t* data) {");
            sb.AppendLine("    /* telemetry_send_packet routine */");
            sb.AppendLine("    char buf[TELEMETRY_BUFFER_SIZE];");
            sb.AppendLine("    int len = snprintf(buf, sizeof(buf), \"$%s,%s,%.2f,%.2f*00\\r\\n\", TELEMETRY_TAG, TELEMETRY_NODE_ID, data->temperature, data->vibration);");
            sb.AppendLine("    Serial.write((const uint8_t*)buf, len);");
            sb.AppendLine("}");
        }
        else
        {
            // Default STM32
            sb.AppendLine("/* STM32 HAL Driver Implementation */");
            sb.AppendLine("#include \"stm32f4xx_hal.h\"");
            sb.AppendLine("extern UART_HandleTypeDef huart1;");
            sb.AppendLine();
            sb.AppendLine("void Telemetry_SendPacket(TelemetryData_t* data) {");
            sb.AppendLine("    /* telemetry_send_packet routine */");
            sb.AppendLine("    char buf[TELEMETRY_BUFFER_SIZE];");
            sb.AppendLine("    int len = snprintf(buf, sizeof(buf), \"$%s,%s,%.2f,%.2f*00\\r\\n\", TELEMETRY_TAG, TELEMETRY_NODE_ID, data->temperature, data->vibration);");
            sb.AppendLine("    HAL_UART_Transmit(&huart1, (uint8_t*)buf, len, HAL_MAX_DELAY);");
            sb.AppendLine("}");
        }

        sb.AppendLine();
        sb.AppendLine("void telemetry_send_packet(TelemetryData_t* data) {");
        sb.AppendLine("    Telemetry_SendPacket(data);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public string GenerateDriverCode(SensorNodeConfig config, string? platformOrTemplatePath = null)
    {
        string platform = string.IsNullOrWhiteSpace(platformOrTemplatePath)
            ? (config?.TargetPlatform ?? "STM32")
            : platformOrTemplatePath;

        return GenerateDriverCode(platform);
    }

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
