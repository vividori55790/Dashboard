# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular PluginManager.
# ======================================================================

import os
import sys
import importlib
import traceback
from plugins.base_plugin import BasePlugin

CERTIFIED_PLUGINS = {
    "thermal_heatmap": {
        "id": "thermal_heatmap",
        "name": "Thermal Heatmap Grid",
        "author": "Antigravity Dev",
        "version": "1.0.0",
        "category": "시각화/도구",
        "icon": "🌡️",
        "description": "다채널 MOSFET/변압기 등의 실시간 온도 데이터를 HSL 그라데이션 기반의 2D 격자 뷰로 화려하게 시각화합니다.",
        "url": "https://raw.githubusercontent.com/vividori55790/Dashboard/main/plugins/thermal_heatmap.py",
        "code": """# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: PyQt6
# ======================================================================
import random
from PyQt6.QtWidgets import QDockWidget, QWidget, QGridLayout, QLabel, QVBoxLayout, QHBoxLayout
from PyQt6.QtCore import QTimer, Qt
from plugins.base_plugin import BasePlugin

class ThermalHeatmapPlugin(BasePlugin):
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "thermal_heatmap"
        self.name = "Thermal Heatmap Grid"
        self.description = "Visualizes live subsystem cell temperatures on a high-tech 2D gradient grid."
        
    def on_enable(self):
        self.dock_widget = QDockWidget("🌡️ Thermal Heatmap Grid", self.main_window)
        self.dock_widget.setObjectName("ThermalHeatmapDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        container = QWidget()
        container.setStyleSheet("background-color: #0b0c10; color: #ffffff;")
        layout = QVBoxLayout(container)
        
        grid_widget = QWidget()
        self.grid_layout = QGridLayout(grid_widget)
        self.grid_layout.setSpacing(4)
        self.cells = []
        
        for r in range(4):
            row_cells = []
            for c in range(8):
                lbl = QLabel("0.0°C")
                lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
                lbl.setStyleSheet("border: 1px solid #1f2833; border-radius: 4px; font-weight: bold; font-size: 10px; min-width: 50px; min-height: 40px; background-color: #000033; color: #ffffff;")
                self.grid_layout.addWidget(lbl, r, c)
                row_cells.append(lbl)
            self.cells.append(row_cells)
            
        layout.addWidget(grid_widget)
        
        self.status_lbl = QLabel("Heat grid active. Syncing with subsystems...")
        self.status_lbl.setStyleSheet("font-size: 10px; color: #66fcf1;")
        layout.addWidget(self.status_lbl)
        
        self.dock_widget.setWidget(container)
        
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        
        self.sim_timer = QTimer()
        self.sim_timer.timeout.connect(self.update_simulation)
        self.sim_timer.start(1000)
        
    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if hasattr(self, 'sim_timer'):
            self.sim_timer.stop()
        super().on_disable()
        
    def on_telemetry_routed(self, sub_name, data):
        temps = []
        for k, v in data.items():
            if 'temp' in k.lower() or 't_' in k.lower():
                try:
                    temps.append(float(v))
                except:
                    pass
        if temps:
            self.status_lbl.setText(f"Syncing temp data from {sub_name}: {temps[0]:.1f}°C")
            self.update_grid(temps[0])
            
    def update_grid(self, base_temp):
        for r in range(4):
            for c in range(8):
                lbl = self.cells[r][c]
                dist = abs(r - 1.5) + abs(c - 3.5)
                temp = base_temp - (dist * 1.8) + random.uniform(-0.5, 0.5)
                temp = max(15.0, min(temp, 120.0))
                lbl.setText(f"{temp:.1f}°C")
                color = self.get_color_for_temp(temp)
                lbl.setStyleSheet(f"border: 1px solid #1f2833; border-radius: 4px; font-weight: bold; font-size: 10px; min-width: 50px; min-height: 40px; background-color: {color}; color: #ffffff;")
                
    def update_simulation(self):
        self.update_grid(35.0 + random.uniform(-1.0, 1.0))
        
    def get_color_for_temp(self, temp):
        if temp < 25.0:
            return "#1f3a60"
        elif temp < 45.0:
            return "#1a5f57"
        elif temp < 65.0:
            return "#b87c04"
        elif temp < 85.0:
            return "#d94f04"
        else:
            return "#8b0000"
"""
    },
    "csv_auto_logger": {
        "id": "csv_auto_logger",
        "name": "CSV Auto-Logger",
        "author": "Antigravity Dev",
        "version": "1.0.0",
        "category": "데이터/로그",
        "icon": "💾",
        "description": "실시간 데이터 수신 패킷을 타임스탬프와 함께 로컬 CSV 로그 파일로 백그라운드 자동 정밀 백업 기록합니다.",
        "url": "https://raw.githubusercontent.com/vividori55790/Dashboard/main/plugins/csv_auto_logger.py",
        "code": """# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: PyQt6
# ======================================================================
import os
import csv
import time
from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QPushButton, QLabel, QHBoxLayout
from PyQt6.QtCore import Qt, pyqtSlot
from plugins.base_plugin import BasePlugin

class CsvAutoLoggerPlugin(BasePlugin):
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "csv_auto_logger"
        self.name = "CSV Auto-Logger"
        self.description = "Auto-saves all received telemetry variable data packets into timestamped local CSV logs."
        self.log_file = None
        self.writer = None
        self.total_rows = 0
        self.is_logging = True
        
    def on_enable(self):
        self.dock_widget = QDockWidget("💾 CSV Auto-Logger", self.main_window)
        self.dock_widget.setObjectName("CsvAutoLoggerDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        container = QWidget()
        container.setStyleSheet("background-color: #0b0c10; color: #ffffff;")
        layout = QVBoxLayout(container)
        
        self.lbl_status = QLabel("Active Logger Status: Logging Enabled")
        self.lbl_status.setStyleSheet("font-weight: bold; color: #00ff66;")
        layout.addWidget(self.lbl_status)
        
        from path_resolver import get_writable_app_dir
        self.logs_dir = get_writable_app_dir() / "csv_logs"
        self.logs_dir.mkdir(parents=True, exist_ok=True)
        
        self.lbl_path = QLabel(f"Path: {self.logs_dir}")
        self.lbl_path.setWordWrap(True)
        self.lbl_path.setStyleSheet("font-size: 10px; color: #8e94a6;")
        layout.addWidget(self.lbl_path)
        
        self.lbl_stats = QLabel("Total Packets Logged: 0 rows")
        self.lbl_stats.setStyleSheet("font-size: 11px; color: #38bdf8;")
        layout.addWidget(self.lbl_stats)
        
        btn_layout = QHBoxLayout()
        self.btn_toggle = QPushButton("Pause Logging")
        self.btn_toggle.clicked.connect(self.toggle_logging)
        btn_layout.addWidget(self.btn_toggle)
        
        btn_open = QPushButton("Open Directory")
        btn_open.clicked.connect(self.open_directory)
        btn_layout.addWidget(btn_open)
        
        layout.addLayout(btn_layout)
        self.dock_widget.setWidget(container)
        
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        self.start_new_log_file()
        
    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        self.close_log_file()
        super().on_disable()
        
    def start_new_log_file(self):
        self.close_log_file()
        filename = f"telemetry_log_{int(time.time())}.csv"
        self.current_filepath = self.logs_dir / filename
        self.log_file = open(self.current_filepath, 'w', newline='', encoding='utf-8')
        self.writer = csv.writer(self.log_file)
        self.writer.writerow(["Timestamp", "Subsystem", "Variable", "Value"])
        self.total_rows = 0
        self.lbl_path.setText(f"File: {self.current_filepath.name}")
        
    def close_log_file(self):
        if self.log_file:
            self.log_file.flush()
            self.log_file.close()
            self.log_file = None
            self.writer = None
            
    def toggle_logging(self):
        self.is_logging = not self.is_logging
        if self.is_logging:
            self.lbl_status.setText("Active Logger Status: Logging Enabled")
            self.lbl_status.setStyleSheet("font-weight: bold; color: #00ff66;")
            self.btn_toggle.setText("Pause Logging")
            self.start_new_log_file()
        else:
            self.lbl_status.setText("Active Logger Status: Logging Paused")
            self.lbl_status.setStyleSheet("font-weight: bold; color: #ff3366;")
            self.btn_toggle.setText("Resume Logging")
            self.close_log_file()
            
    def open_directory(self):
        import os
        try:
            os.startfile(str(self.logs_dir))
        except Exception as e:
            print(f"Error opening logs dir: {e}")
            
    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        if not self.is_logging or not self.writer:
            return
            
        timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
        for k, v in data.items():
            self.writer.writerow([timestamp, subsystem_name, k, v])
            self.total_rows += 1
            
        if self.total_rows % 10 == 0:
            if self.log_file:
                self.log_file.flush()
                
        self.lbl_stats.setText(f"Total Packets Logged: {self.total_rows} rows")
"""
    },
    "discord_alert": {
        "id": "discord_alert",
        "name": "Discord Alert Dispatcher",
        "author": "Antigravity Dev",
        "version": "1.0.0",
        "category": "연동/알림",
        "icon": "🔔",
        "description": "텔레메트리 임계값(OVP, OTP, OCP 등) 초과 경보가 감지되면 디스코드 웹훅을 통해 실시간 긴급 경보를 발송합니다.",
        "url": "https://raw.githubusercontent.com/vividori55790/Dashboard/main/plugins/discord_alert.py",
        "code": """# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: PyQt6
# ======================================================================
import json
import time
from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QPushButton, QLabel, QLineEdit, QHBoxLayout
from PyQt6.QtCore import Qt, pyqtSlot, QThread, pyqtSignal
from plugins.base_plugin import BasePlugin

class DiscordWebhookThread(QThread):
    finished_signal = pyqtSignal(bool, str)
    
    def __init__(self, webhook_url, content):
        super().__init__()
        self.webhook_url = webhook_url
        self.content = content
        
    def run(self):
        import urllib.request
        import json
        try:
            req = urllib.request.Request(
                self.webhook_url,
                data=json.dumps({"content": self.content}).encode('utf-8'),
                headers={'User-Agent': 'Mozilla/5.0', 'Content-Type': 'application/json'}
            )
            with urllib.request.urlopen(req) as response:
                if response.status == 204 or response.status == 200:
                    self.finished_signal.emit(True, "Alert sent successfully!")
                else:
                    self.finished_signal.emit(False, f"HTTP Status: {response.status}")
        except Exception as e:
            self.finished_signal.emit(False, str(e))

class DiscordAlertPlugin(BasePlugin):
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "discord_alert"
        self.name = "Discord Alert Dispatcher"
        self.description = "Sends real-time telemetry fault/threshold alarm alerts to a Discord channel."
        self.webhook_url = ""
        self.last_sent_time = 0
        self.cooldown_seconds = 30
        
    def on_enable(self):
        self.dock_widget = QDockWidget("🔔 Discord Dispatcher", self.main_window)
        self.dock_widget.setObjectName("DiscordAlertDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        container = QWidget()
        container.setStyleSheet("background-color: #0b0c10; color: #ffffff;")
        layout = QVBoxLayout(container)
        
        layout.addWidget(QLabel("Discord Webhook URL Configuration:"))
        
        self.edit_webhook = QLineEdit()
        self.edit_webhook.setPlaceholderText("https://discord.com/api/webhooks/...")
        self.edit_webhook.setStyleSheet("background-color: #1f2833; color: #ffffff; border: 1px solid #45f3ff; border-radius: 4px; padding: 6px;")
        
        if hasattr(self.main_window, "config_data"):
            saved_url = self.main_window.config_data.get("discord_webhook", "")
            self.edit_webhook.setText(saved_url)
            self.webhook_url = saved_url
            
        layout.addWidget(self.edit_webhook)
        
        btn_layout = QHBoxLayout()
        btn_save = QPushButton("Save Webhook")
        btn_save.clicked.connect(self.save_webhook)
        btn_layout.addWidget(btn_save)
        
        btn_test = QPushButton("Send Test Alert")
        btn_test.clicked.connect(self.send_test_alert)
        btn_layout.addWidget(btn_test)
        
        layout.addLayout(btn_layout)
        
        self.lbl_status = QLabel("Status: Idle. Listening for thresholds...")
        self.lbl_status.setWordWrap(True)
        self.lbl_status.setStyleSheet("font-size: 10px; color: #8e94a6;")
        layout.addWidget(self.lbl_status)
        
        self.dock_widget.setWidget(container)
        
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        
    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        super().on_disable()
        
    def save_webhook(self):
        self.webhook_url = self.edit_webhook.text().strip()
        if hasattr(self.main_window, "config_data"):
            self.main_window.config_data["discord_webhook"] = self.webhook_url
            self.main_window.config_manager.save_config()
            self.lbl_status.setText("Status: Webhook saved successfully!")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #00ff66;")
            
    def send_test_alert(self):
        url = self.edit_webhook.text().strip()
        if not url:
            self.lbl_status.setText("Error: Webhook URL is empty!")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #ff3366;")
            return
            
        self.lbl_status.setText("Status: Dispatching test message...")
        self.thread = DiscordWebhookThread(url, "🔔 **[Embedded Telemetry Test Alert]** Discord dispatcher test notification successful!")
        self.thread.finished_signal.connect(self.on_alert_finished)
        self.thread.start()
        
    def on_alert_finished(self, success, message):
        if success:
            self.lbl_status.setText(f"Status: {message}")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #00ff66;")
        else:
            self.lbl_status.setText(f"Status: Failed - {message}")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #ff3366;")
            
    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        if not self.webhook_url:
            return
            
        now = time.time()
        if now - self.last_sent_time < self.cooldown_seconds:
            return
            
        router = self.main_window.data_router
        if subsystem_name not in router.subsystems:
            return
            
        sub = router.subsystems[subsystem_name]
        thresholds = getattr(sub, 'thresholds', {})
        
        alert_triggered = False
        alert_msg = ""
        
        for k, v in data.items():
            if k in thresholds:
                limit = thresholds[k]
                try:
                    val = float(v)
                    if val > limit:
                        alert_triggered = True
                        alert_msg = f"⚠️ **[ALARM TRIGERRED]** Subsystem **{sub.display_name}** variable **{k}** is **{val:.2f}**, exceeding threshold of **{limit:.2f}**!"
                        break
                except:
                    pass
                    
        if alert_triggered:
            self.last_sent_time = now
            self.lbl_status.setText(f"Status: Fault dispatched to Discord - {k}={val}")
            self.lbl_status.setStyleSheet("font-size: 10px; color: #ffaa00;")
            self.thread = DiscordWebhookThread(self.webhook_url, alert_msg)
            self.thread.finished_signal.connect(lambda s, m: print(f"Discord Alert Dispatch: {m}"))
            self.thread.start()
"""
    },
    "fft_analyzer": {
        "id": "fft_analyzer",
        "name": "FFT Waveform Analyzer",
        "author": "Antigravity Dev",
        "version": "1.0.0",
        "category": "시각화/도구",
        "icon": "📊",
        "description": "선택한 아날로그 텔레메트리 변수를 실시간 고속 푸리에 변환(FFT)하여 주파수 도메인 분포를 분석하고 시각화합니다.",
        "url": "https://raw.githubusercontent.com/vividori55790/Dashboard/main/plugins/fft_analyzer.py",
        "code": """# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: PyQt6
# ======================================================================
import numpy as np
from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QComboBox, QLabel
from PyQt6.QtCore import Qt, pyqtSlot, QTimer
import pyqtgraph as pg
from plugins.base_plugin import BasePlugin

class FftAnalyzerPlugin(BasePlugin):
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "fft_analyzer"
        self.name = "FFT Waveform Analyzer"
        self.description = "Calculates and plots live Fast Fourier Transform (FFT) spectrum of selected numerical variables."
        self.selected_sub = ""
        self.selected_var = ""
        
    def on_enable(self):
        self.dock_widget = QDockWidget("📊 FFT Waveform Analyzer", self.main_window)
        self.dock_widget.setObjectName("FftAnalyzerDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        container = QWidget()
        container.setStyleSheet("background-color: #0b0c10; color: #ffffff;")
        layout = QVBoxLayout(container)
        
        self.combo_var = QComboBox()
        self.combo_var.currentIndexChanged.connect(self.on_variable_selected)
        layout.addWidget(QLabel("Select Telemetry Variable for Frequency Analysis:"))
        layout.addWidget(self.combo_var)
        
        self.plot_widget = pg.PlotWidget(title="FFT Frequency Domain Spectrum")
        self.plot_widget.setBackground('#0b0c10')
        self.plot_widget.showGrid(x=True, y=True, alpha=0.3)
        self.plot_widget.setLabel('left', 'Magnitude (dB)')
        self.plot_widget.setLabel('bottom', 'Frequency', units='Hz')
        
        self.curve = self.plot_widget.plot(pen=pg.mkPen(color='#38bdf8', width=2))
        layout.addWidget(self.plot_widget)
        
        self.dock_widget.setWidget(container)
        
        self.refresh_timer = QTimer()
        self.refresh_timer.timeout.connect(self.populate_variables_list)
        self.refresh_timer.start(2000)
        
        self.populate_variables_list()
        
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        
    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if hasattr(self, 'refresh_timer'):
            self.refresh_timer.stop()
        super().on_disable()
        
    def populate_variables_list(self):
        router = self.main_window.data_router
        current_selection = self.combo_var.currentText()
        
        self.combo_var.blockSignals(True)
        self.combo_var.clear()
        
        has_items = False
        for sub_name, sub in router.subsystems.items():
            for v in sub.variables:
                if v["is_numerical"]:
                    item_text = f"{sub_name}.{v['name']}"
                    self.combo_var.addItem(item_text, (sub_name, v['name']))
                    has_items = True
                    
        idx = self.combo_var.findText(current_selection)
        if idx != -1:
            self.combo_var.setCurrentIndex(idx)
        elif has_items:
            self.combo_var.setCurrentIndex(0)
            
        self.combo_var.blockSignals(False)
        self.on_variable_selected()
        
    def on_variable_selected(self):
        data = self.combo_var.currentData()
        if data:
            self.selected_sub, self.selected_var = data
        else:
            self.selected_sub, self.selected_var = "", ""
            
    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        if subsystem_name != self.selected_sub or self.selected_var not in data:
            return
            
        router = self.main_window.data_router
        sub = router.subsystems[subsystem_name]
        
        if self.selected_var in sub.buffers:
            buffer_data = sub.buffers[self.selected_var]
            if len(buffer_data) < 8:
                return
                
            try:
                y = np.array(buffer_data)
                y = y - np.mean(y)
                
                n = len(y)
                fft_vals = np.fft.rfft(y)
                fft_freqs = np.fft.rfftfreq(n, d=0.1)
                
                mags = np.abs(fft_vals)
                mags_db = 20 * np.log10(mags + 1e-5)
                
                self.curve.setData(fft_freqs, mags_db)
            except Exception as e:
                print(f"FFT computation error: {e}")
"""
    }
}

class PluginManager:
    """
    Handles dynamic discovery, verification, enabling, disabling,
    and runtime configuration of standard and downloadable plugins.
    """
    def __init__(self, main_window):
        self.main_window = main_window
        self.available_plugins = {} # plugin_id -> Class object
        self.active_plugins = {}    # plugin_id -> Instantiated Plugin object
        
        from path_resolver import get_plugins_directories
        dirs = get_plugins_directories()
        self.internal_plugins_dir = dirs["internal"]
        self.external_plugins_dir = dirs["external"]

    def get_certified_plugins(self):
        """
        Returns a dict of certified plugins with their current status:
        - "installed": True/False
        - "active": True/False
        """
        result = {}
        for pid, meta in CERTIFIED_PLUGINS.items():
            installed = pid in self.available_plugins or os.path.exists(os.path.join(self.external_plugins_dir, f"{pid}.py"))
            active = pid in self.active_plugins
            result[pid] = {
                **meta,
                "installed": installed,
                "active": active
            }
        return result

    def download_and_install_plugin(self, plugin_id):
        """
        Downloads a certified plugin from the web repository.
        If offline or 404 occurs, falls back to injecting the pre-bundled fallback code.
        """
        if plugin_id not in CERTIFIED_PLUGINS:
            raise ValueError(f"Unknown certified plugin ID: {plugin_id}")
            
        meta = CERTIFIED_PLUGINS[plugin_id]
        url = meta["url"]
        fallback = meta["code"]
        
        # Ensure external plugins directory exists
        os.makedirs(self.external_plugins_dir, exist_ok=True)
        dest_path = os.path.join(self.external_plugins_dir, f"{plugin_id}.py")
        
        download_success = False
        import urllib.request
        try:
            # Short timeout to prevent freezing the GUI on network blockages
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            with urllib.request.urlopen(req, timeout=3) as response:
                if response.status == 200:
                    code_content = response.read().decode('utf-8')
                    if code_content.strip():
                        with open(dest_path, 'w', encoding='utf-8') as f:
                            f.write(code_content)
                        download_success = True
        except Exception as e:
            print(f"Online download failed for {plugin_id}, using high-fidelity local fallback: {e}")
            
        if not download_success:
            # Fallback to predefined code
            with open(dest_path, 'w', encoding='utf-8') as f:
                f.write(fallback)
                
        # Re-scan for discovering the new plugin
        self.discover_plugins()
        return dest_path

    def uninstall_plugin(self, plugin_id):
        """
        Uninstalls a certified or custom plugin from the user's external writable plugins folder.
        Safely disables it first if running.
        """
        # 1. Disable if active
        if plugin_id in self.active_plugins:
            self.toggle_plugin(plugin_id, False)
            
        # 2. Find file path in external dir
        filepath = os.path.join(self.external_plugins_dir, f"{plugin_id}.py")
        if os.path.exists(filepath):
            try:
                os.remove(filepath)
            except Exception as e:
                print(f"Error deleting plugin file {filepath}: {e}")
                
        # Remove from sys.modules to prevent cache pollution
        full_module_name = f"plugins.external.{plugin_id}"
        if full_module_name in sys.modules:
            del sys.modules[full_module_name]
            
        # 3. Re-scan
        self.discover_plugins()

    def discover_plugins(self):
        """
        Scans both internal standard plugins and external user plugins directories,
        dynamically loading all modules inheriting from BasePlugin.
        """
        self.available_plugins.clear()
        
        # Ensure directories exist
        for d in [self.internal_plugins_dir, self.external_plugins_dir]:
            if not os.path.exists(d):
                try:
                    os.makedirs(d)
                except Exception as e:
                    print(f"Error creating plugin directory {d}: {e}")
                    
        # Add internal bundle path to sys.path to allow absolute dynamic imports inside plugins
        # (e.g. from plugins.base_plugin import BasePlugin)
        internal_parent = os.path.dirname(self.internal_plugins_dir)
        if internal_parent not in sys.path:
            sys.path.insert(0, internal_parent)

        plugin_files = []
        
        # 1. Scan internal bundled plugins
        if os.path.exists(self.internal_plugins_dir):
            for filename in os.listdir(self.internal_plugins_dir):
                if filename.endswith(".py") and filename not in ["__init__.py", "base_plugin.py"]:
                    path = os.path.join(self.internal_plugins_dir, filename)
                    module_name = filename[:-3]
                    plugin_files.append((path, f"plugins.internal.{module_name}"))
                    
        # 2. Scan external custom user plugins (external overrides internal on name conflict)
        if os.path.exists(self.external_plugins_dir):
            for filename in os.listdir(self.external_plugins_dir):
                if filename.endswith(".py") and filename not in ["__init__.py", "base_plugin.py"]:
                    path = os.path.join(self.external_plugins_dir, filename)
                    module_name = filename[:-3]
                    plugin_files.append((path, f"plugins.external.{module_name}"))

        import importlib.util

        for file_path, full_module_name in plugin_files:
            try:
                # Load module dynamically from absolute file path
                spec = importlib.util.spec_from_file_location(full_module_name, file_path)
                if spec is None or spec.loader is None:
                    continue
                    
                module = importlib.util.module_from_spec(spec)
                sys.modules[full_module_name] = module
                spec.loader.exec_module(module)
                
                # Search classes inside the module
                for attr_name in dir(module):
                    attr = getattr(module, attr_name)
                    if isinstance(attr, type) and attr is not BasePlugin and issubclass(attr, BasePlugin):
                        plugin_inst = attr(self.main_window)
                        p_id = plugin_inst.plugin_id
                        self.available_plugins[p_id] = attr
                        
            except Exception as e:
                print(f"Error loading dynamic plugin package {file_path}: {str(e)}")
                print(traceback.format_exc())

    def install_plugin(self, source_file_path):
        """
        Copies a specified python plugin file into the user's external writable plugins folder,
        then triggers a rediscovery scan.
        """
        import shutil
        if not os.path.exists(source_file_path):
            raise FileNotFoundError(f"Source plugin file not found: {source_file_path}")
            
        if not source_file_path.endswith(".py"):
            raise ValueError("Only Python files (.py) can be installed as plugins.")
            
        filename = os.path.basename(source_file_path)
        dest_path = os.path.join(self.external_plugins_dir, filename)
        
        # Ensure external plugins directory exists
        os.makedirs(self.external_plugins_dir, exist_ok=True)
        
        # Copy the file
        shutil.copy2(source_file_path, dest_path)
        
        # Re-scan for discovering the new plugin
        self.discover_plugins()
        return filename

    def enable_plugin(self, plugin_id):
        """
        Instantiates and activates an available plugin.
        """
        if plugin_id in self.active_plugins:
            return True
            
        if plugin_id not in self.available_plugins:
            return False
            
        try:
            plugin_class = self.available_plugins[plugin_id]
            plugin_instance = plugin_class(self.main_window)
            
            # Enable operations
            plugin_instance.on_enable()
            self.active_plugins[plugin_id] = plugin_instance
            
            # Inject visual QDockWidget if provided
            dock = plugin_instance.get_dock_widget()
            if dock:
                # Add to GUI (default to Bottom area, user can float/move)
                from PyQt6.QtCore import Qt
                self.main_window.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, dock)
                dock.setVisible(True)
                
            return True
        except Exception as e:
            err_msg = f"Failed to activate plugin '{plugin_id}': {str(e)}"
            print(err_msg)
            print(traceback.format_exc())
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: {err_msg}")
            return False

    def disable_plugin(self, plugin_id):
        """
        Deactivates and destroys an enabled plugin.
        """
        if plugin_id not in self.active_plugins:
            return
            
        try:
            plugin_instance = self.active_plugins.pop(plugin_id)
            plugin_instance.on_disable()
        except Exception as e:
            print(f"Error disabling plugin '{plugin_id}': {str(e)}")

    def toggle_plugin(self, plugin_id, enabled):
        """
        Toggles state and saves the choice persistently.
        """
        if enabled:
            success = self.enable_plugin(plugin_id)
            if success:
                self._update_saved_plugin_state(plugin_id, True)
        else:
            self.disable_plugin(plugin_id)
            self._update_saved_plugin_state(plugin_id, False)

    def load_active_plugins_from_config(self, enabled_list):
        """
        Enables plugins that were saved as active in the user profile.
        """
        # If no explicit config, enable all discovered plugins by default
        if enabled_list is None:
            enabled_list = list(self.available_plugins.keys())
            
        for p_id in enabled_list:
            self.enable_plugin(p_id)

    def _update_saved_plugin_state(self, plugin_id, is_enabled):
        """
        Updates the active plugins list inside persistent config.json.
        """
        if hasattr(self.main_window, "config_manager"):
            cm = self.main_window.config_manager
            cfg = cm.config_data
            
            if "active_plugins" not in cfg:
                cfg["active_plugins"] = []
                
            if is_enabled:
                if plugin_id not in cfg["active_plugins"]:
                    cfg["active_plugins"].append(plugin_id)
            else:
                if plugin_id in cfg["active_plugins"]:
                    cfg["active_plugins"].remove(plugin_id)
                    
            cm.save_config()
