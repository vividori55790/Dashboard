# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-06-01)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Dynamically instantiates and mounts reusable telemetry widgets inside separate dock windows.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-06-01) - Antigravity: Added custom DockTitleBar, floating snap-back, size limits, and beginner quick-start presets.
# * v1.0.0 (2026-06-01) - Antigravity: Initial creation of the configurable dynamic dock container.
# ======================================================================

import uuid
from PyQt6.QtWidgets import (
    QDockWidget, QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
    QComboBox, QPushButton, QScrollArea, QCheckBox, QFrame
)
from PyQt6.QtCore import Qt, pyqtSlot

class DockTitleBar(QWidget):
    """
    Premium custom dark-themed title bar for QDockWidget.
    Provides one-click snap-back (docking) capability and elegant styling.
    """
    def __init__(self, dock_widget):
        super().__init__(dock_widget)
        self.dock_widget = dock_widget
        self.init_ui()
        self.apply_theme_styling()

    def init_ui(self):
        layout = QHBoxLayout(self)
        layout.setContentsMargins(8, 4, 8, 4)
        layout.setSpacing(6)

        self.lbl_title = QLabel(self.dock_widget.windowTitle())
        layout.addWidget(self.lbl_title)
        layout.addStretch()

        # 1. Dock snap-back button (highly prominent, visible when floating)
        self.btn_dock = QPushButton("📌 화면에 도킹")
        self.btn_dock.setObjectName("btn_dock")
        self.btn_dock.clicked.connect(self.dock_back)
        self.btn_dock.setVisible(self.dock_widget.isFloating())
        layout.addWidget(self.btn_dock)

        # 2. Config button
        self.btn_config = QPushButton("⚙️ 설정 변경")
        self.btn_config.clicked.connect(self.dock_widget.show_config_panel)
        self.btn_config.setVisible(self.dock_widget.widget_type is not None)
        layout.addWidget(self.btn_config)

        # 3. Float toggle button
        self.btn_float = QPushButton("🗖 분리" if not self.dock_widget.isFloating() else "🗗 도킹")
        self.btn_float.clicked.connect(self.toggle_float)
        layout.addWidget(self.btn_float)

        # 4. Close button
        self.btn_close = QPushButton("✕")
        self.btn_close.clicked.connect(self.dock_widget.close)
        layout.addWidget(self.btn_close)

    def apply_theme_styling(self):
        """
        Dynamically applies main window active colors to DockTitleBar controls.
        """
        theme_cfg = self.dock_widget.main_window.config_data.get("theme_config", {
            "window_bg": "#0e0f12",
            "card_bg": "#13141a",
            "border": "#272a38",
            "accent": "#38bdf8",
            "text": "#a0a5b5"
        })
        
        win_bg = theme_cfg.get("window_bg", "#0e0f12")
        card_bg = theme_cfg.get("card_bg", "#13141a")
        border = theme_cfg.get("border", "#272a38")
        accent = theme_cfg.get("accent", "#38bdf8")
        text = theme_cfg.get("text", "#a0a5b5")

        self.setStyleSheet(f"""
            QWidget {{
                background-color: {card_bg};
                border-bottom: 1px solid {border};
            }}
            QLabel {{
                color: {accent};
                font-size: 11px;
                font-weight: bold;
                background: transparent;
            }}
            QPushButton {{
                background-color: {win_bg};
                border: 1px solid {border};
                border-radius: 4px;
                color: {text};
                font-size: 9px;
                font-weight: bold;
                padding: 3px 8px;
            }}
            QPushButton:hover {{
                background-color: {card_bg};
                border-color: {accent};
                color: {accent};
            }}
            QPushButton#btn_dock {{
                background-color: {accent};
                border-color: {accent};
                color: {win_bg};
            }}
            QPushButton#btn_dock:hover {{
                background-color: {card_bg};
                border-color: {accent};
                color: {accent};
            }}
        """)

        # Special close button hover coloring
        self.btn_close.setStyleSheet("QPushButton { color: #f87171; } QPushButton:hover { background-color: #ef4444; color: white; border-color: #ef4444; }")

    def toggle_float(self):
        self.dock_widget.setFloating(not self.dock_widget.isFloating())

    def dock_back(self):
        self.dock_widget.setFloating(False)

    def update_title(self, text):
        self.lbl_title.setText(text)

    def update_buttons_visibility(self):
        self.btn_dock.setVisible(self.dock_widget.isFloating())
        self.btn_config.setVisible(self.dock_widget.widget_type is not None)
        self.btn_float.setText("🗗 도킹" if self.dock_widget.isFloating() else "🗖 분리")


class ConfigurableTelemetryDock(QDockWidget):
    """
    Highly configurable container QDockWidget.
    Starts as a blank settings manager and transforms dynamically.
    """
    def __init__(self, main_window, parent=None, saved_state=None):
        super().__init__("🆕 빈 모니터링 윈도우", parent or main_window)
        self.main_window = main_window

        # Unique identifier for saving profile states
        self.dock_id = saved_state.get("dock_id") if saved_state else f"custom_dock_{uuid.uuid4().hex[:8]}"
        self.setObjectName(self.dock_id)
        self.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        # Sense default floating size
        self.resize(420, 320)

        # State properties
        self.widget_type = saved_state.get("widget_type") if saved_state else None
        self.subsystem_id = saved_state.get("subsystem_id") if saved_state else "ALL"
        self.selected_vars = set(saved_state.get("selected_vars", [])) if saved_state else set()

        self.active_view = None

        # Setup custom title bar
        self.title_bar = DockTitleBar(self)
        self.setTitleBarWidget(self.title_bar)
        
        # Connect float state change listener
        self.topLevelChanged.connect(self.on_top_level_changed)

        # If we have saved state, load it immediately, otherwise load config panel
        if self.widget_type:
            self.apply_configuration()
        else:
            self.show_config_panel()

    def on_top_level_changed(self, is_floating):
        if is_floating:
            # Re-enforce a safe size when popped out
            self.resize(450, 350)
        self.title_bar.update_buttons_visibility()

    def show_config_panel(self):
        """
        Unmounts active view and displays settings selection panel.
        """
        if self.active_view:
            if hasattr(self.active_view, "closeEvent"):
                from PyQt6.QtGui import QCloseEvent
                self.active_view.closeEvent(QCloseEvent())
            self.active_view.deleteLater()
            self.active_view = None

        self.setWindowTitle("🆕 모니터링 구성 설정")
        self.title_bar.update_title("🆕 모니터링 구성 설정")
        self.title_bar.update_buttons_visibility()

        config_widget = QWidget()
        self.apply_config_panel_theme(config_widget)

        layout = QVBoxLayout(config_widget)
        layout.setContentsMargins(15, 12, 15, 12)
        layout.setSpacing(8)

        # Usability tip label
        lbl_tip = QLabel("💡 팁: 창의 타이틀바를 더블 클릭하거나 드래그하면 화면에서 분리할 수 있으며,\n분리된 창의 '📌 화면에 도킹' 버튼을 누르면 즉시 원래 화면으로 안전하게 되돌아갑니다!")
        lbl_tip.setWordWrap(True)
        lbl_tip.setStyleSheet("""
            color: #8c8ea0;
            background-color: #151622;
            border: 1px solid #282a40;
            border-radius: 4px;
            padding: 8px;
            font-size: 10px;
            line-height: 1.4;
        """)
        layout.addWidget(lbl_tip)

        # 0. Quick Start Presets (Highly intuitive for beginners!)
        layout.addWidget(QLabel("⚡ 초보자용 퀵 스타트 프리셋 (Quick Start Presets):"))
        self.combo_preset = QComboBox()
        self.combo_preset.addItem("직접 선택 (Custom Setup)", "custom")
        self.combo_preset.addItem("📈 실시간 파형 차트 스코프 프리셋", "preset_charts")
        self.combo_preset.addItem("📟 핵심 수치 카드 알림 세트", "preset_cards")
        self.combo_preset.addItem("🔌 프로토콜 HEX 스니퍼 콘솔", "preset_analyzer")
        self.combo_preset.addItem("⚡ 서브시스템 연결 토폴로지 맵", "preset_topology")
        self.combo_preset.addItem("🔧 매개변수 설정 & 제한치 프리셋", "preset_parameter")
        self.combo_preset.addItem("🌐 서비스 허브 프리셋", "preset_service")
        self.combo_preset.addItem("📺 디버그 터미널 프리셋", "preset_terminal")
        self.combo_preset.currentIndexChanged.connect(self.apply_preset_choice)
        layout.addWidget(self.combo_preset)

        # 1. Select type
        layout.addWidget(QLabel("1. 위젯/윈도우 기능 선택:"))
        self.combo_type = QComboBox()
        self.combo_type.addItem("📊 실시간 파형 차트 스코프", "trend_charts")
        self.combo_type.addItem("📟 텍스트 수치 카드", "telemetry_cards")
        self.combo_type.addItem("🔌 프로토콜 패킷 분석기", "protocol_analyzer")
        self.combo_type.addItem("⚡ 노드 토폴로지 맵", "topology_visualizer")
        self.combo_type.addItem("🔧 매개변수 설정 & 제한치 제어", "parameter_manager")
        self.combo_type.addItem("🌐 로깅 & 스트리밍 서비스 허브", "service_console")
        self.combo_type.addItem("📺 하드웨어 VCP 터미널 콘솔", "mcu_terminal")
        
        idx = self.combo_type.findData(self.widget_type)
        if idx != -1:
            self.combo_type.setCurrentIndex(idx)
        layout.addWidget(self.combo_type)

        # 2. Select subsystem
        layout.addWidget(QLabel("2. 연동할 서브시스템 노드 선택:"))
        self.combo_sub = QComboBox()
        self.combo_sub.addItem("전체 서브시스템 (All Nodes)", "ALL")
        for sub in self.main_window.config_data.get("subsystems", []):
            self.combo_sub.addItem(f"{sub.get('display_name', sub['name'])} ({sub['name']})", sub["name"])

        idx = self.combo_sub.findData(self.subsystem_id)
        if idx != -1:
            self.combo_sub.setCurrentIndex(idx)
            
        self.combo_sub.currentIndexChanged.connect(self.update_variables_list)
        layout.addWidget(self.combo_sub)

        # 3. Check variables
        layout.addWidget(QLabel("3. 표시할 데이터 변수 선택:"))
        self.scroll_vars = QScrollArea()
        self.scroll_vars.setWidgetResizable(True)

        self.vars_container = QWidget()
        self.vars_container.setStyleSheet("background: transparent;")
        self.vars_lay = QVBoxLayout(self.vars_container)
        self.vars_lay.setContentsMargins(6, 6, 6, 6)
        self.vars_lay.setSpacing(6)
        self.scroll_vars.setWidget(self.vars_container)

        layout.addWidget(self.scroll_vars)

        self.update_variables_list()

        # Apply button
        btn_apply = QPushButton("🚀 설정 적용 및 모니터링 시작")
        btn_apply.clicked.connect(self.on_apply_clicked)
        layout.addWidget(btn_apply)

        self.setWidget(config_widget)

    def apply_preset_choice(self):
        preset = self.combo_preset.currentData()
        if preset == "custom":
            return
            
        self.combo_type.blockSignals(True)
        self.combo_sub.blockSignals(True)
        
        if preset == "preset_charts":
            self.combo_type.setCurrentIndex(self.combo_type.findData("trend_charts"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
            # Check all variables
            for chk in self.var_checkboxes:
                chk.setChecked(True)
                
        elif preset == "preset_cards":
            self.combo_type.setCurrentIndex(self.combo_type.findData("telemetry_cards"))
            # Default to first subsystem if available
            subsystems = self.main_window.config_data.get("subsystems", [])
            if subsystems:
                sub_name = subsystems[0]["name"]
                self.combo_sub.setCurrentIndex(self.combo_sub.findData(sub_name))
            else:
                self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
            for chk in self.var_checkboxes:
                chk.setChecked(True)
                
        elif preset == "preset_analyzer":
            self.combo_type.setCurrentIndex(self.combo_type.findData("protocol_analyzer"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
            
        elif preset == "preset_topology":
            self.combo_type.setCurrentIndex(self.combo_type.findData("topology_visualizer"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
        elif preset == "preset_parameter":
            self.combo_type.setCurrentIndex(self.combo_type.findData("parameter_manager"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
        elif preset == "preset_service":
            self.combo_type.setCurrentIndex(self.combo_type.findData("service_console"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()
        elif preset == "preset_terminal":
            self.combo_type.setCurrentIndex(self.combo_type.findData("mcu_terminal"))
            self.combo_sub.setCurrentIndex(self.combo_sub.findData("ALL"))
            self.update_variables_list()

        self.combo_type.blockSignals(False)
        self.combo_sub.blockSignals(False)

    def update_variables_list(self):
        while self.vars_lay.count():
            item = self.vars_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

        self.var_checkboxes = []
        selected_sub = self.combo_sub.currentData()

        subsystems = self.main_window.config_data.get("subsystems", [])
        for sub in subsystems:
            if selected_sub != "ALL" and sub["name"] != selected_sub:
                continue

            for var in sub.get("variables", []):
                chk = QCheckBox(f"{var['name']} ({var.get('display_name', var['name'])})")
                chk.setProperty("var_name", var["name"])

                if var["name"] in self.selected_vars or not self.selected_vars:
                    chk.setChecked(True)

                self.vars_lay.addWidget(chk)
                self.var_checkboxes.append(chk)

        self.vars_lay.addStretch()

    def on_apply_clicked(self):
        self.widget_type = self.combo_type.currentData()
        self.subsystem_id = self.combo_sub.currentData()
        self.selected_vars = {chk.property("var_name") for chk in self.var_checkboxes if chk.isChecked()}

        self.apply_configuration()
        self.save_custom_docks_state()

    def apply_configuration(self):
        """
        Dynamically instantiates and mounts selected telemetry widget.
        """
        if self.active_view:
            if hasattr(self.active_view, "closeEvent"):
                from PyQt6.QtGui import QCloseEvent
                self.active_view.closeEvent(QCloseEvent())
            self.active_view.deleteLater()
            self.active_view = None

        container = QWidget()
        container.setStyleSheet("background-color: #0c0d12;")
        layout = QVBoxLayout(container)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # Icon / text mappings
        type_titles = {
            "trend_charts": ("📈 Waveform Scope", "📈 파형 차트 스코프"),
            "telemetry_cards": ("📟 Telemetry Cards", "📟 수치 카드"),
            "protocol_analyzer": ("🔌 Protocol Analyzer", "🔌 프로토콜 분석기"),
            "topology_visualizer": ("⚡ Topology Interconnector", "⚡ 토폴로지 맵"),
            "parameter_manager": ("🔧 Parameter Limits", "🔧 매개변수 & 제한치"),
            "service_console": ("🌐 Services Hub", "🌐 서비스 허브"),
            "mcu_terminal": ("📺 MCU Terminal", "📺 하드웨어 터미널")
        }

        header_text, win_title = type_titles.get(self.widget_type, ("🆕 Monitoring Window", "🆕 모니터링"))
        disp_sub = self.subsystem_id if self.subsystem_id != "ALL" else "All"

        self.setWindowTitle(f"{win_title} [{disp_sub}]")
        self.title_bar.update_title(f"{win_title} [{disp_sub}]")
        self.title_bar.update_buttons_visibility()

        # Reusable Widget dynamic mounting
        view_widget = None
        if self.widget_type == "trend_charts":
            from plugins.trend_charts import TrendChartsWidget
            view_widget = TrendChartsWidget(
                main_window=self.main_window, 
                parent=self, 
                subsystem_id=self.subsystem_id, 
                visible_variables=self.selected_vars
            )
        elif self.widget_type == "telemetry_cards":
            from plugins.telemetry_cards import TelemetryCardsWidget
            view_widget = TelemetryCardsWidget(
                main_window=self.main_window, 
                parent=self, 
                subsystem_id=self.subsystem_id, 
                visible_variables=self.selected_vars
            )
        elif self.widget_type == "protocol_analyzer":
            from plugins.protocol_analyzer import ProtocolAnalyzerWidget
            view_widget = ProtocolAnalyzerWidget(
                main_window=self.main_window, 
                parent=self
            )
        elif self.widget_type == "topology_visualizer":
            from plugins.topology_visualizer import TopologyVisualizerWidget
            view_widget = TopologyVisualizerWidget(
                main_window=self.main_window, 
                parent=self
            )
        elif self.widget_type == "parameter_manager":
            from plugins.parameter_manager import ParameterManagerWidget
            view_widget = ParameterManagerWidget(
                main_window=self.main_window, 
                parent=self
            )
        elif self.widget_type == "service_console":
            from plugins.service_console import ServiceConsoleWidget
            view_widget = ServiceConsoleWidget(
                main_window=self.main_window, 
                parent=self
            )
        elif self.widget_type == "mcu_terminal":
            from plugins.mcu_terminal import McuTerminalWidget
            view_widget = McuTerminalWidget(
                main_window=self.main_window, 
                parent=self
            )

        if view_widget:
            self.active_view = view_widget
            layout.addWidget(view_widget, 1)

        self.setWidget(container)

    def save_custom_docks_state(self):
        if hasattr(self.main_window, "save_custom_docks_layout"):
            self.main_window.save_custom_docks_layout()

    def get_serialization_state(self):
        return {
            "dock_id": self.dock_id,
            "widget_type": self.widget_type,
            "subsystem_id": self.subsystem_id,
            "selected_vars": list(self.selected_vars)
        }

    def apply_theme_styling(self):
        """
        Refreshes stylesheet, title bar styling, and any custom color themes on the active configuration panel or view.
        """
        # 1. Update Title Bar Styling
        if self.title_bar:
            self.title_bar.apply_theme_styling()
            
        # 2. Update config panel stylesheet if currently visible
        current_w = self.widget()
        if current_w and hasattr(self, "combo_type"):
            self.apply_config_panel_theme(current_w)
            
        # 3. Propagate to active views if they have custom styling
        if self.active_view:
            if hasattr(self.active_view, "setStyleSheet"):
                theme_cfg = self.main_window.config_data.get("theme_config", {
                    "window_bg": "#0e0f12",
                    "card_bg": "#13141a",
                    "accent": "#38bdf8"
                })
                # Set dynamic background styling to the container widget
                self.active_view.setStyleSheet(f"background-color: {theme_cfg.get('window_bg', '#0c0d12')};")
            if hasattr(self.active_view, "rebuild_plots"):
                self.active_view.rebuild_plots()

    def apply_config_panel_theme(self, widget):
        if not widget:
            return
        theme_cfg = self.main_window.config_data.get("theme_config", {
            "window_bg": "#0e0f12",
            "card_bg": "#13141a",
            "border": "#272a38",
            "accent": "#38bdf8",
            "text": "#a0a5b5"
        })
        win_bg = theme_cfg.get("window_bg", "#0e0f12")
        card_bg = theme_cfg.get("card_bg", "#13141a")
        border = theme_cfg.get("border", "#272a38")
        accent = theme_cfg.get("accent", "#38bdf8")
        text = theme_cfg.get("text", "#a0a5b5")

        widget.setStyleSheet(f"""
            QWidget {{ background-color: {win_bg}; color: {text}; }}
            QLabel {{ font-size: 11px; font-weight: bold; color: {text}; }}
            QComboBox {{
                background-color: {card_bg};
                border: 1px solid {border};
                border-radius: 4px;
                padding: 6px;
                color: {accent};
                font-weight: bold;
                font-size: 11px;
            }}
            QPushButton {{
                background-color: {accent};
                border: 1px solid {accent};
                border-radius: 6px;
                color: {win_bg};
                font-weight: bold;
                padding: 8px 16px;
                font-size: 11px;
            }}
            QPushButton:hover {{
                background-color: {card_bg};
                border-color: {accent};
                color: {accent};
            }}
            QScrollArea {{
                background-color: {card_bg};
                border: 1px solid {border};
                border-radius: 6px;
            }}
            QCheckBox {{
                spacing: 8px;
                font-size: 11px;
                color: {text};
            }}
            QCheckBox:hover {{ color: white; }}
        """)

    def closeEvent(self, event):
        if self.active_view:
            if hasattr(self.active_view, "closeEvent"):
                from PyQt6.QtGui import QCloseEvent
                self.active_view.closeEvent(QCloseEvent())
            self.active_view.deleteLater()
            self.active_view = None

        if hasattr(self.main_window, "custom_docks") and self.dock_id in self.main_window.custom_docks:
            del self.main_window.custom_docks[self.dock_id]
            self.save_custom_docks_state()

        super().closeEvent(event)
