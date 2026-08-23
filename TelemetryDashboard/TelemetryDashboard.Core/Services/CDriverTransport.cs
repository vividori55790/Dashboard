using System;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Handing bytes to a UART, which is the only part of the driver a target changes.
/// </summary>
/// <remarks>
/// Split out so the framing above it has one implementation rather than three. The previous driver
/// repeated the whole send routine once per platform, and the checksum bug it shipped with was
/// therefore present three times -- fixing it in the STM32 branch alone would have left ESP32 and
/// Arduino boards transmitting frames the dashboard rejects.
/// </remarks>
public static class CDriverTransport
{
    /// <summary>The one part that differs between targets: handing bytes to a UART.</summary>
    public static string For(string platform)
    {
        if (platform.Contains("ESP32", StringComparison.Ordinal))
        {
            return """
                /* ESP32 transport */
                #include "driver/uart.h"

                static void Telemetry_Transmit(const uint8_t* bytes, size_t length) {
                    uart_write_bytes(UART_NUM_1, (const char*)bytes, length);
                }

                """;
        }

        if (platform.Contains("ARDUINO", StringComparison.Ordinal))
        {
            return """
                /* Arduino transport */
                #include <Arduino.h>

                static void Telemetry_Transmit(const uint8_t* bytes, size_t length) {
                    Serial.write(bytes, length);
                }

                """;
        }

        return """
            /* STM32 HAL transport */
            #include "stm32f4xx_hal.h"
            extern UART_HandleTypeDef huart1;

            static void Telemetry_Transmit(const uint8_t* bytes, size_t length) {
                HAL_UART_Transmit(&huart1, (uint8_t*)bytes, (uint16_t)length, HAL_MAX_DELAY);
            }

            """;
    }
}
