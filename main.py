# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.1.0 (2026-05-25)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Central workspace entry launcher for real & virtual telemetry monitoring
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.1.0 (2026-05-25) - Antigravity: Added single-instance protection using QSharedMemory.
# * v1.0.0 (2026-05-22) - Antigravity: Initial creation of standard central launcher.
# ======================================================================

import os
import sys
import argparse

def setup_simulation_mock():
    """
    Injects a high-fidelity virtual serial port loopback mock layer.
    This simulates multiple microcontrollers transmitting CSV telemetry.
    """
    import time
    import math
    import random

    class MockSerialPort:
        def __init__(self, port, baudrate, timeout=None, write_timeout=None):
            self.port = port
            self.baudrate = baudrate
            self.is_open = True
            self.last_read_time = time.time()
            self.start_time = time.time()
            print(f"[MOCK SYSTEM]: Virtual serial connection opened on {self.port} at {self.baudrate} baud.")
            
        def close(self):
            self.is_open = False
            print(f"[MOCK SYSTEM]: Virtual serial connection closed on {self.port}.")
            
        def write(self, data):
            cmd = data.decode('utf-8', errors='ignore').strip()
            print(f"[MOCK MCU {self.port} RX Command]: {cmd}")
            
        def flush(self):
            pass
            
        def readline(self):
            # Limit rate to 10Hz to emulate standard microcontroller transmission intervals
            now = time.time()
            elapsed = now - self.last_read_time
            interval = 0.1
            if elapsed < interval:
                time.sleep(interval - elapsed)
            self.last_read_time = time.time()
            
            if not self.is_open:
                return b""
                
            t = time.time() - self.start_time
            
            # COM3 simulates the Engine Subsystem (e.g. RPM, temperature, battery voltage)
            if self.port == "COM3":
                rpm = 3500.0 + 800.0 * math.sin(t * 0.5) + random.uniform(-15.0, 15.0)
                temp = 72.0 + 8.0 * math.sin(t * 0.08) + random.uniform(-0.3, 0.3)
                volt = 13.8 + 0.4 * math.sin(t * 0.15) + random.uniform(-0.08, 0.08)
                line = f"ENG,{rpm:.2f},{temp:.2f},{volt:.2f}\n"
                return line.encode('utf-8')
                
            # COM4 simulates the Battery Subsystem (e.g. Voltage, Current, Temperature, Safety Standby status)
            elif self.port == "COM4":
                vin = 398.5 + 2.5 * math.sin(t * 0.3) + random.uniform(-0.5, 0.5)
                curr = 8.5 + 2.0 * math.sin(t * 0.6) + random.uniform(-0.1, 0.1)
                temp_mos = 42.0 + 12.0 * (curr / 10.0)**2 + random.uniform(-0.15, 0.15)
                temp_trans = 48.0 + 8.0 * (curr / 10.0) + random.uniform(-0.1, 0.1)
                standby = 0.0 if curr > 0.1 else 1.0
                line = f"BAT,{vin:.2f},{curr:.2f},{temp_mos:.2f},{temp_trans:.2f},{standby:.1f}\n"
                return line.encode('utf-8')
                
            else:
                v1 = 100.0 + 20.0 * math.sin(t)
                v2 = 50.0 + 10.0 * math.cos(t)
                line = f"{v1:.3f},{v2:.3f}\n"
                return line.encode('utf-8')

    class MockPortInfo:
        def __init__(self, device, description):
            self.device = device
            self.description = description
            self.manufacturer = "Mock Hardware Ltd."

    def mock_comports():
        return [
            MockPortInfo("COM3", "Mock STM32 Engine controller"),
            MockPortInfo("COM4", "Mock ESP32 Battery sensor node")
        ]

    class MockSerialLib:
        Serial = MockSerialPort
        SerialException = Exception
        
        class tools:
            class list_ports:
                @staticmethod
                def comports():
                    return mock_comports()

    mock_lib = MockSerialLib()
    sys.modules['serial'] = mock_lib
    sys.modules['serial.tools'] = mock_lib.tools
    sys.modules['serial.tools.list_ports'] = mock_lib.tools.list_ports

def has_physical_com_ports():
    """
    Checks if there are any real hardware COM ports available on the PC.
    """
    try:
        import serial.tools.list_ports
        # Filter out mock ports if already loaded
        ports = serial.tools.list_ports.comports()
        real_ports = [p for p in ports if getattr(p, 'manufacturer', '') != "Mock Hardware Ltd."]
        return len(real_ports) > 0
    except:
        return False

def main():
    parser = argparse.ArgumentParser(description="📦 Universal MCU Telemetry Monitoring System Launcher")
    parser.add_argument("--sim", action="store_true", help="Force launch in Virtual Simulation Mode with mock dual-MCU streams")
    parser.add_argument("--hardware", action="store_true", help="Force launch in real hardware serial mode")
    args = parser.parse_args()

    # Move working directory to directory of this script to ensure config.json loads/saves correctly
    script_dir = os.path.dirname(os.path.abspath(__file__))
    os.chdir(script_dir)

    # Resolve execution mode
    run_sim = args.sim
    
    if not args.sim and not args.hardware:
        # Auto-detect physical hardware COM ports
        if not has_physical_com_ports():
            print("[LAUNCHER]: No physical VCP COM serial ports detected on this PC.")
            print("[LAUNCHER]: Defaulting to Virtual Simulation Mode for instant demonstration.")
            run_sim = True
        else:
            print("[LAUNCHER]: Physical VCP COM serial ports detected.")
            print("[LAUNCHER]: Launching in Real Hardware Serial Mode.")

    if run_sim:
        print("[LAUNCHER]: Initializing Virtual Simulation Mode. Mocking STM32/ESP32 serial ports...")
        setup_simulation_mock()

    # Launch GUI
    print("[LAUNCHER]: Launching PyQt6 GUI...")
    from dashboard import DashboardWindow, QApplication
    from PyQt6.QtCore import QSharedMemory
    from PyQt6.QtWidgets import QMessageBox
    
    app = QApplication(sys.argv)

    # Single-Instance Protection using QSharedMemory
    shared_memory_key = "EmbeddedTelemetryMonitor_SingleInstance_Key_v1.0.0"
    shared_memory = QSharedMemory(shared_memory_key)
    if not shared_memory.create(1):
        print("[LAUNCHER]: Embedded Telemetry Monitor is already running. Exiting second instance.")
        msg_box = QMessageBox()
        msg_box.setIcon(QMessageBox.Icon.Warning)
        msg_box.setWindowTitle("Instance Already Active")
        msg_box.setText("Embedded Telemetry Monitor is already running.")
        msg_box.setInformativeText("Only one instance of this application is allowed to run.")
        msg_box.setStandardButtons(QMessageBox.StandardButton.Ok)
        msg_box.exec()
        sys.exit(0)

    # Attach to app to maintain reference lifetime and prevent garbage collection
    app.shared_memory = shared_memory
    
    window = DashboardWindow()
    window.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
