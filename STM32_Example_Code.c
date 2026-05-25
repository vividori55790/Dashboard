/**
  ******************************************************************************
  * @file           : STM32_Example_Code.c
  * @brief          : STM32 VCP (Virtual COM Port) telemetry & command parser
  *                   for DAB & PSFB Converter Monitor Dashboard.
  * @author         : Power Electronics MCU Systems Expert
  * @note           : This example uses standard STM32 HAL libraries.
  *                   It is designed to be integrated into main.c or a dedicated
  *                   telemetry task (e.g., in FreeRTOS).
  ******************************************************************************
  */

#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>

/* STM32 HAL USB CDC header (Generated automatically by CubeMX) */
#include "usbd_cdc_if.h" 

/* -------------------------------------------------------------------------
 * 1. Telemetry Data Structure
 * ------------------------------------------------------------------------- */
typedef struct {
    float vin;             /* Primary input voltage (V) */
    float iin;             /* Primary input current (A) */
    float vout;            /* Secondary output voltage (V) */
    float iout;            /* Secondary output current (A) */
    float temp_mos;        /* MOSFET heatsink temperature (C) */
    float temp_tx;         /* HF Transformer core temperature (C) */
    float phase_shift;     /* Control variable: Phase shift (deg or normalized) */
    uint16_t status_flags; /* System status flags bitmask:
                              Bit0 = OVP (Over Voltage)
                              Bit1 = OCP (Over Current)
                              Bit2 = OTP (Over Temperature)
                              Bit3 = Standby (1 = stopped, 0 = running) */
} DAB_Telemetry_t;

/* Global instances */
DAB_Telemetry_t g_telemetry = {
    .vin = 380.0f,
    .iin = 0.0f,
    .vout = 0.0f,
    .iout = 0.0f,
    .temp_mos = 25.0f,
    .temp_tx = 25.0f,
    .phase_shift = 0.0f,
    .status_flags = 0x0008 /* Start in Standby */
};

/* Volatile controls regulated by control loops or commands */
volatile bool g_converter_running = false;
volatile float g_target_vout = 12.0f;

/* -------------------------------------------------------------------------
 * 2. Mathematical First-Order Low-Pass Filter (IIR)
 *    Essential for raw ADC sensor noise suppression.
 * ------------------------------------------------------------------------- */
#define LPF_BETA 0.1f  /* Cutoff factor (0.0 to 1.0). Smaller = heavier filtering */
float ADC_LowPassFilter(float raw_in, float last_filtered) {
    return (LPF_BETA * raw_in) + ((1.0f - LPF_BETA) * last_filtered);
}

/* -------------------------------------------------------------------------
 * 3. Telemetry Transmission Frame (MCU -> PC)
 *    Encodes telemetry into a CSV frame with an XOR Checksum.
 * ------------------------------------------------------------------------- */
void DAB_SendTelemetry(void) {
    char tx_buffer[128];
    uint8_t checksum = 0;
    
    /* 1. Format core telemetry variables to CSV.
     *    Omit the leading '$' and trailing Checksum during string printing
     *    to easily calculate the XOR Checksum over the body. */
    int body_len = sprintf(tx_buffer, "DAB,%.2f,%.3f,%.3f,%.2f,%.1f,%.1f,%.2f,%u",
                           g_telemetry.vin,
                           g_telemetry.iin,
                           g_telemetry.vout,
                           g_telemetry.iout,
                           g_telemetry.temp_mos,
                           g_telemetry.temp_tx,
                           g_telemetry.phase_shift,
                           g_telemetry.status_flags);
                           
    /* 2. Compute XOR Checksum over the entire CSV body string */
    for (int i = 0; i < body_len; i++) {
        checksum ^= (uint8_t)tx_buffer[i];
    }
    
    /* 3. Assemble full encapsulated packet: $[BODY]*[CHECKSUM_HEX]\n */
    char full_packet[160];
    int full_len = sprintf(full_packet, "$%s*%02X\r\n", tx_buffer, checksum);
    
    /* 4. Transmit packet over STM32 USB Virtual COM Port (Non-blocking CDC) */
    #ifdef USBD_CDC_IF_H_
    uint8_t result = CDC_Transmit_FS((uint8_t*)full_packet, (uint16_t)full_len);
    if (result != USBD_OK) {
        /* Handle USB Tx congestion/busy state (e.g. port not opened on PC) */
    }
    #endif
}

/* -------------------------------------------------------------------------
 * 4. Control Command Parser (PC -> MCU)
 *    Processes control inputs dispatched from the Dashboard in real-time.
 * ------------------------------------------------------------------------- */
void DAB_ProcessCommand(const char* cmd_str) {
    /* Validate simple frame format: "$CMD,..." */
    if (strncmp(cmd_str, "$CMD,", 5) != 0) {
        return; /* Invalid command structure */
    }
    
    char temp_cmd[64];
    strncpy(temp_cmd, cmd_str, sizeof(temp_cmd));
    temp_cmd[sizeof(temp_cmd)-1] = '\0';
    
    /* Tokenize parsing using string commas */
    char* token = strtok(temp_cmd, ",\r\n"); /* "$CMD" */
    token = strtok(NULL, ",\r\n");            /* Command Action */
    
    if (token == NULL) return;
    
    if (strcmp(token, "RUN") == 0) {
        g_converter_running = true;
        g_telemetry.status_flags &= ~0x0008; /* Clear Standby Flag */
        /* Enable Gate Driver hardware, initialize PWM modules */
        // HAL_TIM_PWM_Start(&htim1, TIM_CHANNEL_1); 
        
    } else if (strcmp(token, "STOP") == 0) {
        g_converter_running = false;
        g_telemetry.status_flags |= 0x0008;  /* Set Standby Flag */
        /* Instantly disable Gate Driver PWM signals for safety! */
        // HAL_TIM_PWM_Stop(&htim1, TIM_CHANNEL_1);
        
    } else if (strcmp(token, "RESET") == 0) {
        /* Clear latching faults */
        g_telemetry.status_flags &= ~0x0007; /* Clear OVP, OCP, OTP faults */
        
    } else if (strcmp(token, "SET_VOUT") == 0) {
        token = strtok(NULL, ",\r\n"); /* Get value string */
        if (token != NULL) {
            float temp_ref = 0.0f;
            if (sscanf(token, "%f", &temp_ref) == 1) {
                /* Set target and bound it between safe system parameters */
                if (temp_ref >= 0.0f && temp_ref <= 60.0f) {
                    g_target_vout = temp_ref;
                }
            }
        }
    }
}

/* -------------------------------------------------------------------------
 * 5. USB CDC Reception Integration (CubeMX CDC Callback hook)
 *    Replace the auto-generated CDC_Receive_FS callback in your project's
 *    `usbd_cdc_if.c` file to route USB RX bytes to the parser.
 * ------------------------------------------------------------------------- */
#define RX_BUFFER_SIZE 128
static char s_rx_line_buffer[RX_BUFFER_SIZE];
static uint16_t s_rx_index = 0;

/**
  * @brief  Hook this function inside USB CDC Receive Callback:
  *         `static int8_t CDC_Receive_FS(uint8_t* Buf, uint32_t *Len)`
  *         in the file: `Src/usbd_cdc_if.c`
  */
int8_t CDC_Receive_Hook(uint8_t* Buf, uint32_t Len) {
    for (uint32_t i = 0; i < Len; i++) {
        char ch = (char)Buf[i];
        
        /* Check boundary overflow */
        if (s_rx_index >= (RX_BUFFER_SIZE - 1)) {
            s_rx_index = 0; /* Flush buffer on overflow */
        }
        
        /* Capture character */
        if (ch == '\n' || ch == '\r') {
            if (s_rx_index > 0) {
                s_rx_line_buffer[s_rx_index] = '\0';
                DAB_ProcessCommand(s_rx_line_buffer);
                s_rx_index = 0; /* Clear line */
            }
        } else {
            s_rx_line_buffer[s_rx_index++] = ch;
        }
    }
    return 0;
}

/* -------------------------------------------------------------------------
 * 6. MCU Mock System Main Loop / Core Task
 *    Simulates a standard FreeRTOS task or bare-metal 100Hz telemetry loop.
 * ------------------------------------------------------------------------- */
void Telemetry_Task_Loop_100Hz(void) {
    /* Fast ADC sensor reads (Called from timer or DMA interrupts in practice) */
    float raw_vin_reading = 380.5f; /* Mock ADC reads */
    float raw_vout_reading = 12.01f;
    float raw_iout_reading = 45.3f;
    
    /* Run digital filters to eliminate high frequency switching noise */
    g_telemetry.vin  = ADC_LowPassFilter(raw_vin_reading, g_telemetry.vin);
    g_telemetry.vout = ADC_LowPassFilter(raw_vout_reading, g_telemetry.vout);
    g_telemetry.iout = ADC_LowPassFilter(raw_iout_reading, g_telemetry.iout);
    
    /* Calculate derived input current based on power & estimated converter efficiency */
    float estimated_eff = 0.94f;
    float calculated_pin = (g_telemetry.vout * g_telemetry.iout) / estimated_eff;
    g_telemetry.iin = calculated_pin / g_telemetry.vin;
    
    /* Fast Hardware Safety Scans */
    if (g_telemetry.vout > 14.5f) { /* OVP limit */
        g_converter_running = false;
        g_telemetry.status_flags |= 0x0001; /* Set OVP Flag */
        g_telemetry.status_flags |= 0x0008; /* Set Standby */
        // Disable PWM immediately!
    }
    
    if (g_telemetry.iout > 100.0f) { /* OCP limit */
        g_converter_running = false;
        g_telemetry.status_flags |= 0x0002; /* Set OCP Flag */
        g_telemetry.status_flags |= 0x0008; /* Set Standby */
        // Disable PWM immediately!
    }
    
    /* Send telemetry packet to USB Host at a standard 50Hz update rate */
    static uint32_t tick_count = 0;
    tick_count++;
    if (tick_count % 2 == 0) {
        DAB_SendTelemetry();
    }
    
    /* Wait 10 ms (Standard scheduler tick) */
    // osDelay(10);
}
