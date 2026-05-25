# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.1.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Consolidated Parameter Calibration & Safety Limits Plugin
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.1.0 (2026-05-22) - Antigravity: Consolidated calibrator.py and safety_alarms.py into a single high-density instrument panel.
# ======================================================================

from PyQt6.QtWidgets import (
    QDockWidget, QWidget, QVBoxLayout, QHBoxLayout, QGridLayout,
    QGroupBox, QComboBox, QListView, QTableWidget, QTableWidgetItem,
    QHeaderView, QLabel, QSlider
)
from PyQt6.QtCore import Qt, QTimer, pyqtSlot
from PyQt6.QtGui import QColor, QFont
from plugins.base_plugin import BasePlugin

class ParameterManagerPlugin(BasePlugin):
    """
    High-density industrial control panel that integrates linear calibration mapping
    (y = ax + b) and safety alarms/threshold sliders inside a single cohesive console.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "parameter_manager"
        self.name = "Parameter Manager"
        self.description = "Consolidated parameter calibration and dynamic safety thresholds manager."
        
        self.active_subsystem = ""
        self.led_widgets = {}          # alarm_type -> QLabel
        self.temp_widgets = {}         # temp_type -> QLabel
        self.sliders = {}              # limit_type -> QSlider
        self.slider_labels = {}        # limit_type -> QLabel
        
        self.alarm_blink_state = False
        self.blink_timer = None

    def on_enable(self):
        self.dock_widget = QDockWidget("🔧 Parameter Calibration & Limits", self.main_window)
        self.dock_widget.setObjectName("dock_parameter_manager")
        
        main_container = QWidget()
        main_lay = QVBoxLayout(main_container)
        main_lay.setContentsMargins(8, 8, 8, 8)
        main_lay.setSpacing(6)
        
        # Subsystem selection bar
        sel_bar = QHBoxLayout()
        sel_bar.addWidget(QLabel("Select Subsystem Node:"))
        self.combo_subs = QComboBox()
        self.combo_subs.setView(QListView())
        self.combo_subs.currentTextChanged.connect(self.on_subsystem_selection_changed)
        sel_bar.addWidget(self.combo_subs, 1)
        sel_bar.addStretch(1)
        main_lay.addLayout(sel_bar)
        
        # Horizontal Split Panel
        split_layout = QHBoxLayout()
        split_layout.setSpacing(10)
        
        # 1. Left Sub-panel: Safety Limits & Alarms
        self.safety_group = QGroupBox("🚦 Safety Limits & Thresholds")
        self.safety_lay = QHBoxLayout(self.safety_group)
        self.safety_lay.setContentsMargins(8, 12, 8, 8)
        self.safety_lay.setSpacing(8)
        
        # 1a. Alarm LED Status Blocks
        led_container = QWidget()
        led_v_lay = QVBoxLayout(led_container)
        led_v_lay.setContentsMargins(0, 0, 0, 0)
        led_v_lay.setSpacing(4)
        
        self.lbl_state = QLabel("OFFLINE")
        self.lbl_ovp = QLabel("OVP NORMAL")
        self.lbl_ocp = QLabel("OCP NORMAL")
        self.lbl_otp = QLabel("OTP NORMAL")
        
        for led in [self.lbl_state, self.lbl_ovp, self.lbl_ocp, self.lbl_otp]:
            led.setAlignment(Qt.AlignmentFlag.AlignCenter)
            led.setFixedHeight(22)
            led.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
            led.setStyleSheet("background-color: #171821; color: #546e7a; border-radius: 4px; border: 1px solid #282b3d;")
            led_v_lay.addWidget(led)
            
        self.led_widgets["state"] = self.lbl_state
        self.led_widgets["ovp"] = self.lbl_ovp
        self.led_widgets["ocp"] = self.lbl_ocp
        self.led_widgets["otp"] = self.lbl_otp
        
        # Thermals
        self.lbl_t1 = QLabel("MOSFET (T1): OFFLINE")
        self.lbl_t2 = QLabel("Transformer (T2): OFFLINE")
        self.lbl_t1.setStyleSheet("color: #8c91a5; font-size: 10px;")
        self.lbl_t2.setStyleSheet("color: #8c91a5; font-size: 10px;")
        led_v_lay.addWidget(self.lbl_t1)
        led_v_lay.addWidget(self.lbl_t2)
        
        self.temp_widgets["t1"] = self.lbl_t1
        self.temp_widgets["t2"] = self.lbl_t2
        
        self.safety_lay.addWidget(led_container, 1)
        
        # 1b. Boundary Threshold Sliders
        slider_container = QWidget()
        slider_v_lay = QVBoxLayout(slider_container)
        slider_v_lay.setContentsMargins(0, 0, 0, 0)
        slider_v_lay.setSpacing(4)
        
        limits_configs = [
            ("ovp", "OVP Boundary", 0, 450, "V"),
            ("ocp", "OCP Boundary", 0, 150, "A"),
            ("otp", "OTP Boundary", 0, 150, "°C")
        ]
        
        for key, display, min_val, max_val, unit in limits_configs:
            lbl_slider = QLabel(f"{display}: --- {unit}")
            lbl_slider.setStyleSheet("font-size: 9px; font-weight: bold; color: #8c91a5;")
            slider_v_lay.addWidget(lbl_slider)
            self.slider_labels[key] = lbl_slider
            
            slider = QSlider(Qt.Orientation.Horizontal)
            slider.setRange(min_val, max_val)
            slider.setFixedWidth(130)
            slider.valueChanged.connect(lambda val, k=key, u=unit, d=display: self.on_slider_changed(k, val, u, d))
            slider_v_lay.addWidget(slider)
            self.sliders[key] = slider
            
        self.safety_lay.addWidget(slider_container, 0)
        split_layout.addWidget(self.safety_group, 2)
        
        # 2. Right Sub-panel: Calibration table
        self.cal_group = QGroupBox("🔧 Linear Calibration & Column Mappers (y = ax + b)")
        cal_lay = QVBoxLayout(self.cal_group)
        cal_lay.setContentsMargins(8, 12, 8, 8)
        
        self.table_cal = QTableWidget()
        self.table_cal.setColumnCount(5)
        self.table_cal.setHorizontalHeaderLabels(["Variable", "Index / Key", "Gain (a)", "Offset (b)", "Live Value"])
        self.table_cal.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.table_cal.horizontalHeader().resizeSection(0, 110)
        self.table_cal.horizontalHeader().resizeSection(1, 90)
        self.table_cal.horizontalHeader().resizeSection(2, 70)
        self.table_cal.horizontalHeader().resizeSection(3, 70)
        self.table_cal.horizontalHeader().setSectionResizeMode(4, QHeaderView.ResizeMode.Stretch)
        self.table_cal.verticalHeader().setVisible(False)
        self.table_cal.cellChanged.connect(self.on_table_edited)
        
        # Professional Table QSS matching theme
        self.table_cal.setStyleSheet("""
            QTableWidget { background-color: #0b0c10; gridline-color: #272a38; border: 1px solid #272a38; }
            QHeaderView::section { background-color: #171821; color: #a0a5b5; font-weight: bold; border: 1px solid #272a38; padding: 3px; }
            QTableWidget::item { padding: 2px; }
        """)
        cal_lay.addWidget(self.table_cal)
        
        split_layout.addWidget(self.cal_group, 3)
        main_lay.addLayout(split_layout)
        
        self.dock_widget.setWidget(main_container)
        
        # Hook communication signals
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)
        self.refresh_subsystems()
        
        # LEDs warnings blinking timer
        self.blink_timer = QTimer(self.dock_widget)
        self.blink_timer.timeout.connect(self.update_blinking_leds)
        self.blink_timer.start(200)

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if self.blink_timer:
            self.blink_timer.stop()
            self.blink_timer = None
        super().on_disable()

    def refresh_subsystems(self):
        """
        Updates the subsystems selector dropdown list.
        """
        self.combo_subs.blockSignals(True)
        self.combo_subs.clear()
        
        router = self.main_window.data_router
        for sub_name in router.subsystems.keys():
            self.combo_subs.addItem(sub_name, sub_name)
            
        if self.combo_subs.count() > 0:
            self.combo_subs.setCurrentIndex(0)
            self.active_subsystem = self.combo_subs.currentText()
        else:
            self.active_subsystem = ""
            
        self.combo_subs.blockSignals(False)
        self.refresh_cal_and_sliders()

    def on_subsystem_selection_changed(self, text):
        self.active_subsystem = text
        self.refresh_cal_and_sliders()

    def refresh_cal_and_sliders(self):
        """
        Synchronizes all slider limits and linear calibration tables to match chosen subsystem.
        """
        self.table_cal.blockSignals(True)
        self.table_cal.setRowCount(0)
        
        # Block signals from sliders during refresh
        for s in self.sliders.values():
            s.blockSignals(True)
            
        router = self.main_window.data_router
        if not self.active_subsystem or self.active_subsystem not in router.subsystems:
            for s in self.sliders.values():
                s.blockSignals(False)
            self.table_cal.blockSignals(False)
            return
            
        sub = router.subsystems[self.active_subsystem]
        
        # 1. Update sliders value
        thresholds = sub.thresholds
        limits_configs = [("ovp", "OVP Boundary", "V"), ("ocp", "OCP Boundary", "A"), ("otp", "OTP Boundary", "°C")]
        
        for key, display, unit in limits_configs:
            val = thresholds.get(f"{key}_val", 0.0)
            if key in self.sliders:
                self.sliders[key].setValue(int(val))
            if key in self.slider_labels:
                self.slider_labels[key].setText(f"{display}: {val:.0f}{unit}")
                
        # 2. Update variables in calibration table
        self.table_cal.setRowCount(len(sub.variables))
        for row_idx, var in enumerate(sub.variables):
            item_lbl = QTableWidgetItem(var["display_name"])
            item_lbl.setFlags(Qt.ItemFlag.ItemIsEnabled)
            item_lbl.setForeground(QColor("#a0a5b5"))
            self.table_cal.setItem(row_idx, 0, item_lbl)
            
            item_col = QTableWidgetItem(str(var["column_index"]))
            self.table_cal.setItem(row_idx, 1, item_col)
            
            item_gain = QTableWidgetItem(f"{var['gain']:.4f}")
            self.table_cal.setItem(row_idx, 2, item_gain)
            
            item_offset = QTableWidgetItem(f"{var['offset']:.2f}")
            self.table_cal.setItem(row_idx, 3, item_offset)
            
            item_live = QTableWidgetItem("---")
            item_live.setFlags(Qt.ItemFlag.ItemIsEnabled)
            item_live.setForeground(QColor("#00a2ff"))
            item_live.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.table_cal.setItem(row_idx, 4, item_live)
            
        # Re-enable signals
        for s in self.sliders.values():
            s.blockSignals(False)
        self.table_cal.blockSignals(False)

    def on_slider_changed(self, key, val, unit, display_name):
        """
        Triggered when safety boundary slider is dragged by user.
        """
        router = self.main_window.data_router
        if self.active_subsystem in router.subsystems:
            sub = router.subsystems[self.active_subsystem]
            sub.thresholds[f"{key}_val"] = float(val)
            
            if key in self.slider_labels:
                self.slider_labels[key].setText(f"{display_name}: {val:.0f}{unit}")
                
            self.main_window.save_workspace_config()

    def on_table_edited(self, row, col):
        """
        Syncs inline edits in the calibration table directly to config.json.
        """
        router = self.main_window.data_router
        if not self.active_subsystem or self.active_subsystem not in router.subsystems:
            return
            
        sub = router.subsystems[self.active_subsystem]
        if row >= len(sub.variables):
            return
            
        var = sub.variables[row]
        item = self.table_cal.item(row, col)
        if not item:
            return
            
        text = item.text().strip()
        try:
            if col == 1:
                var["column_index"] = int(text) if text.isdigit() else text
            elif col == 2:
                var["gain"] = float(text)
            elif col == 3:
                var["offset"] = float(text)
                
            self.main_window.save_workspace_config()
        except Exception as e:
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"Calibrator Input Error (Row {row}, Col {col}): {str(e)}")
            self.refresh_cal_and_sliders() # revert

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        """
        Fast slot receiving telemetry updates to push direct numeric/thermal outputs.
        """
        router = self.main_window.data_router
        if subsystem_name not in router.subsystems:
            return
            
        sub = router.subsystems[subsystem_name]
        
        # 1. Update live value cells if this subsystem is currently selected
        if subsystem_name == self.active_subsystem:
            self.table_cal.blockSignals(True)
            for row_idx, var in enumerate(sub.variables):
                var_name = var["name"]
                if var_name in data:
                    val = data[var_name]
                    item = self.table_cal.item(row_idx, 4)
                    if item:
                        item.setText(f"{val:.2f}" if isinstance(val, float) else str(val))
            self.table_cal.blockSignals(False)
            
            # Update thermals readout labels
            t1_var = sub.temp_mosfet_var
            if t1_var and t1_var in data:
                self.lbl_t1.setText(f"MOSFET (T1): {data[t1_var]:.1f} °C")
                
            t2_var = sub.temp_transformer_var
            if t2_var and t2_var in data:
                self.lbl_t2.setText(f"Transformer (T2): {data[t2_var]:.1f} °C")

    def update_blinking_leds(self):
        """
        Blinks warning LEDs red dynamically at 5Hz on protective boundary faults.
        """
        self.alarm_blink_state = not self.alarm_blink_state
        router = self.main_window.data_router
        
        if not self.active_subsystem or self.active_subsystem not in router.subsystems:
            for led in self.led_widgets.values():
                led.setStyleSheet("background-color: #171821; color: #546e7a; border-radius: 4px; border: 1px solid #282b3d;")
            self.lbl_state.setText("OFFLINE")
            return
            
        sub = router.subsystems[self.active_subsystem]
        
        # Standby status
        if not sub.alarm_states.get("standby", True):
            self.lbl_state.setStyleSheet("background-color: #047857; color: #ffffff; border-radius: 4px; border: 1px solid #059669; font-weight: bold;")
            self.lbl_state.setText("SYSTEM ACTIVE / RUN")
        else:
            self.lbl_state.setStyleSheet("background-color: #272a38; color: #8c91a5; border-radius: 4px; border: 1px solid #282b3d;")
            self.lbl_state.setText("SYSTEM STANDBY")
            
        # OVP, OCP, OTP Protection indicators
        for alarm_key, label_text in [("ovp", "OVP"), ("ocp", "OCP"), ("otp", "OTP")]:
            led = self.led_widgets.get(alarm_key)
            if led:
                is_active = sub.alarm_states.get(alarm_key, False)
                if is_active:
                    if self.alarm_blink_state:
                        led.setStyleSheet("background-color: #dc2626; color: #ffffff; border-radius: 4px; border: 1px solid #dc2626; font-weight: bold;")
                    else:
                        led.setStyleSheet("background-color: #7f1d1d; color: #f87171; border-radius: 4px; border: 1px solid #991b1b; font-weight: bold;")
                    led.setText(f"{label_text} FAULT")
                else:
                    led.setStyleSheet("background-color: #171821; color: #546e7a; border-radius: 4px; border: 1px solid #282b3d;")
                    led.setText(f"{label_text} NORMAL")

    def rebuild_ui(self):
        """
        Triggered when subsystems configuration gets reloaded.
        """
        self.refresh_subsystems()

    def rebuild_plots(self):
        pass
