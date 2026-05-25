# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.1.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Consolidated CSV Logger & Web Streamer Plugin
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.1.0 (2026-05-22) - Antigravity: Consolidated matlab_logger.py and web_streamer.py into a single high-density console.
# ======================================================================

import os
import csv
import json
import time
import socket
import threading
import http.server
import socketserver
import webbrowser
import queue
from datetime import datetime

from PyQt6.QtWidgets import (
    QDockWidget, QWidget, QVBoxLayout, QHBoxLayout,
    QGroupBox, QPushButton, QLabel, QSpinBox, QFileDialog
)
from PyQt6.QtCore import Qt, QTimer, pyqtSlot
from plugins.base_plugin import BasePlugin

# ======================================================================
# [WEB STREAM INFRASTRUCTURE]
# ======================================================================
class TelemetryStreamServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    def __init__(self, server_address, RequestHandlerClass, plugin):
        super().__init__(server_address, RequestHandlerClass)
        self.plugin = plugin
        self.clients = []
        self.lock = threading.Lock()
    
    def broadcast(self, data):
        with self.lock:
            dead_clients = []
            json_str = json.dumps(data)
            for client in self.clients:
                try:
                    client.send_event(json_str)
                except Exception:
                    dead_clients.append(client)
            for client in dead_clients:
                if client in self.clients:
                    self.clients.remove(client)

class TelemetryStreamHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass # Suppress command line console noise
        
    def send_event(self, data):
        try:
            self.wfile.write(f"data: {data}\n\n".encode('utf-8'))
            self.wfile.flush()
        except socket.error as e:
            raise e

    def do_GET(self):
        if self.path == '/stream':
            self.send_response(200)
            self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Cache-Control', 'no-cache')
            self.send_header('Connection', 'keep-alive')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            self.server.clients.append(self)
            try:
                while self.server.plugin.is_streaming:
                    time.sleep(0.5)
            except Exception:
                pass
            finally:
                if self in self.server.clients:
                    self.server.clients.remove(self)
        elif self.path == '/api/config':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            router = self.server.plugin.main_window.data_router
            cfg_data = []
            for name, sub in router.subsystems.items():
                vars_list = []
                for v in sub.variables:
                    vars_list.append({
                        "name": v["name"],
                        "display_name": v["display_name"],
                        "unit": v["unit"],
                        "is_numerical": v["is_numerical"]
                    })
                cfg_data.append({
                    "name": sub.name,
                    "display_name": sub.display_name,
                    "variables": vars_list,
                    "thresholds": sub.thresholds
                })
            self.wfile.write(json.dumps(cfg_data).encode('utf-8'))
        elif self.path == '/' or self.path == '/index.html':
            try:
                self.send_response(200)
                self.send_header('Content-Type', 'text/html; charset=utf-8')
                self.end_headers()
                html_path = os.path.join(os.path.dirname(__file__), "..", "stream_client.html")
                if os.path.exists(html_path):
                    with open(html_path, 'r', encoding='utf-8') as f:
                        self.wfile.write(f.read().encode('utf-8'))
                else:
                    self.wfile.write("<h1>Stream Client HTML missing!</h1>".encode('utf-8'))
            except Exception as e:
                try:
                    self.send_error(500, f"Error: {str(e)}")
                except Exception:
                    pass
        else:
            self.send_error(404, "File Not Found")

# ======================================================================
# [CONSOLIDATED PLUGIN MODULE]
# ======================================================================
class ServiceConsolePlugin(BasePlugin):
    """
    Consolidated utilities hub coordinating both MATLAB-compatible CSV recording
    and local HTTP server real-time Web Streaming.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "service_console"
        self.name = "Services Console"
        self.description = "Consolidated telemetry CSV logger and HTTP Web Streaming server."
        
        # 1. Logger variables
        self.log_file = None
        self.log_writer = None
        self.is_recording = False
        self.recorded_count = 0
        self.start_time = 0.0
        self.active_vars_map = {}
        
        # 2. Web Streamer variables
        self.is_streaming = False
        self.stream_server = None
        self.stream_thread = None
        self.stream_port = 8080
        
        # Outgoing SSE queue and thread worker to prevent thread explosion
        self.outgoing_queue = queue.Queue()
        self.sse_worker_thread = None
        
        # UI Timers
        self.ui_timer = None
        self.blink_state = False

    def on_enable(self):
        self.dock_widget = QDockWidget("⚙️ Services Hub (Logging & Streaming)", self.main_window)
        self.dock_widget.setObjectName("dock_services_hub")
        
        main_container = QWidget()
        main_lay = QHBoxLayout(main_container)
        main_lay.setContentsMargins(8, 8, 8, 8)
        main_lay.setSpacing(10)
        
        # ==================================================================
        # GROUP A: MATLAB CSV TELEMETRY LOGGER
        # ==================================================================
        log_group = QGroupBox("💾 CSV Telemetry Recorder")
        log_v_lay = QVBoxLayout(log_group)
        log_v_lay.setContentsMargins(8, 12, 8, 8)
        log_v_lay.setSpacing(6)
        
        self.lbl_log_status = QLabel("Recorder: STANDBY")
        self.lbl_log_status.setStyleSheet("font-size: 11px; font-weight: bold; color: #8c91a5;")
        log_v_lay.addWidget(self.lbl_log_status)
        
        log_btns = QHBoxLayout()
        self.btn_log_start = QPushButton("🔴 Start Log")
        self.btn_log_start.clicked.connect(self.start_csv_logging)
        self.btn_log_start.setStyleSheet("background-color: #2a1b1b; border-color: #5a2e2e; color: #f87171;")
        
        self.btn_log_stop = QPushButton("⏹️ Stop Log")
        self.btn_log_stop.setEnabled(False)
        self.btn_log_stop.clicked.connect(self.stop_csv_logging)
        
        log_btns.addWidget(self.btn_log_start)
        log_btns.addWidget(self.btn_log_stop)
        log_v_lay.addLayout(log_btns)
        
        main_lay.addWidget(log_group, 5)
        
        # ==================================================================
        # GROUP B: REAL-TIME WEB TELEMETRY SERVER
        # ==================================================================
        stream_group = QGroupBox("🌐 Web Stream Server")
        stream_v_lay = QVBoxLayout(stream_group)
        stream_v_lay.setContentsMargins(8, 12, 8, 8)
        stream_v_lay.setSpacing(6)
        
        self.lbl_stream_status = QLabel("Server: OFFLINE")
        self.lbl_stream_status.setStyleSheet("font-size: 11px; font-weight: bold; color: #d97706;")
        stream_v_lay.addWidget(self.lbl_stream_status)
        
        port_lay = QHBoxLayout()
        port_lay.addWidget(QLabel("Listen Port:"))
        self.spin_port = QSpinBox()
        self.spin_port.setRange(1024, 65535)
        self.spin_port.setValue(self.stream_port)
        self.spin_port.setFixedWidth(80)
        self.spin_port.valueChanged.connect(self.update_stream_port)
        port_lay.addWidget(self.spin_port)
        port_lay.addStretch()
        stream_v_lay.addLayout(port_lay)
        
        stream_btns = QHBoxLayout()
        self.btn_stream_toggle = QPushButton("🌐 Start Server")
        self.btn_stream_toggle.clicked.connect(self.toggle_stream_server)
        
        self.btn_stream_open = QPushButton("🔗 Open Web UI")
        self.btn_stream_open.setEnabled(False)
        self.btn_stream_open.clicked.connect(self.open_browser)
        
        stream_btns.addWidget(self.btn_stream_toggle)
        stream_btns.addWidget(self.btn_stream_open)
        stream_v_lay.addLayout(stream_btns)
        
        main_lay.addWidget(stream_group, 6)
        
        self.container = main_container
        self.dock_widget.setWidget(main_container)
        
        # Connect router signals
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        
        # GUI status blinker timer
        self.ui_timer = QTimer(self.dock_widget)
        self.ui_timer.timeout.connect(self.on_ui_timer_tick)
        self.ui_timer.start(500)
        
        # Autostart web streaming server if default
        self.start_web_server()

    def on_disable(self):
        self.stop_csv_logging()
        self.stop_web_server()
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if self.ui_timer:
            self.ui_timer.stop()
            self.ui_timer = None
        if hasattr(self, "container") and self.container:
            self.container.deleteLater()
            self.container = None
        super().on_disable()

    # ======================================================================
    # LOGGER LOGIC
    # ======================================================================
    def start_csv_logging(self):
        router = self.main_window.data_router
        if not router.subsystems:
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic("ERROR: Cannot record. No active subsystems configured!")
            return
            
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        default_filename = f"Telemetry_Log_{timestamp}.csv"
        
        file_path, _ = QFileDialog.getSaveFileName(
            self.main_window, "Create Dynamic Telemetry CSV Log", 
            os.path.join(os.path.expanduser("~"), default_filename),
            "CSV Files (*.csv)"
        )
        if not file_path:
            return
            
        try:
            self.log_file = open(file_path, mode='w', newline='', encoding='utf-8')
            self.log_writer = csv.writer(self.log_file)
            
            # Formulate headers dynamically
            headers = ["Timestamp_s", "Subsystem"]
            self.active_vars_map = {}
            
            for sub_name, sub in router.subsystems.items():
                self.active_vars_map[sub_name] = [v["name"] for v in sub.variables]
                for v in sub.variables:
                    headers.append(f"{sub_name}_{v['name']}")
                    
            self.log_writer.writerow(headers)
            self.log_file.flush()
            
            self.start_time = time.time()
            self.is_recording = True
            self.recorded_count = 0
            self.btn_log_start.setEnabled(False)
            self.btn_log_stop.setEnabled(True)
            self.btn_log_start.setStyleSheet("background-color: #272a38; border-color: #282b3d; color: #8c91a5;")
            
            self.lbl_log_status.setText("Recorder: RECORDING 🔴")
            self.lbl_log_status.setStyleSheet("color: #dc2626; font-weight: bold;")
            
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"Telemetry logging started: {os.path.basename(file_path)}")
        except Exception as e:
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: Cannot create CSV file: {str(e)}")

    def stop_csv_logging(self):
        if not self.is_recording:
            return
            
        self.is_recording = False
        try:
            if self.log_file:
                self.log_file.close()
                self.log_file = None
                self.log_writer = None
                
            self.btn_log_start.setEnabled(True)
            self.btn_log_stop.setEnabled(False)
            self.btn_log_start.setStyleSheet("background-color: #2a1b1b; border-color: #5a2e2e; color: #f87171;")
            self.lbl_log_status.setText(f"Recorder: SAVED ({self.recorded_count} rows)")
            self.lbl_log_status.setStyleSheet("color: #059669; font-weight: bold;")
            
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"Telemetry logging saved successfully. Count: {self.recorded_count}")
        except Exception as e:
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: Failed to save log: {str(e)}")

    # ======================================================================
    # STREAMER LOGIC
    # ======================================================================
    def update_stream_port(self, val):
        self.stream_port = val

    def toggle_stream_server(self):
        if self.is_streaming:
            self.stop_web_server()
        else:
            self.start_web_server()

    def start_web_server(self):
        if self.is_streaming:
            return
            
        try:
            self.is_streaming = True
            
            # Clear previous queue
            while not self.outgoing_queue.empty():
                try:
                    self.outgoing_queue.get_nowait()
                except:
                    break
            
            self.stream_server = TelemetryStreamServer(
                ("", self.stream_port), TelemetryStreamHandler, self
            )
            self.stream_thread = threading.Thread(target=self.stream_server.serve_forever, daemon=True)
            self.stream_thread.start()
            
            # Start SSE broadcast Queue daemon worker thread
            self.sse_worker_thread = threading.Thread(target=self._sse_broadcast_loop, daemon=True)
            self.sse_worker_thread.start()
            
            self.lbl_stream_status.setText(f"Server: ONLINE (Port {self.stream_port})")
            self.lbl_stream_status.setStyleSheet("font-size: 11px; font-weight: bold; color: #059669;")
            self.btn_stream_toggle.setText("🌐 Stop Server")
            self.btn_stream_open.setEnabled(True)
            self.spin_port.setEnabled(False)
            
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"INFO: Web stream server listening on http://localhost:{self.stream_port}")
                
        except Exception as e:
            self.is_streaming = False
            self.lbl_stream_status.setText("Server: PORT CONFLICT / ERROR")
            self.lbl_stream_status.setStyleSheet("font-size: 11px; font-weight: bold; color: #dc2626;")
            self.btn_stream_toggle.setText("🌐 Start Server")
            self.btn_stream_open.setEnabled(False)
            self.spin_port.setEnabled(True)
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: Web stream server startup failed: {str(e)}")

    def _sse_broadcast_loop(self):
        """
        Background loop consuming telemetry packets from queue and broadcasting them,
        mitigating thread explosion under high telemetry transmission rates.
        """
        while self.is_streaming:
            try:
                # Use a timeout to regularly check is_streaming state
                packet = self.outgoing_queue.get(timeout=0.1)
                if self.stream_server:
                    self.stream_server.broadcast(packet)
                self.outgoing_queue.task_done()
            except Exception:
                continue

    def stop_web_server(self):
        if not self.is_streaming:
            return
            
        self.is_streaming = False
        try:
            if self.stream_server:
                self.stream_server.shutdown()
                self.stream_server.server_close()
                self.stream_server = None
                self.stream_thread = None
                self.sse_worker_thread = None
                
            self.lbl_stream_status.setText("Server: OFFLINE")
            self.lbl_stream_status.setStyleSheet("font-size: 11px; font-weight: bold; color: #d97706;")
            self.btn_stream_toggle.setText("🌐 Start Server")
            self.btn_stream_open.setEnabled(False)
            self.spin_port.setEnabled(True)
            
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic("INFO: Web stream server shut down successfully.")
        except Exception as e:
            print(f"Error stopping stream server: {str(e)}")

    def open_browser(self):
        try:
            webbrowser.open(f"http://localhost:{self.stream_port}")
        except Exception as e:
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: Cannot launch browser: {str(e)}")

    # ======================================================================
    # TIMERS & TELEMETRY ROUTING
    # ======================================================================
    def on_ui_timer_tick(self):
        # Recorder active blinker
        if self.is_recording:
            self.blink_state = not self.blink_state
            if self.blink_state:
                self.lbl_log_status.setText(f"Recorder: RECORDING 🔴 ({self.recorded_count} rows)")
            else:
                self.lbl_log_status.setText(f"Recorder: RECORDING    ({self.recorded_count} rows)")

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        """
        Receives calibrated telemetry packets to route them to the active CSV logger
        and broadcasts them dynamically to connected Web clients.
        """
        # 1. Write to CSV Log
        if self.is_recording and self.log_writer:
            t_curr = time.time() - self.start_time
            router = self.main_window.data_router
            
            row_data = [f"{t_curr:.3f}", subsystem_name]
            
            for sub_name, sub in router.subsystems.items():
                for v in sub.variables:
                    v_name = v["name"]
                    if sub_name == subsystem_name and v_name in data:
                        val = data[v_name]
                        try:
                            row_data.append(f"{float(val):.3f}" if v["is_numerical"] else str(val))
                        except:
                            row_data.append(str(val))
                    else:
                        row_data.append("")
                        
            try:
                self.log_writer.writerow(row_data)
                self.recorded_count += 1
                if self.recorded_count % 10 == 0:
                    self.log_file.flush()
            except Exception as e:
                if hasattr(self.main_window, "log_to_diagnostic"):
                    self.main_window.log_to_diagnostic(f"Telemetry CSV Write failure: {str(e)}")

        # 2. Broadcast to Web SSE clients
        if self.is_streaming and self.stream_server:
            # Build status flags (1: OVP, 2: OCP, 4: OTP, 8: Standby)
            flags = 0
            sub = self.main_window.data_router.subsystems.get(subsystem_name)
            if sub:
                if sub.alarm_states.get("ovp"): flags |= 1
                if sub.alarm_states.get("ocp"): flags |= 2
                if sub.alarm_states.get("otp"): flags |= 4
                if sub.alarm_states.get("standby"): flags |= 8

            # Unpack dictionary data variables directly in the top-level keys for web client parsing
            packet = {
                "device": subsystem_name,
                "timestamp": time.time() - self.main_window.data_router.start_time,
                "status_flags": flags,
                **data
            }
            # Put the packet in the thread-safe Queue instead of starting a new thread
            self.outgoing_queue.put(packet)

    def rebuild_ui(self):
        pass

    def rebuild_plots(self):
        pass
