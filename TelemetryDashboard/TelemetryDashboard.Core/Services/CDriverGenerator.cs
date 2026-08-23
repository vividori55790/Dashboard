using System;
using System.Collections.Generic;
using System.Text;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// The driver source that puts this product's own frames on a wire.
/// </summary>
/// <remarks>
/// What it emits is the format <see cref="Parsers.PrefixParser"/> reads and
/// <see cref="DefaultRoutingRules"/> registers — <c>$TAG,node,channel,value,unit*XX</c>, one frame
/// per channel, checksummed over everything between the <c>$</c> and the <c>*</c>. Firmware
/// generated from a dashboard that cannot then be read by that dashboard is worse than none: it
/// looks like a working integration until the frames arrive and are silently discarded.
/// <para>
/// The previous driver emitted <c>"$%s,%s,%.2f,%.2f*00"</c>. Three things were wrong with it and
/// each was enough on its own. It sent two values positionally where the parser expects a named
/// channel and a unit. It read <c>data-&gt;temperature</c> and <c>data-&gt;vibration</c>, which
/// exist in no struct this generator writes unless the configuration happens to declare them. And
/// the checksum was the literal <c>00</c>, so every frame failed validation — the header defined
/// <c>CALCULATE_XOR_CHECKSUM</c> and nothing called it.
/// </para>
/// </remarks>
public static class CDriverGenerator
{
    /// <summary>Digits after the decimal point in a transmitted value.</summary>
    /// <remarks>
    /// Three, which is milli-resolution on a volt or an amp and is what this product's converters
    /// are read to. Not the channel's display decimals: rounding on the wire discards measurement
    /// to match a screen, and the dashboard can always show fewer digits than it was sent.
    /// </remarks>
    public const int ValueDecimals = 3;

    /// <summary>Generates the driver for <paramref name="config"/>.</summary>
    public static string Generate(SensorNodeConfig? config, string? platformOverride = null)
    {
        string platform = Platform(config, platformOverride);
        IReadOnlyList<CField> fields = CFieldNames.For(config);

        var text = new StringBuilder();
        text.AppendLine($"/* Auto-Generated Driver Source for {platform} */");
        text.AppendLine("/* Save as UTF-8: a unit such as °C is two bytes on the wire, and the");
        text.AppendLine("   checksum below covers bytes, which is what the dashboard validates. */");
        text.AppendLine("#include \"telemetry_config.h\"");
        text.AppendLine("#include <stdio.h>");
        text.AppendLine("#include <string.h>");
        text.AppendLine();
        text.Append(CDriverTransport.For(platform));
        text.AppendLine();

        // Omitted with no channels: a static helper nothing calls is an unused-function warning on
        // every toolchain, and generated code that warns teaches an engineer to ignore warnings.
        if (fields.Count > 0)
        {
            text.Append(SendField());
            text.AppendLine();
        }

        text.Append(SendPacket(fields));
        return text.ToString();
    }

    private static string Platform(SensorNodeConfig? config, string? platformOverride)
    {
        string chosen = string.IsNullOrWhiteSpace(platformOverride)
            ? config?.TargetPlatform ?? string.Empty
            : platformOverride;

        return string.IsNullOrWhiteSpace(chosen) ? "STM32" : chosen.ToUpperInvariant();
    }

    /// <summary>One framed, checksummed channel reading.</summary>
    private static string SendField() => $$"""
        /* $TAG,node,channel,value,unit*XX -- the checksum covers everything between
           the '$' and the '*', which is exactly what the dashboard recomputes. A frame
           that would not fit the buffer is dropped rather than sent truncated: half a
           frame reaches the parser as a checksum failure, indistinguishable from noise. */
        static void Telemetry_SendField(const char* channel, float value, const char* unit) {
            char body[TELEMETRY_BUFFER_SIZE];
            char line[TELEMETRY_BUFFER_SIZE];

            int written = snprintf(body, sizeof(body), "%s,%s,%s,%.{{ValueDecimals}}f,%s",
                                   TELEMETRY_TAG, TELEMETRY_NODE_ID, channel, value, unit);
            if (written < 0 || (size_t)written >= sizeof(body)) return;

            uint8_t checksum = CALCULATE_XOR_CHECKSUM(body, (size_t)written);

            int length = snprintf(line, sizeof(line), "$%s*%02X\r\n", body, checksum);
            if (length < 0 || (size_t)length >= sizeof(line)) return;

            Telemetry_Transmit((const uint8_t*)line, (size_t)length);
        }

        """;

    /// <summary>One call per configured channel, naming the struct members the header declares.</summary>
    private static string SendPacket(IReadOnlyList<CField> fields)
    {
        var text = new StringBuilder();
        text.AppendLine("void Telemetry_SendPacket(const TelemetryData_t* data) {");

        if (fields.Count == 0)
        {
            text.AppendLine("    /* No channels were configured, so there is nothing to send. */");
            text.AppendLine("    (void)data;");
        }

        foreach (CField field in fields)
        {
            string unit = Escape(field.Variable.Unit ?? string.Empty);
            text.AppendLine($"    Telemetry_SendField(\"{Escape(field.Variable.Name)}\", "
                + $"(float)data->{field.Name}, \"{unit}\");");
        }

        text.AppendLine("}");
        text.AppendLine();
        text.AppendLine("void telemetry_send_packet(TelemetryData_t* data) {");
        text.AppendLine("    Telemetry_SendPacket(data);");
        text.AppendLine("}");
        return text.ToString();
    }

    /// <summary>Makes a channel name or unit safe to sit inside a C string literal.</summary>
    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
           .Replace("\"", "\\\"", StringComparison.Ordinal)
           .Replace("\r", string.Empty, StringComparison.Ordinal)
           .Replace("\n", string.Empty, StringComparison.Ordinal);
}
