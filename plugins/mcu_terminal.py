# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular MCU Terminal plugin.
# ======================================================================

import time
from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QHBoxLayout, QGroupBox, QTextEdit, QLineEdit, QPushButton, QComboBox, QListView, QLabel
from PyQt6.QtCore import pyqtSlot, Qt
from PyQt6.QtGui import QFont
from plugins.base_plugin import BasePlugin

class McuTerminalPlugin(BasePlugin):
    """
    Implements a multi-port serial packet logger and manual command injector.
    Provides diagnostic logs and raw feeds.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "mcu_terminal"
        self.name = "MCU Terminal"
        self.description = "Manages VCP serial console monitoring and direct packet injection."

    def on_enable(self):
        self.dock_widget = QDockWidget("📺 Hardware VCP Terminal & Diagnostics", self.main_window)
        self.dock_widget.setObjectName("dock_terminal_logs")
        
        container = QWidget()
        main_lay = QVBoxLayout(container)
        main_lay.setContentsMargins(6, 6, 6, 6)
        main_lay.setSpacing(4)
        
        term_lay = QHBoxLayout()
        term_lay.setSpacing(10)
        
        # 1. Raw Packet Console
        raw_box = QGroupBox("MCU USB CDC VCP Live Raw Packet Terminal")
        raw_v_lay = QVBoxLayout(raw_box)
        raw_v_lay.setContentsMargins(6, 12, 6, 6)
        self.console_raw = QTextEdit()
        self.console_raw.setReadOnly(True)
        self.console_raw.setFont(QFont("Consolas", 8))
        self.console_raw.setStyleSheet("background-color: #050508; color: #00ff66; border: 1px solid #113311;")
        raw_v_lay.addWidget(self.console_raw)
        
        # 2. Diagnostic Console
        err_box = QGroupBox("System Diagnostic Logs")
        err_v_lay = QVBoxLayout(err_box)
        err_v_lay.setContentsMargins(6, 12, 6, 6)
        self.console_err = QTextEdit()
        self.console_err.setReadOnly(True)
        self.console_err.setFont(QFont("Consolas", 8))
        self.console_err.setStyleSheet("background-color: #050508; color: #ff5252; border: 1px solid #331111;")
        err_v_lay.addWidget(self.console_err)
        
        term_lay.addWidget(raw_box, 1)
        term_lay.addWidget(err_box, 1)
        main_lay.addLayout(term_lay, 1)
        
        # 3. Command Injector Bar
        input_bar = QWidget()
        input_bar_lay = QHBoxLayout(input_bar)
        input_bar_lay.setContentsMargins(0, 4, 0, 0)
        input_bar_lay.setSpacing(6)
        
        input_bar_lay.addWidget(QLabel("Target:"))
        self.combo_target_port = QComboBox()
        self.combo_target_port.setView(QListView())
        self.combo_target_port.setFixedWidth(120)
        input_bar_lay.addWidget(self.combo_target_port)
        
        self.txt_manual_cmd = QLineEdit()
        self.txt_manual_cmd.setPlaceholderText("Enter command here (e.g. $CMD,RUN) ...")
        self.txt_manual_cmd.setStyleSheet("background-color: #050508; color: #ffffff; border: 1px solid #282836;")
        self.txt_manual_cmd.returnPressed.connect(self.send_manual_command)
        input_bar_lay.addWidget(self.txt_manual_cmd)
        
        self.btn_send_cmd = QPushButton("✉️ Transmit")
        self.btn_send_cmd.setFixedWidth(100)
        self.btn_send_cmd.clicked.connect(self.send_manual_command)
        input_bar_lay.addWidget(self.btn_send_cmd)
        
        main_lay.addWidget(input_bar)
        self.container = container
        self.dock_widget.setWidget(container)
        
        # Connect Manager Signals
        sm = self.main_window.serial_manager
        sm.raw_packet_received.connect(self.on_raw_packet_received)
        sm.error_occurred.connect(self.on_error_occurred)
        sm.connection_status_changed.connect(self.on_connection_status_changed)
        
        self.refresh_ports_dropdown()
        self.log_to_diagnostic("MCU Debug Terminal Plugin initialized successfully.")

    def on_disable(self):
        try:
            sm = self.main_window.serial_manager
            sm.raw_packet_received.disconnect(self.on_raw_packet_received)
            sm.error_occurred.disconnect(self.on_error_occurred)
            sm.connection_status_changed.disconnect(self.on_connection_status_changed)
        except:
            pass
        if hasattr(self, "container") and self.container:
            self.container.deleteLater()
            self.container = None
        super().on_disable()

    def log_to_diagnostic(self, text):
        self.console_err.append(f"[{time.strftime('%H:%M:%S')}] {text}")

    def refresh_ports_dropdown(self):
        """
        Updates the manual command target ports dropdown list.
        """
        self.combo_target_port.clear()
        self.combo_target_port.addItem("Broadcast All", "BROADCAST")
        
        sm = self.main_window.serial_manager
        for port in sm.active_threads.keys():
            self.combo_target_port.addItem(port, port)

    @pyqtSlot(str, str)
    def on_raw_packet_received(self, port_name, text):
        """
        Appends raw received strings, colored/labeled by source port.
        """
        self.console_raw.append(f"[{port_name}] {text}")
        
        # Constrain lines to prevent memory lag
        doc = self.console_raw.document()
        if doc.blockCount() > 150:
            cursor = self.console_raw.textCursor()
            cursor.movePosition(cursor.MoveOperation.Start)
            cursor.select(cursor.SelectionType.BlockUnderCursor)
            cursor.removeSelectedText()
            cursor.deleteChar()

    @pyqtSlot(str, str)
    def on_error_occurred(self, port_name, err_msg):
        self.console_err.append(f"[{time.strftime('%H:%M:%S')}] [{port_name}] ERROR: {err_msg}")

    @pyqtSlot(str, bool, str)
    def on_connection_status_changed(self, port_name, connected, msg):
        self.log_to_diagnostic(f"[{port_name}] status: {msg}")
        self.refresh_ports_dropdown()

    def send_manual_command(self):
        """
        Sends the text command to the selected target port.
        """
        cmd = self.txt_manual_cmd.text().strip()
        if not cmd:
            return
            
        target = self.combo_target_port.currentData()
        sm = self.main_window.serial_manager
        
        if not cmd.endswith('\n'):
            cmd += '\n'
            
        if target == "BROADCAST":
            sm.broadcast_command(cmd)
            self.log_to_diagnostic(f"Broadcasted command: {cmd.strip()}")
        else:
            sm.send_command(target, cmd)
            self.log_to_diagnostic(f"Sent command to {target}: {cmd.strip()}")
            
        self.txt_manual_cmd.clear()
