# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.2.0 (2026-05-25)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Central workspace entry launcher for real & virtual telemetry monitoring
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.2.0 (2026-05-25) - Antigravity: Restructured codebase by moving modular source files into 'src/' folder.
# * v1.1.0 (2026-05-25) - Antigravity: Added single-instance protection using QSharedMemory.
# * v1.0.0 (2026-05-22) - Antigravity: Initial creation of standard central launcher.
# ======================================================================

import os
import sys
import argparse

# Dynamically inject 'src' directory into Python search path to resolve modular imports cleanly
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src'))

def setup_simulation_mock():
    """
    Injects a high-fidelity virtual serial port loopback mock layer.
    This simulates multiple microcontrollers transmitting CSV telemetry,
    with advanced support for Protocol Analyzer ($HEX) and PC-led Resync ($HIST) recovery.
    """
    import simulation_mock
    simulation_mock.setup_simulation_mock()

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
    from PyQt6.QtGui import QIcon
    
    app = QApplication(sys.argv)

    # Set AppUserModelID to display custom taskbar icon correctly on Windows script launch
    import ctypes
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("google.deepmind.embeddedtelemetrydashboard.v1")
    except:
        pass

    # Load and set application wide icon
    if os.path.exists("Logo_Gemini.png"):
        app.setWindowIcon(QIcon("Logo_Gemini.png"))

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
