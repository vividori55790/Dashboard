# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-06-01)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Refactored to support multi-instance custom widgets.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-06-01) - Antigravity: Extracted reusable ProtocolAnalyzerWidget to support multi-instance raw packet monitors.
# * v1.0.0 (2026-05-29) - Antigravity: Initial creation of high-fidelity visual Protocol Analyzer Plugin.
# ======================================================================

import time
from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, QTextEdit, QCheckBox
from PyQt6.QtCore import Qt, pyqtSlot
from PyQt6.QtGui import QTextCursor
from plugins.base_plugin import BasePlugin

class ProtocolAnalyzerWidget(QWidget):
    """
    Reusable Widget representing the high-tech Real-time Protocol Analyzer console.
    """
    def __init__(self, main_window, parent=None):
        super().__init__(parent)
        self.main_window = main_window
        self.is_paused = False
        self.auto_scroll = True
        self.max_lines = 100
        self.line_count = 0
        
        self.init_ui()

    def init_ui(self):
        container_style = """
            QWidget {
                background-color: #0b0c10;
                color: #c5c6c7;
                font-family: 'Segoe UI', Consolas, monospace;
            }
            QPushButton {
                background-color: #1f2833;
                color: #66fcf1;
                border: 1px solid #45f3ff;
                border-radius: 4px;
                padding: 6px 12px;
                font-weight: bold;
                font-size: 11px;
            }
            QPushButton:hover {
                background-color: #45f3ff;
                color: #0b0c10;
            }
            QPushButton:pressed {
                background-color: #66fcf1;
                color: #0b0c10;
            }
            QTextEdit {
                background-color: #020205;
                color: #00ffcc;
                border: 1px solid #1f2833;
                border-radius: 6px;
                font-family: 'Consolas', monospace;
                font-size: 12px;
            }
            QCheckBox {
                spacing: 6px;
                font-weight: bold;
                color: #8e94a6;
            }
            QCheckBox::indicator {
                width: 13px;
                height: 13px;
                border: 1px solid #1f2833;
                background-color: #020205;
                border-radius: 3px;
            }
            QCheckBox::indicator:checked {
                background-color: #45f3ff;
                border-color: #45f3ff;
            }
        """
        self.setStyleSheet(container_style)
        
        layout = QVBoxLayout(self)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(8)

        # Header Info
        header_lay = QHBoxLayout()
        title_lbl = QLabel("📡 Raw Serial Packet Stream Inspector")
        title_lbl.setStyleSheet("font-size: 12px; font-weight: bold; color: #45f3ff;")
        header_lay.addWidget(title_lbl)
        
        self.lbl_status = QLabel("Status: Active & Sniffing...")
        self.lbl_status.setStyleSheet("font-size: 10px; color: #8e94a6; font-style: italic;")
        header_lay.addStretch()
        header_lay.addWidget(self.lbl_status)
        layout.addLayout(header_lay)

        # Text Screen
        self.console_view = QTextEdit()
        self.console_view.setReadOnly(True)
        self.console_view.setPlaceholderText("No raw HEX/Byte streams captured yet.\n(Tip: Simulated node transmits packet starting with '$HEX,7E,...' for decoding demo)")
        layout.addWidget(self.console_view)

        # Controls Panel
        ctrl_lay = QHBoxLayout()
        
        btn_pause = QPushButton("⏸️ Pause Capture")
        btn_pause.setCheckable(True)
        btn_pause.clicked.connect(self.toggle_pause)
        
        btn_clear = QPushButton("🗑️ Clear Screen")
        btn_clear.clicked.connect(self.clear_console)
        
        self.chk_scroll = QCheckBox("Auto-Scroll")
        self.chk_scroll.setChecked(True)
        self.chk_scroll.stateChanged.connect(self.toggle_scroll)

        ctrl_lay.addWidget(btn_pause)
        ctrl_lay.addWidget(btn_clear)
        ctrl_lay.addWidget(self.chk_scroll)
        ctrl_lay.addStretch()
        
        # Color legend label
        legend = QLabel("Legend: <span style='color:#ff3366;'>[Header]</span> <span style='color:#38bdf8;'>[ID]</span> <span style='color:#eab308;'>[CMD]</span> <span style='color:#10b981;'>[Payload]</span> <span style='color:#a855f7;'>[Footer]</span>")
        legend.setStyleSheet("font-size: 10px; color: #c5c6c7;")
        ctrl_lay.addWidget(legend)
        
        layout.addLayout(ctrl_lay)
        
        # Connect main window data router telemetry event
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed_sniff)
        
        # Connect raw serial packet listener for dynamic hex parsing inside the widget
        if hasattr(self.main_window.serial_manager, "raw_packet_received"):
            self.main_window.serial_manager.raw_packet_received.connect(self.on_raw_packet_received)

    def toggle_pause(self, checked):
        self.is_paused = checked
        if self.is_paused:
            self.lbl_status.setText("Status: Capture Paused")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #ff3366;")
        else:
            self.lbl_status.setText("Status: Active & Sniffing...")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #8e94a6;")

    def clear_console(self):
        self.console_view.clear()
        self.line_count = 0

    def toggle_scroll(self, state):
        self.auto_scroll = bool(state)

    def parse_hex_line(self, raw_line: str) -> bool:
        line_str = raw_line.strip()
        if not line_str.startswith("$HEX,"):
            return False

        parts = line_str.split(',')
        if len(parts) < 6:
            return False

        try:
            hdr = parts[1].strip()
            dev_id = int(parts[2].strip(), 16)
            cmd = int(parts[3].strip(), 16)
            payload = int(parts[4].strip(), 16)
            ftr = parts[5].strip()
            
            if not self.is_paused:
                timestamp = time.strftime("%H:%M:%S")
                html = (
                    f"<font color='#555577'>[{timestamp}]</font> "
                    f"<b>Parsed Packet</b>: "
                    f"<font color='#ff3366'>[Hdr: {hdr}]</font>➔"
                    f"<font color='#38bdf8'>[ID: {dev_id:02d}]</font>➔"
                    f"<font color='#eab308'>[Cmd: 0x{cmd:02X}]</font>➔"
                    f"<font color='#10b981'>[Payload: {payload}]</font>➔"
                    f"<font color='#a855f7'>[Ftr: {ftr}]</font> "
                    f"<font color='#8e94a6'><i>(Custom Protocol Recognized)</i></font>"
                )
                self.append_html_line(html)
            return True
        except Exception as e:
            if not self.is_paused:
                self.append_html_line(f"<font color='#ffaa00'>[Warning]: Failed parsing HEX stream: {str(e)}</font>")
            return False

    @pyqtSlot(str)
    def on_raw_packet_received(self, raw_line):
        if self.is_paused:
            return
        self.parse_hex_line(raw_line)

    @pyqtSlot(str, dict)
    def on_telemetry_routed_sniff(self, subsystem_name, data):
        """
        Passive sniffer callback to display default CSV/JSON telemetry updates
        on the console when no custom HEX protocol is currently captured.
        """
        if self.is_paused:
            return
            
        # Ignore parsed HEX variables themselves to prevent infinite loops
        if "hex_sensor_value" in data:
            return

        timestamp = time.strftime("%H:%M:%S")
        snippet = ", ".join([f"{k}={v}" for k, v in list(data.items())[:3]])
        if len(data) > 3:
            snippet += "..."
            
        html = (
            f"<font color='#555577'>[{timestamp}]</font> "
            f"<font color='#8e94a6'>[Sniffer ➔ {subsystem_name}]:</font> "
            f"<font color='#00ff88'>{snippet}</font>"
        )
        self.append_html_line(html)

    def append_html_line(self, html_str):
        self.console_view.append(html_str)
        self.line_count += 1
        
        if self.line_count > self.max_lines:
            self.console_view.clear()
            self.console_view.append("<font color='#555577'>... [System: Cleared older logs to save memory] ...</font>")
            self.console_view.append(html_str)
            self.line_count = 2

        if self.auto_scroll:
            self.console_view.moveCursor(QTextCursor.MoveOperation.End)

    def closeEvent(self, event):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed_sniff)
        except:
            pass
        if hasattr(self.main_window.serial_manager, "raw_packet_received"):
            try:
                self.main_window.serial_manager.raw_packet_received.disconnect(self.on_raw_packet_received)
            except:
                pass
        super().closeEvent(event)


class ProtocolAnalyzerPlugin(BasePlugin):
    """
    Wrapper for dynamic plugin discovery compatibility.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "protocol_analyzer"
        self.name = "Protocol Analyzer Console"
        self.description = "Real-time raw packet HEX stream inspector and decodable visualizer."
        self.dock_widget = None

    def on_enable(self):
        self.dock_widget = QDockWidget("⚡ Real-Time Protocol Analyzer Console", self.main_window)
        self.dock_widget.setObjectName("ProtocolAnalyzerDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)

        # Instantiate reusable widget
        self.container = ProtocolAnalyzerWidget(self.main_window)
        self.dock_widget.setWidget(self.container)

    def parse_data(self, raw_line: str) -> dict | None:
        """
        Allow plugin manager dynamic binding of hex decoding to subsystem variables.
        """
        line_str = raw_line.strip()
        if not line_str.startswith("$HEX,"):
            return None

        parts = line_str.split(',')
        if len(parts) < 6:
            return None

        try:
            hdr = parts[1].strip()
            dev_id = int(parts[2].strip(), 16)
            cmd = int(parts[3].strip(), 16)
            payload = int(parts[4].strip(), 16)
            ftr = parts[5].strip()
            
            # Print on active plugin instance console if available
            if hasattr(self, "container") and self.container and not self.container.is_paused:
                timestamp = time.strftime("%H:%M:%S")
                html = (
                    f"<font color='#555577'>[{timestamp}]</font> "
                    f"<b>Parsed Packet</b>: "
                    f"<font color='#ff3366'>[Hdr: {hdr}]</font>➔"
                    f"<font color='#38bdf8'>[ID: {dev_id:02d}]</font>➔"
                    f"<font color='#eab308'>[Cmd: 0x{cmd:02X}]</font>➔"
                    f"<font color='#10b981'>[Payload: {payload}]</font>➔"
                    f"<font color='#a855f7'>[Ftr: {ftr}]</font> "
                    f"<font color='#8e94a6'><i>(Custom Protocol Intercepted)</i></font>"
                )
                self.container.append_html_line(html)
            
            return {
                "hex_header": hdr,
                "hex_device_id": float(dev_id),
                "hex_command": float(cmd),
                "hex_sensor_value": float(payload),
                "hex_footer": ftr
            }
        except:
            return None

    def on_disable(self):
        if self.dock_widget:
            self.main_window.removeDockWidget(self.dock_widget)
            self.dock_widget.deleteLater()
            self.dock_widget = None
        super().on_disable()
