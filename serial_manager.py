# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular MultiPortSerialManager and updated SerialHandlerThread.
# ======================================================================

import time
import serial
import serial.tools.list_ports
from PyQt6.QtCore import QThread, pyqtSignal, QObject

class SerialHandlerThread(QThread):
    """
    Background worker thread dedicated to managing a SINGLE serial connection.
    Does NOT contain variable parsing logic to ensure lightweight and specialized operation.
    """
    # Signals tagged with the source port name
    raw_packet_received = pyqtSignal(str, str)             # PortName, LineString
    connection_status_changed = pyqtSignal(str, bool, str) # PortName, Connected, Message
    error_occurred = pyqtSignal(str, str)                  # PortName, ErrorMessage

    def __init__(self, port, baudrate=115200):
        super().__init__()
        self.port = port
        self.baudrate = baudrate
        self.is_running = True
        self.serial_conn = None

    def send_cmd(self, cmd_string):
        """
        Transmits a raw command string to the target port.
        """
        if self.serial_conn and self.serial_conn.is_open:
            try:
                packet = cmd_string.encode('ascii', errors='ignore')
                if not packet.endswith(b'\n'):
                    packet += b'\n'
                self.serial_conn.write(packet)
                self.serial_conn.flush()
            except Exception as e:
                self.error_occurred.emit(self.port, f"Serial TX Error: {str(e)}")

    def run(self):
        self.is_running = True
        last_state = None # None, True (connected), False (failed/disconnected)

        while self.is_running:
            # Physical Serial port acquisition
            if not self.serial_conn or not self.serial_conn.is_open:
                if last_state is not False:
                    self.connection_status_changed.emit(self.port, False, f"Connecting to {self.port}...")
                
                try:
                    self.serial_conn = serial.Serial(
                        port=self.port,
                        baudrate=self.baudrate,
                        timeout=0.5,
                        write_timeout=0.5
                    )
                    self.connection_status_changed.emit(self.port, True, f"Connected to {self.port}")
                    last_state = True
                except Exception as e:
                    if last_state != False:
                        self.connection_status_changed.emit(self.port, False, f"Connection Failed: {str(e)}")
                        last_state = False
                    time.sleep(1.0) # Wait before retry
                    continue

            # Read and emit raw data
            try:
                line_bytes = self.serial_conn.readline()
                if not line_bytes:
                    continue # Timeout, retry
                
                line_str = line_bytes.decode('utf-8', errors='ignore').strip()
                if not line_str:
                    continue
                
                # Deliver to data router
                self.raw_packet_received.emit(self.port, line_str)
                
            except serial.SerialException as se:
                self.error_occurred.emit(self.port, f"Serial port disconnected: {str(se)}")
                self.connection_status_changed.emit(self.port, False, "Disconnected")
                last_state = False
                try:
                    self.serial_conn.close()
                except:
                    pass
                self.serial_conn = None
                time.sleep(1.0)
            except Exception as e:
                self.error_occurred.emit(self.port, f"Read Exception: {str(e)}")

        # Cleanup port on shutdown
        if self.serial_conn and self.serial_conn.is_open:
            try:
                self.serial_conn.close()
            except:
                pass
            self.serial_conn = None
            
        self.connection_status_changed.emit(self.port, False, "Disconnected (Thread Stopped)")

    def stop(self):
        self.is_running = False
        self.wait()


class MultiPortSerialManager(QObject):
    """
    Coordinates and monitors multiple concurrent SerialHandlerThread channels.
    Provides uniform signal broadcasting.
    """
    raw_packet_received = pyqtSignal(str, str)             # PortName, LineString
    connection_status_changed = pyqtSignal(str, bool, str) # PortName, Connected, Message
    error_occurred = pyqtSignal(str, str)                  # PortName, ErrorMessage

    def __init__(self):
        super().__init__()
        self.active_threads = {} # port_name -> SerialHandlerThread

    @staticmethod
    def list_available_ports():
        """
        Scan and return a list of physical serial ports available.
        """
        ports = serial.tools.list_ports.comports()
        return [{"port": p.device, "description": p.description} for p in ports]

    def start_monitoring(self, port_name, baudrate=115200):
        """
        Starts a background reader thread for the specified port.
        """
        if port_name in self.active_threads:
            self.stop_monitoring(port_name)

        thread = SerialHandlerThread(port_name, int(baudrate))
        thread.raw_packet_received.connect(self.raw_packet_received.emit)
        thread.connection_status_changed.connect(self.connection_status_changed.emit)
        thread.error_occurred.connect(self.error_occurred.emit)
        
        self.active_threads[port_name] = thread
        thread.start()

    def stop_monitoring(self, port_name):
        """
        Gracefully halts a port thread and clears its pointer.
        """
        if port_name in self.active_threads:
            thread = self.active_threads.pop(port_name)
            thread.stop()

    def send_command(self, port_name, cmd_string):
        """
        Sends an command string to a specific COM port.
        """
        if port_name in self.active_threads:
            self.active_threads[port_name].send_cmd(cmd_string)

    def broadcast_command(self, cmd_string):
        """
        Sends an command string to ALL connected MCU ports.
        """
        for thread in self.active_threads.values():
            thread.send_cmd(cmd_string)

    def is_running(self, port_name):
        return port_name in self.active_threads and self.active_threads[port_name].isRunning()

    def stop_all(self):
        """
        Halts all active background ports cleanly.
        """
        ports = list(self.active_threads.keys())
        for p in ports:
            self.stop_monitoring(p)

    # ==========================================
    # [COMPATIBILITY ALIASES FOR MAIN WINDOW]
    # ==========================================
    def connect_port(self, port_name, baudrate=115200):
        self.start_monitoring(port_name, baudrate)

    def disconnect_port(self, port_name):
        self.stop_monitoring(port_name)

    def disconnect_all(self):
        self.stop_all()
