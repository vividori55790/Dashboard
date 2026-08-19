using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F08_CCodeGeneratorTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void CHeaderGenerator_ExportConfig_IncludesHeaderGuard()
    {
        string headerCode = CodeGeneratorHelper.GenerateTelemetryConfigHeader("STM32");
        headerCode.Should().Contain("#ifndef TELEMETRY_CONFIG_H");
        headerCode.Should().Contain("#define TELEMETRY_CONFIG_H");
        headerCode.Should().Contain("#endif");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CHeaderGenerator_ExportConfig_DefinesBaudRates()
    {
        string headerCode = CodeGeneratorHelper.GenerateTelemetryConfigHeader("ESP32");
        headerCode.Should().Contain("#define TELEMETRY_BAUDRATE 115200");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CHeaderGenerator_ExportConfig_GeneratesStructDefinitions()
    {
        string headerCode = CodeGeneratorHelper.GenerateTelemetryConfigHeader("Arduino");
        headerCode.Should().Contain("typedef struct");
        headerCode.Should().Contain("float temperature;");
        headerCode.Should().Contain("float vibration;");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CHeaderGenerator_ExportDriver_GeneratesTransmissionFunctions()
    {
        string sourceCode = CodeGeneratorHelper.GenerateTelemetryDriverSource("STM32");
        sourceCode.Should().Contain("void Telemetry_SendPacket(");
        sourceCode.Should().Contain("HAL_UART_Transmit");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CHeaderGenerator_ExportDriver_IncludesXorChecksumMacro()
    {
        string headerCode = CodeGeneratorHelper.GenerateTelemetryConfigHeader("STM32");
        headerCode.Should().Contain("#define CALCULATE_XOR_CHECKSUM");
    }
}

public static class CodeGeneratorHelper
{
    public static string GenerateTelemetryConfigHeader(string platform)
    {
        return CHeaderGenerator.GenerateTelemetryConfigHeader(platform);
    }

    public static string GenerateTelemetryDriverSource(string platform)
    {
        return CHeaderGenerator.GenerateTelemetryDriverSource(platform);
    }
}
