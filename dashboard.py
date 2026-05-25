# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Completed Phase 3 refactoring of Main GUI Frame to integrate plugin system, dynamic Serial manager, and Profile Setup Wizard.
# ======================================================================

import os
import sys
import time
import json
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
    QGridLayout, QGroupBox, QLabel, QPushButton, QComboBox, 
    QTableWidget, QTableWidgetItem, QHeaderView, QSlider, 
    QDoubleSpinBox, QSpinBox, QTextEdit, QFileDialog, QRadioButton, QButtonGroup,
    QTabWidget, QDockWidget, QCheckBox, QFrame, QListView, QLineEdit, QDialog,
    QDialogButtonBox, QMessageBox, QScrollArea
)
from PyQt6.QtCore import Qt, QTimer, pyqtSlot
from PyQt6.QtGui import QFont, QColor

# Dynamic Infrastructure imports
from serial_manager import MultiPortSerialManager
from data_router import DataRouter
from config_manager import ConfigManager
from plugin_manager import PluginManager

class WorkspaceSetupTab(QWidget):
    """
    Highly detailed, full-screen configuration manager panel for dynamic workspaces.
    Allows complete runtime editing of serial ports, subsystems, variables, 
    packet routing rules, and mathematical linked formulas.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.main_window = main_window
        self.config_data = {} # Holds active editing copy
        
        self.setStyleSheet("""
            WorkspaceSetupTab { background-color: #13141a; }
            QWidget { color: #b5b5c3; font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; }
            
            /* Tabs styling */
            QTabWidget::pane { border: 1px solid #272a38; background-color: #0f0f12; border-radius: 6px; top: -1px; }
            QTabWidget > QWidget { background-color: #0f0f12; }
            QTabBar::tab { background-color: #1a1a24; color: #a2a2b5; padding: 8px 20px; border: 1px solid #272a38; border-bottom: none; font-weight: bold; border-top-left-radius: 4px; border-top-right-radius: 4px;}
            QTabBar::tab:selected { background-color: #0f0f12; color: #38bdf8; border-bottom: 2px solid #38bdf8; }
            QTabBar::tab:hover { color: #ffffff; background-color: #222230; }
            
            /* ScrollArea styling */
            QScrollArea { background-color: #0f0f12; border: none; }
            QScrollArea::viewport { background-color: #0f0f12; }
            #right_container { background-color: #0f0f12; }
            
            /* Nested widgets inside tabs */
            QWidget#left_panel { background-color: #0c0c0e; border-right: 1px solid #272a38; }
            
            /* Table Widget */
            QTableWidget { background-color: #050507; border: 1px solid #272a38; gridline-color: #1c1c24; border-radius: 4px; color: #ffffff; }
            QHeaderView::section { background-color: #171721; color: #38bdf8; font-weight: bold; border: 1px solid #272a38; padding: 4px; }
            QTableWidget QTableCornerButton::section { background-color: #171721; border: 1px solid #272a38; }
            
            /* Inputs & Controls */
            QLineEdit, QComboBox, QDoubleSpinBox, QSpinBox { background-color: #0b0b0d; border: 1px solid #272a38; border-radius: 4px; padding: 4px; color: #ffffff; }
            QLineEdit:focus, QComboBox:focus, QDoubleSpinBox:focus, QSpinBox:focus { border-color: #38bdf8; }
            QComboBox QAbstractItemView { background-color: #0c0c0e; border: 1px solid #272a38; selection-background-color: #1e1e2d; selection-color: #38bdf8; color: #ffffff; }
            
            /* Group Box */
            QGroupBox { border: 1px solid #272a38; border-radius: 6px; margin-top: 10px; padding-top: 12px; font-weight: bold; color: #38bdf8; }
            QGroupBox::title { subcontrol-origin: margin; subcontrol-position: top left; left: 10px; padding: 0 5px; background-color: #0f0f12; }
            
            /* Buttons */
            QPushButton { background-color: #1e1e2d; border: 1px solid #32324d; border-radius: 4px; color: #38bdf8; font-weight: bold; padding: 6px 14px; }
            QPushButton:hover { background-color: #27273d; border-color: #38bdf8; }
            QPushButton:pressed { background-color: #38bdf8; color: #0c0c0e; }
            
            /* Checkboxes */
            QCheckBox { spacing: 6px; font-weight: bold; color: #a2a2b5; background: transparent; }
            QCheckBox::indicator { width: 13px; height: 13px; border: 1px solid #272a38; background-color: #0b0b0d; border-radius: 3px; }
            QCheckBox::indicator:checked { background-color: #38bdf8; border-color: #38bdf8; }
            
            /* Labels */
            QLabel { background: transparent; }
        """)
        
        self.init_ui()
        self.load_configuration_into_ui()

    def init_ui(self):
        main_lay = QVBoxLayout(self)
        main_lay.setContentsMargins(12, 12, 12, 12)
        main_lay.setSpacing(10)
        
        # 1. Premium SCADA Header Panel
        header_widget = QWidget()
        header_widget.setStyleSheet("background-color: #171821; border: 1px solid #272a38; border-radius: 6px;")
        header_lay = QVBoxLayout(header_widget)
        header_lay.setContentsMargins(15, 10, 15, 10)
        header_lay.setSpacing(2)
        
        title_lbl = QLabel("🛠️ Enterprise Embedded Workspace Configurator")
        title_lbl.setStyleSheet("font-size: 15px; font-weight: bold; color: #38bdf8; border: none; background: transparent;")
        desc_lbl = QLabel("Establish physical USB COM ports list, register logical microcontroller subsystems, assign routing signatures, and build mathematical formulas.")
        desc_lbl.setStyleSheet("font-size: 11px; color: #8e94a6; border: none; background: transparent;")
        
        header_lay.addWidget(title_lbl)
        header_lay.addWidget(desc_lbl)
        main_lay.addWidget(header_widget)
        
        # 2. Main Config Tab Container
        self.tabs = QTabWidget()
        main_lay.addWidget(self.tabs, 1)
        
        self.setup_ports_tab()
        self.setup_subsystems_tab()
        self.setup_routing_tab()
        self.setup_formulas_tab()
        self.setup_plugins_tab()
        
        # 3. Actions Footer Panel
        btn_panel = QWidget()
        btn_panel.setStyleSheet("background-color: #121216; border: 1px solid #272a38; border-radius: 6px;")
        btn_lay = QHBoxLayout(btn_panel)
        btn_lay.setContentsMargins(15, 8, 15, 8)
        btn_lay.setSpacing(10)
        
        btn_apply = QPushButton("✅ Apply & Commit Changes")
        btn_apply.setStyleSheet("background-color: #059669; border-color: #10b981; color: white; font-weight: bold; padding: 6px 18px;")
        btn_apply.clicked.connect(self.validate_and_save)
        
        btn_save_as = QPushButton("💾 Save Profile As...")
        btn_save_as.setStyleSheet("background-color: #2563eb; border-color: #3b82f6; color: white; font-weight: bold; padding: 6px 18px;")
        btn_save_as.clicked.connect(self.main_window.save_profile_as)
        
        btn_load = QPushButton("📂 Load Profile...")
        btn_load.setStyleSheet("background-color: #374151; border-color: #4b5563; color: white; font-weight: bold; padding: 6px 18px;")
        btn_load.clicked.connect(self.main_window.load_profile)
        
        btn_presets = QPushButton("📋 프리셋 템플릿 로드/조합...")
        btn_presets.setStyleSheet("background-color: #7c3aed; border-color: #8b5cf6; color: white; font-weight: bold; padding: 6px 18px;")
        btn_presets.clicked.connect(self.open_preset_dialog)
        
        btn_revert = QPushButton("🔄 Revert Uncommitted Changes")
        btn_revert.setStyleSheet("background-color: #1b1c24; border-color: #272a38; color: #8e94a6; padding: 6px 18px;")
        btn_revert.clicked.connect(self.load_configuration_into_ui)
        
        btn_lay.addWidget(btn_apply)
        btn_lay.addWidget(btn_save_as)
        btn_lay.addWidget(btn_load)
        btn_lay.addWidget(btn_presets)
        btn_lay.addWidget(btn_revert)
        btn_lay.addStretch()
        main_lay.addWidget(btn_panel)

    def open_preset_dialog(self):
        dialog = PresetSelectionDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            preset_type = dialog.combo_presets.currentData()
            combine = dialog.radio_combine.isChecked()
            
            preset_dict = None
            if preset_type == "DAB":
                preset_dict = DAB_CONVERTER_PRESET
            elif preset_type == "BMS":
                preset_dict = EV_BMS_PRESET
            elif preset_type == "ARDUINO":
                preset_dict = ARDUINO_PLOTTER_PRESET
            elif preset_type == "ESS":
                from path_resolver import get_bundle_dir
                import os
                import json
                example_path = os.path.join(get_bundle_dir(), "example_profile.json")
                if os.path.exists(example_path):
                    try:
                        with open(example_path, 'r', encoding='utf-8') as f:
                            preset_dict = json.load(f)
                    except Exception as e:
                        QMessageBox.critical(self, "에러", f"예제 프로필을 불러오지 못했습니다:\n{str(e)}")
                        return
                else:
                    QMessageBox.warning(self, "경고", f"예제 프로필 파일을 찾을 수 없습니다:\n{example_path}")
                    return

            if preset_dict:
                self.main_window.apply_preset(preset_dict, combine=combine)
                if combine:
                    QMessageBox.information(self, "성공", "선택한 프리셋이 기존 구성에 정상적으로 조합되었습니다!")
                else:
                    QMessageBox.information(self, "성공", "선택한 프리셋으로 전체 구성이 덮어씌워졌습니다!")

    def load_configuration_into_ui(self):
        """
        Loads the main window's current config_data into the setup tab UI elements,
        effectively synchronizing it or rolling back any uncommitted edits.
        """
        # Deep copy active config
        self.config_data = json.loads(json.dumps(self.main_window.config_data))
        
        # 1. Ports Tab
        self.tbl_ports.blockSignals(True)
        self.tbl_ports.setRowCount(0)
        for p in self.config_data.get("ports", []):
            self.add_port_row(p["port"], p["baudrate"])
        self.tbl_ports.blockSignals(False)
        
        # 2. Subsystems Tab
        self.combo_subs.blockSignals(True)
        self.combo_subs.clear()
        for s in self.config_data.get("subsystems", []):
            self.combo_subs.addItem(s["name"])
        self.combo_subs.blockSignals(False)
        
        # Trigger selection reload for subsystems details
        if self.combo_subs.count() > 0:
            self.on_subsystem_selection_changed(self.combo_subs.currentText())
        else:
            self.rebuild_subsystem_details_panel("")
            
        # 3. Routing Rules Tab
        self.tbl_rules.blockSignals(True)
        self.tbl_rules.setRowCount(0)
        for r in self.config_data.get("routing_rules", []):
            self.add_rule_row(r["port"], r.get("type", "COLUMNS"), r.get("pattern", ""), r.get("target", ""))
        self.tbl_rules.blockSignals(False)
        
        # 4. Formulas Tab
        self.tbl_links.blockSignals(True)
        self.tbl_links.setRowCount(0)
        for f in self.config_data.get("linking_formulas", []):
            self.add_formula_row(f.get("target_sub", ""), f.get("target_var", ""), f.get("formula", ""))
        self.tbl_links.blockSignals(False)
        
        # 5. Plugins Tab synchronization
        self.main_window.load_plugins_from_profile()

    def setup_ports_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_ports")
        lay = QVBoxLayout(tab)
        
        desc = QLabel("🔌 Registered USB COM Serial Ports Monitoring Configuration:")
        desc.setStyleSheet("font-weight: bold; color: #ffffff; font-size: 12px;")
        lay.addWidget(desc)
        
        self.tbl_ports = QTableWidget(0, 2)
        self.tbl_ports.setHorizontalHeaderLabels(["COM Port Name (e.g. COM3)", "Baudrate Speed"])
        self.tbl_ports.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        lay.addWidget(self.tbl_ports)
        
        btn_lay = QHBoxLayout()
        btn_add = QPushButton("➕ Add Port Entry")
        btn_add.clicked.connect(self.add_port_row)
        btn_del = QPushButton("➖ Delete Selected Port")
        btn_del.clicked.connect(self.del_port_row)
        btn_lay.addWidget(btn_add)
        btn_lay.addWidget(btn_del)
        btn_lay.addStretch()
        lay.addLayout(btn_lay)
        
        self.tabs.addTab(tab, "🔌 COM Ports")

    def add_port_row(self, port="", baud=115200):
        row = self.tbl_ports.rowCount()
        self.tbl_ports.insertRow(row)
        
        item_port = QTableWidgetItem(port)
        self.tbl_ports.setItem(row, 0, item_port)
        
        combo_baud = QComboBox()
        combo_baud.addItems(["9600", "38400", "57600", "115200", "230400", "460800", "921600"])
        combo_baud.setCurrentText(str(baud))
        self.tbl_ports.setCellWidget(row, 1, combo_baud)

    def del_port_row(self):
        cur = self.tbl_ports.currentRow()
        if cur >= 0:
            self.tbl_ports.removeRow(cur)

    def setup_subsystems_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_subsystems")
        lay = QHBoxLayout(tab)
        
        # Left pane: Subsystems list
        left_panel = QWidget()
        left_panel.setObjectName("left_panel")
        left_lay = QVBoxLayout(left_panel)
        left_lay.setContentsMargins(0, 0, 0, 0)
        
        lbl_list = QLabel("💾 Subsystems Panels:")
        lbl_list.setStyleSheet("font-weight: bold; color: #ffffff;")
        left_lay.addWidget(lbl_list)
        
        self.combo_subs = QComboBox()
        self.combo_subs.setView(QListView())
        self.combo_subs.currentTextChanged.connect(self.on_subsystem_selection_changed)
        left_lay.addWidget(self.combo_subs)
        
        btn_sub_lay = QHBoxLayout()
        btn_add_sub = QPushButton("➕ Add Sub")
        btn_add_sub.clicked.connect(self.add_new_subsystem)
        btn_del_sub = QPushButton("➖ Remove")
        btn_del_sub.clicked.connect(self.remove_selected_subsystem)
        btn_sub_lay.addWidget(btn_add_sub)
        btn_sub_lay.addWidget(btn_del_sub)
        left_lay.addLayout(btn_sub_lay)
        left_lay.addStretch()
        
        lay.addWidget(left_panel, 1)
        
        # Right pane: Scroll area with details
        self.right_panel = QScrollArea()
        self.right_panel.setWidgetResizable(True)
        self.right_container = QWidget()
        self.right_container.setObjectName("right_container")
        self.right_lay = QVBoxLayout(self.right_container)
        self.right_panel.setWidget(self.right_container)
        
        lay.addWidget(self.right_panel, 3)
        self.tabs.addTab(tab, "⚡ Subsystems & Variables")

    def add_new_subsystem(self):
        from PyQt6.QtWidgets import QInputDialog
        text, ok = QInputDialog.getText(self, "Add Subsystem", "Enter unique subsystem ID (e.g. EngineNode):")
        if ok and text.strip():
            sub_id = text.strip()
            # Verify duplication
            for idx in range(self.combo_subs.count()):
                if self.combo_subs.itemText(idx) == sub_id:
                    QMessageBox.warning(self, "Error", "Subsystem ID already exists!")
                    return
            
            # Create a blank default skeleton structure
            new_sub = {
                "name": sub_id,
                "display_name": sub_id + " Panel",
                "variables": [
                    {"name": "temp", "display_name": "Core Temperature", "column_index": "0", "gain": 1.0, "offset": 0.0, "unit": "°C", "is_numerical": True}
                ],
                "thresholds": {"ovp_var": "", "ovp_val": 100.0, "ocp_var": "", "ocp_val": 50.0, "otp_var": "temp", "otp_val": 85.0},
                "temp_mosfet_var": "temp",
                "temp_transformer_var": ""
            }
            self.config_data["subsystems"].append(new_sub)
            self.combo_subs.addItem(sub_id)
            self.combo_subs.setCurrentText(sub_id)

    def remove_selected_subsystem(self):
        cur_id = self.combo_subs.currentText()
        if not cur_id:
            return
        
        self.config_data["subsystems"] = [s for s in self.config_data["subsystems"] if s["name"] != cur_id]
        idx = self.combo_subs.currentIndex()
        self.combo_subs.removeItem(idx)

    def on_subsystem_selection_changed(self, sub_id):
        self.rebuild_subsystem_details_panel(sub_id)

    def rebuild_subsystem_details_panel(self, sub_id):
        # Clear container layout
        while self.right_lay.count():
            item = self.right_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        if not sub_id:
            lbl = QLabel("Please add or select a logical subsystem panel on the left.")
            lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl.setStyleSheet("color: #75758a; font-size: 12px;")
            self.right_lay.addWidget(lbl)
            return
            
        sub = next((s for s in self.config_data["subsystems"] if s["name"] == sub_id), None)
        if not sub:
            return
            
        # 1. Names Metadata
        meta_group = QGroupBox("Subsystem Metadata Identity")
        meta_lay = QGridLayout(meta_group)
        
        txt_disp = QLineEdit(sub.get("display_name", sub_id))
        txt_disp.textChanged.connect(lambda text: sub.update({"display_name": text}))
        
        meta_lay.addWidget(QLabel("Display Screen Title:"), 0, 0)
        meta_lay.addWidget(txt_disp, 0, 1)
        self.right_lay.addWidget(meta_group)
        
        # 2. Variables Mapping Table
        var_group = QGroupBox("Dynamic Variables Mapping Schema")
        var_lay = QVBoxLayout(var_group)
        
        self.tbl_vars = QTableWidget(0, 7)
        self.tbl_vars.setHorizontalHeaderLabels(["Name (Key)", "Display Label", "Unit", "Col / Key Index", "Gain (a)", "Offset (b)", "Is Numerical"])
        self.tbl_vars.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        var_lay.addWidget(self.tbl_vars)
        
        # Save changes whenever cell changes
        self.tbl_vars.itemChanged.connect(lambda: self.save_current_variables_to_state(sub))
        
        btn_v_lay = QHBoxLayout()
        btn_add_v = QPushButton("➕ Add Variable")
        btn_add_v.clicked.connect(lambda: self.add_variable_row(sub))
        btn_del_v = QPushButton("➖ Delete Selected")
        btn_del_v.clicked.connect(self.del_variable_row)
        btn_v_lay.addWidget(btn_add_v)
        btn_v_lay.addWidget(btn_del_v)
        btn_v_lay.addStretch()
        var_lay.addLayout(btn_v_lay)
        
        self.right_lay.addWidget(var_group)
        
        # Populate table
        self.tbl_vars.blockSignals(True)
        for v in sub.get("variables", []):
            self.add_variable_row(sub, v)
        self.tbl_vars.blockSignals(False)
        
        # 3. Alarms and Hotspots Targets
        safe_group = QGroupBox("Safety Alarms Boundary limits & Hotspots")
        safe_lay = QGridLayout(safe_group)
        
        thresholds = sub.setdefault("thresholds", {"ovp_var": "", "ovp_val": 100.0, "ocp_var": "", "ocp_val": 50.0, "otp_var": "", "otp_val": 85.0})
        
        # Combo boxes for targets
        combo_ovp = QComboBox()
        combo_ocp = QComboBox()
        combo_otp = QComboBox()
        combo_t1 = QComboBox()
        combo_t2 = QComboBox()
        
        for c in [combo_ovp, combo_ocp, combo_otp, combo_t1, combo_t2]:
            c.addItem("None (Disabled)", "")
            for v in sub["variables"]:
                c.addItem(v["name"], v["name"])
                
        # Set current choices
        combo_ovp.setCurrentText(thresholds.get("ovp_var", ""))
        combo_ocp.setCurrentText(thresholds.get("ocp_var", ""))
        combo_otp.setCurrentText(thresholds.get("otp_var", ""))
        combo_t1.setCurrentText(sub.get("temp_mosfet_var", ""))
        combo_t2.setCurrentText(sub.get("temp_transformer_var", ""))
        
        # Threshold spinboxes
        spin_ovp = QDoubleSpinBox()
        spin_ovp.setRange(0, 10000)
        spin_ovp.setValue(thresholds.get("ovp_val", 100.0))
        
        spin_ocp = QDoubleSpinBox()
        spin_ocp.setRange(0, 10000)
        spin_ocp.setValue(thresholds.get("ocp_val", 50.0))
        
        spin_otp = QDoubleSpinBox()
        spin_otp.setRange(0, 500)
        spin_otp.setValue(thresholds.get("otp_val", 85.0))
        
        # Hook up change callbacks
        combo_ovp.currentTextChanged.connect(lambda t: thresholds.update({"ovp_var": t}))
        spin_ovp.valueChanged.connect(lambda val: thresholds.update({"ovp_val": val}))
        
        combo_ocp.currentTextChanged.connect(lambda t: thresholds.update({"ocp_var": t}))
        spin_ocp.valueChanged.connect(lambda val: thresholds.update({"ocp_val": val}))
        
        combo_otp.currentTextChanged.connect(lambda t: thresholds.update({"otp_var": t}))
        spin_otp.valueChanged.connect(lambda val: thresholds.update({"otp_val": val}))
        
        combo_t1.currentTextChanged.connect(lambda t: sub.update({"temp_mosfet_var": t}))
        combo_t2.currentTextChanged.connect(lambda t: sub.update({"temp_transformer_var": t}))
        
        # Layout arrangement
        safe_lay.addWidget(QLabel("Over-Voltage (OVP) Variable:"), 0, 0)
        safe_lay.addWidget(combo_ovp, 0, 1)
        safe_lay.addWidget(QLabel("Limit Value:"), 0, 2)
        safe_lay.addWidget(spin_ovp, 0, 3)
        
        safe_lay.addWidget(QLabel("Over-Current (OCP) Variable:"), 1, 0)
        safe_lay.addWidget(combo_ocp, 1, 1)
        safe_lay.addWidget(QLabel("Limit Value:"), 1, 2)
        safe_lay.addWidget(spin_ocp, 1, 3)
        
        safe_lay.addWidget(QLabel("Over-Temp (OTP) Variable:"), 2, 0)
        safe_lay.addWidget(combo_otp, 2, 1)
        safe_lay.addWidget(QLabel("Limit Value:"), 2, 2)
        safe_lay.addWidget(spin_otp, 2, 3)
        
        safe_lay.addWidget(QLabel("MOSFET Hotspot (T1) Variable:"), 3, 0)
        safe_lay.addWidget(combo_t1, 3, 1)
        safe_lay.addWidget(QLabel("Transformer Hotspot (T2):"), 3, 2)
        safe_lay.addWidget(combo_t2, 3, 3)
        
        self.right_lay.addWidget(safe_group)
        self.right_lay.addStretch()

    def add_variable_row(self, sub, var_data=None):
        row = self.tbl_vars.rowCount()
        self.tbl_vars.insertRow(row)
        
        name = var_data["name"] if var_data else f"var_{row}"
        disp = var_data["display_name"] if var_data else f"Var {row}"
        unit = var_data["unit"] if var_data else "V"
        col_idx = str(var_data.get("column_index", row)) if var_data else str(row)
        gain = float(var_data.get("gain", 1.0)) if var_data else 1.0
        offset = float(var_data.get("offset", 0.0)) if var_data else 0.0
        is_num = bool(var_data.get("is_numerical", True)) if var_data else True
        
        self.tbl_vars.setItem(row, 0, QTableWidgetItem(name))
        self.tbl_vars.setItem(row, 1, QTableWidgetItem(disp))
        self.tbl_vars.setItem(row, 2, QTableWidgetItem(unit))
        self.tbl_vars.setItem(row, 3, QTableWidgetItem(col_idx))
        self.tbl_vars.setItem(row, 4, QTableWidgetItem(str(gain)))
        self.tbl_vars.setItem(row, 5, QTableWidgetItem(str(offset)))
        
        chk = QCheckBox()
        chk.setChecked(is_num)
        chk.stateChanged.connect(lambda: self.save_current_variables_to_state(sub))
        self.tbl_vars.setCellWidget(row, 6, chk)

    def del_variable_row(self):
        cur = self.tbl_vars.currentRow()
        if cur >= 0:
            self.tbl_vars.removeRow(cur)

    def save_current_variables_to_state(self, sub):
        sub["variables"] = []
        for row in range(self.tbl_vars.rowCount()):
            it0 = self.tbl_vars.item(row, 0)
            it1 = self.tbl_vars.item(row, 1)
            it2 = self.tbl_vars.item(row, 2)
            it3 = self.tbl_vars.item(row, 3)
            it4 = self.tbl_vars.item(row, 4)
            it5 = self.tbl_vars.item(row, 5)
            chk = self.tbl_vars.cellWidget(row, 6)
            
            if it0 and it0.text().strip():
                try:
                    gain = float(it4.text()) if it4 else 1.0
                except:
                    gain = 1.0
                try:
                    offset = float(it5.text()) if it5 else 0.0
                except:
                    offset = 0.0
                
                sub["variables"].append({
                    "name": it0.text().strip(),
                    "display_name": it1.text().strip() if it1 else it0.text().strip(),
                    "unit": it2.text().strip() if it2 else "",
                    "column_index": it3.text().strip() if it3 else str(row),
                    "gain": gain,
                    "offset": offset,
                    "is_numerical": chk.isChecked() if chk else True
                })

    def setup_routing_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_routing")
        lay = QVBoxLayout(tab)
        
        desc = QLabel("🚦 Multi-Port Data Packet Split & Splitting Routing Rules Layout:")
        desc.setStyleSheet("font-weight: bold; color: #ffffff;")
        lay.addWidget(desc)
        
        self.tbl_rules = QTableWidget(0, 4)
        self.tbl_rules.setHorizontalHeaderLabels(["Active Port", "Format Type", "Pattern Signature / Key", "Target Subsystem"])
        self.tbl_rules.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        lay.addWidget(self.tbl_rules)
        
        btn_lay = QHBoxLayout()
        btn_add = QPushButton("➕ Add Rule Row")
        btn_add.clicked.connect(self.add_rule_row)
        btn_del = QPushButton("➖ Remove Selected Rule")
        btn_del.clicked.connect(self.del_rule_row)
        btn_lay.addWidget(btn_add)
        btn_lay.addWidget(btn_del)
        btn_lay.addStretch()
        lay.addLayout(btn_lay)
        
        self.tabs.addTab(tab, "🚦 Packet Routing Rules")

    def add_rule_row(self, port="", r_type="COLUMNS", pattern="", target=""):
        row = self.tbl_rules.rowCount()
        self.tbl_rules.insertRow(row)
        
        # Port name input
        self.tbl_rules.setItem(row, 0, QTableWidgetItem(port))
        
        # Rule Type dropdown
        combo_type = QComboBox()
        combo_type.addItems(["COLUMNS", "PREFIX", "JSON"])
        combo_type.setCurrentText(r_type)
        self.tbl_rules.setCellWidget(row, 1, combo_type)
        
        # Pattern field
        self.tbl_rules.setItem(row, 2, QTableWidgetItem(pattern))
        
        # Target Subsystem combo
        combo_target = QComboBox()
        for s in self.config_data.get("subsystems", []):
            combo_target.addItem(s["name"], s["name"])
        combo_target.setCurrentText(target)
        self.tbl_rules.setCellWidget(row, 3, combo_target)

    def del_rule_row(self):
        cur = self.tbl_rules.currentRow()
        if cur >= 0:
            self.tbl_rules.removeRow(cur)

    def setup_formulas_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_formulas")
        lay = QVBoxLayout(tab)
        
        desc = QLabel("🔗 Organic Dynamic Data Linking cross-subsystem computed formulas:")
        desc.setStyleSheet("font-weight: bold; color: #ffffff;")
        lay.addWidget(desc)
        
        self.tbl_links = QTableWidget(0, 3)
        self.tbl_links.setHorizontalHeaderLabels(["Target Subsystem", "Target Computed Var Name", "Arithmetic Formula Expression"])
        self.tbl_links.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        lay.addWidget(self.tbl_links)
        
        btn_lay = QHBoxLayout()
        btn_add = QPushButton("➕ Add Formula Link")
        btn_add.clicked.connect(self.add_formula_row)
        btn_del = QPushButton("➖ Remove Link")
        btn_del.clicked.connect(self.del_formula_row)
        btn_lay.addWidget(btn_add)
        btn_lay.addWidget(btn_del)
        btn_lay.addStretch()
        lay.addLayout(btn_lay)
        
        self.tabs.addTab(tab, "🔗 Dynamic Data Links")

    def add_formula_row(self, target_sub="", target_var="", formula=""):
        row = self.tbl_links.rowCount()
        self.tbl_links.insertRow(row)
        
        # Target sub selection dropdown
        combo_sub = QComboBox()
        for s in self.config_data.get("subsystems", []):
            combo_sub.addItem(s["name"], s["name"])
        combo_sub.setCurrentText(target_sub)
        self.tbl_links.setCellWidget(row, 0, combo_sub)
        
        # Computed Variable output field
        self.tbl_links.setItem(row, 1, QTableWidgetItem(target_var))
        
        # Formula expression
        self.tbl_links.setItem(row, 2, QTableWidgetItem(formula))

    def del_formula_row(self):
        cur = self.tbl_links.currentRow()
        if cur >= 0:
            self.tbl_links.removeRow(cur)

    def setup_plugins_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_plugins")
        lay = QVBoxLayout(tab)
        lay.setContentsMargins(20, 20, 20, 20)
        lay.setSpacing(15)
        
        desc = QLabel("🧩 기능 모듈 및 확장 플러그인 관리 (Plugins Manager)")
        desc.setStyleSheet("font-weight: bold; color: #38bdf8; font-size: 14px;")
        lay.addWidget(desc)
        
        hint = QLabel(
            "대시보드의 기능을 확장하는 플러그인을 활성화하거나 비활성화할 수 있습니다.<br/>"
            "새로운 파이썬 플러그인(.py) 파일을 시스템에 추가로 설치하여 커스텀 연동 뷰 및 특수 제어 패널을 생성할 수도 있습니다."
        )
        hint.setStyleSheet("font-size: 11px; color: #8e94a6; line-height: 1.5;")
        lay.addWidget(hint)
        
        # Separator line
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        line.setStyleSheet("background-color: #272a38; max-height: 1px;")
        lay.addWidget(line)
        
        # Checklist container inside a Scroll Area for elegance
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("background-color: #050507; border: 1px solid #272a38; border-radius: 6px;")
        
        scroll_widget = QWidget()
        scroll_widget.setObjectName("scroll_widget")
        scroll_widget.setStyleSheet("background-color: #050507;")
        
        self.plugins_checkbox_lay = QVBoxLayout(scroll_widget)
        self.plugins_checkbox_lay.setContentsMargins(15, 15, 15, 15)
        self.plugins_checkbox_lay.setSpacing(12)
        
        scroll.setWidget(scroll_widget)
        lay.addWidget(scroll, 1)
        
        self.tabs.addTab(tab, "🧩 플러그인 관리")

    def validate_and_save(self):
        # 1. Ports
        ports_list = []
        for r in range(self.tbl_ports.rowCount()):
            port_name = self.tbl_ports.item(r, 0).text().strip() if self.tbl_ports.item(r, 0) else ""
            combo_baud = self.tbl_ports.cellWidget(r, 1)
            if port_name and combo_baud:
                ports_list.append({"port": port_name, "baudrate": int(combo_baud.currentText())})
        self.config_data["ports"] = ports_list
        
        # 2. Routing Rules
        rules_list = []
        for r in range(self.tbl_rules.rowCount()):
            port_name = self.tbl_rules.item(r, 0).text().strip() if self.tbl_rules.item(r, 0) else ""
            combo_type = self.tbl_rules.cellWidget(r, 1)
            pat_item = self.tbl_rules.item(r, 2)
            combo_target = self.tbl_rules.cellWidget(r, 3)
            
            if port_name and combo_type and combo_target:
                rules_list.append({
                    "port": port_name,
                    "type": combo_type.currentText(),
                    "pattern": pat_item.text().strip() if pat_item else "",
                    "target": combo_target.currentText()
                })
        self.config_data["routing_rules"] = rules_list
        
        # 3. Data links
        links_list = []
        for r in range(self.tbl_links.rowCount()):
            combo_sub = self.tbl_links.cellWidget(r, 0)
            var_item = self.tbl_links.item(r, 1)
            form_item = self.tbl_links.item(r, 2)
            
            if combo_sub and var_item and form_item and var_item.text().strip() and form_item.text().strip():
                links_list.append({
                    "target_sub": combo_sub.currentText(),
                    "target_var": var_item.text().strip(),
                    "formula": form_item.text().strip()
                })
        self.config_data["linking_formulas"] = links_list
        
# ======================================================================
# TELEMETRY CONFIGURATION PRESETS (기초 프리셋 템플릿)
# ======================================================================
DAB_CONVERTER_PRESET = {
    "ports": [
        {"port": "COM3", "baudrate": 115200}
    ],
    "subsystems": [
        {
            "name": "DabNode",
            "display_name": "⚡ DAB Converter Node",
            "variables": [
                {"name": "vin", "display_name": "Input Voltage", "column_index": "0", "gain": 1.0, "offset": 0.0, "unit": "V", "is_numerical": True},
                {"name": "iin", "display_name": "Input Current", "column_index": "1", "gain": 1.0, "offset": 0.0, "unit": "A", "is_numerical": True},
                {"name": "vout", "display_name": "Output Voltage", "column_index": "2", "gain": 1.0, "offset": 0.0, "unit": "V", "is_numerical": True},
                {"name": "iout", "display_name": "Output Current", "column_index": "3", "gain": 1.0, "offset": 0.0, "unit": "A", "is_numerical": True},
                {"name": "t_mos", "display_name": "MOSFET Temp", "column_index": "4", "gain": 1.0, "offset": 0.0, "unit": "°C", "is_numerical": True},
                {"name": "t_trans", "display_name": "Transformer Temp", "column_index": "5", "gain": 1.0, "offset": 0.0, "unit": "°C", "is_numerical": True},
                {"name": "pin", "display_name": "Pin Computed", "column_index": "-1", "gain": 1.0, "offset": 0.0, "unit": "W", "is_numerical": True},
                {"name": "pout", "display_name": "Pout Computed", "column_index": "-1", "gain": 1.0, "offset": 0.0, "unit": "W", "is_numerical": True},
                {"name": "eff", "display_name": "Efficiency", "column_index": "-1", "gain": 1.0, "offset": 0.0, "unit": "%", "is_numerical": True}
            ],
            "thresholds": {
                "ovp_var": "vin",
                "ovp_val": 400.0,
                "ocp_var": "iin",
                "ocp_val": 15.0,
                "otp_var": "t_mos",
                "otp_val": 85.0,
                "standby_var": "",
                "standby_val": 1.0
            },
            "temp_mosfet_var": "t_mos",
            "temp_transformer_var": "t_trans"
        }
    ],
    "routing_rules": [
        {"port": "COM3", "type": "PREFIX", "pattern": "DAB", "target": "DabNode"}
    ],
    "linking_formulas": [
        {"target_sub": "DabNode", "target_var": "pin", "formula": "[DabNode].vin * [DabNode].iin"},
        {"target_sub": "DabNode", "target_var": "pout", "formula": "[DabNode].vout * [DabNode].iout"},
        {"target_sub": "DabNode", "target_var": "eff", "formula": "([DabNode].pout / ([DabNode].pin + 0.001)) * 100"}
    ],
    "active_plugins": [
        "telemetry_cards",
        "trend_charts",
        "service_console",
        "parameter_manager",
        "mcu_terminal"
    ]
}

EV_BMS_PRESET = {
    "ports": [
        {"port": "COM4", "baudrate": 115200}
    ],
    "subsystems": [
        {
            "name": "BmsNode",
            "display_name": "🔋 EV Battery BMS Node",
            "variables": [
                {"name": "vcell_min", "display_name": "Min Cell Volt", "column_index": "0", "gain": 1.0, "offset": 0.0, "unit": "V", "is_numerical": True},
                {"name": "vcell_max", "display_name": "Max Cell Volt", "column_index": "1", "gain": 1.0, "offset": 0.0, "unit": "V", "is_numerical": True},
                {"name": "pack_current", "display_name": "Pack Current", "column_index": "2", "gain": 1.0, "offset": 0.0, "unit": "A", "is_numerical": True},
                {"name": "soc", "display_name": "SOC State", "column_index": "3", "gain": 1.0, "offset": 0.0, "unit": "%", "is_numerical": True},
                {"name": "temp_max", "display_name": "Max Cell Temp", "column_index": "4", "gain": 1.0, "offset": 0.0, "unit": "°C", "is_numerical": True}
            ],
            "thresholds": {
                "ovp_var": "vcell_max",
                "ovp_val": 4.25,
                "ocp_var": "pack_current",
                "ocp_val": 200.0,
                "otp_var": "temp_max",
                "otp_val": 60.0,
                "standby_var": "",
                "standby_val": 1.0
            },
            "temp_mosfet_var": "",
            "temp_transformer_var": ""
        }
    ],
    "routing_rules": [
        {"port": "COM4", "type": "PREFIX", "pattern": "BMS", "target": "BmsNode"}
    ],
    "linking_formulas": [],
    "active_plugins": [
        "telemetry_cards",
        "trend_charts",
        "service_console",
        "parameter_manager",
        "mcu_terminal"
    ]
}

ARDUINO_PLOTTER_PRESET = {
    "ports": [
        {"port": "COM6", "baudrate": 115200}
    ],
    "subsystems": [
        {
            "name": "ArduinoNode",
            "display_name": "🔌 Arduino General Plotter",
            "variables": [
                {"name": "val1", "display_name": "Sensor Value 1", "column_index": "0", "gain": 1.0, "offset": 0.0, "unit": "", "is_numerical": True},
                {"name": "val2", "display_name": "Sensor Value 2", "column_index": "1", "gain": 1.0, "offset": 0.0, "unit": "", "is_numerical": True},
                {"name": "val3", "display_name": "Sensor Value 3", "column_index": "2", "gain": 1.0, "offset": 0.0, "unit": "", "is_numerical": True}
            ],
            "thresholds": {
                "ovp_var": "",
                "ovp_val": 1000.0,
                "ocp_var": "",
                "ocp_val": 1000.0,
                "otp_var": "",
                "otp_val": 100.0,
                "standby_var": "",
                "standby_val": 1.0
            },
            "temp_mosfet_var": "",
            "temp_transformer_var": ""
        }
    ],
    "routing_rules": [
        {"port": "COM6", "type": "PREFIX", "pattern": "ARD", "target": "ArduinoNode"}
    ],
    "linking_formulas": [],
    "active_plugins": [
        "telemetry_cards",
        "trend_charts",
        "service_console",
        "parameter_manager",
        "mcu_terminal"
    ]
}

class FirstTimeWelcomeDialog(QDialog):
    """
    A premium dark-themed Korean Welcome Dialog for first-time users.
    Guides them to load telemetry presets or custom configure workspaces.
    """
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("👋 환영합니다! - Universal MCU Telemetry Monitor")
        self.resize(550, 420)
        self.setWindowFlags(self.windowFlags() & ~Qt.WindowType.WindowContextHelpButtonHint)
        self.init_ui()

    def init_ui(self):
        # Apply dark premium style matching dashboard styling
        self.setStyleSheet("""
            QDialog {
                background-color: #0f1015;
                border: 1px solid #272a38;
                border-radius: 8px;
            }
            QLabel {
                color: #e2e8f0;
                font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            }
            QComboBox {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 4px;
                padding: 6px;
                color: #38bdf8;
                font-weight: bold;
            }
            QComboBox QAbstractItemView {
                background-color: #0f1015;
                border: 1px solid #272a38;
                selection-background-color: #38bdf8;
                selection-color: #0f1015;
                color: #ffffff;
            }
            QPushButton {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
                color: #38bdf8;
                font-weight: bold;
                padding: 10px 18px;
                font-size: 11px;
            }
            QPushButton:hover {
                background-color: #222530;
                border-color: #38bdf8;
            }
            QPushButton#btn_primary {
                background-color: #2563eb;
                border-color: #3b82f6;
                color: white;
            }
            QPushButton#btn_primary:hover {
                background-color: #1d4ed8;
                border-color: #2563eb;
            }
            QPushButton#btn_success {
                background-color: #059669;
                border-color: #10b981;
                color: white;
            }
            QPushButton#btn_success:hover {
                background-color: #047857;
                border-color: #059669;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(25, 25, 25, 25)
        layout.setSpacing(15)

        # Header Title
        title_lbl = QLabel("👋 처음 방문해 주셔서 감사합니다!")
        title_lbl.setStyleSheet("font-size: 18px; font-weight: bold; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # Divider
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        line.setStyleSheet("background-color: #272a38; max-height: 1px;")
        layout.addWidget(line)

        # Description
        desc_lbl = QLabel(
            "현재 구성된 MCU 모니터링 프로필이 없습니다.<br/>"
            "프로그램 동작 원리를 빠르게 파악할 수 있도록 아래의 <b>기초 텔레메트리 프리셋</b>을 제공합니다.<br/>"
            "원하는 템플릿을 선택하고 시작 버튼을 누르면 즉시 시뮬레이션 대시보드가 준비됩니다."
        )
        desc_lbl.setWordWrap(True)
        desc_lbl.setStyleSheet("font-size: 12px; line-height: 1.5; color: #a0a5b5;")
        layout.addWidget(desc_lbl)

        # Dropdown Group
        dropdown_lay = QVBoxLayout()
        dropdown_lay.setSpacing(5)
        dropdown_lay.addWidget(QLabel("초기 텔레메트리 프리셋 선택:"))
        
        self.combo_presets = QComboBox()
        self.combo_presets.addItem("⚡ DAB Converter (듀얼 액티브 브릿지 컨버터 프리셋)", "DAB")
        self.combo_presets.addItem("🔋 EV Battery BMS (전기차 배터리 제어 프리셋)", "BMS")
        self.combo_presets.addItem("🔌 Arduino General Plotter (범용 센서 플로터 프리셋)", "ARDUINO")
        self.combo_presets.addItem("🏠 기본 ESS 모니터링 시스템 (예제 프로필)", "ESS")
        self.combo_presets.setView(QListView())
        self.combo_presets.currentIndexChanged.connect(self.update_summary)
        dropdown_lay.addWidget(self.combo_presets)
        
        layout.addLayout(dropdown_lay)

        # Summary box
        self.summary_lbl = QLabel()
        self.summary_lbl.setStyleSheet("background-color: #12131a; border: 1px solid #272a38; border-radius: 4px; padding: 12px; font-size: 11px; line-height: 1.6; color: #8e94a6;")
        self.summary_lbl.setWordWrap(True)
        layout.addWidget(self.summary_lbl)
        
        layout.addStretch()

        # Button Layout
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(10)

        self.btn_load_preset = QPushButton("🚀 선택한 프리셋으로 시작")
        self.btn_load_preset.setObjectName("btn_primary")
        
        self.btn_go_settings = QPushButton("⚙️ 맞춤형 설정 창으로 이동")
        self.btn_go_settings.setObjectName("btn_success")
        
        self.btn_close = QPushButton("닫기")
        
        btn_layout.addWidget(self.btn_load_preset)
        btn_layout.addWidget(self.btn_go_settings)
        btn_layout.addWidget(self.btn_close)
        
        layout.addLayout(btn_layout)

        # Connect actions
        self.btn_load_preset.clicked.connect(self.accept_load_preset)
        self.btn_go_settings.clicked.connect(self.accept_go_settings)
        self.btn_close.clicked.connect(self.reject)

        self.choice = None
        self.selected_preset = None
        
        self.update_summary()

    def update_summary(self):
        preset_type = self.combo_presets.currentData()
        if preset_type == "DAB":
            self.summary_lbl.setText(
                "<b>⚡ DAB Converter 프리셋</b><br/>"
                "듀얼 액티브 브릿지 컨버터 전압/전류 및 실시간 효율([DabNode].eff) 수식 모니터링을 즉시 구동합니다."
            )
        elif preset_type == "BMS":
            self.summary_lbl.setText(
                "<b>🔋 EV Battery BMS 프리셋</b><br/>"
                "최저/최고 셀 전압, 팩 전류 모니터링 및 OVP/OCP/OTP 안전 한계 임계 알람 상태를 모니터링합니다."
            )
        elif preset_type == "ARDUINO":
            self.summary_lbl.setText(
                "<b>🔌 Arduino 범용 센서 플로터 프리셋</b><br/>"
                "아두이노 MCU로부터 직렬 수신되는 val1, val2, val3를 실시간 플로터에 즉시 매핑하고 확인합니다."
            )
        elif preset_type == "ESS":
            self.summary_lbl.setText(
                "<b>🏠 ESS 에너지 저장 장치 (기본 예제)</b><br/>"
                "서브시스템 2종(EngineNode, BatteryNode)과 3종 전력 계산 수식을 내장한 종합 예제 프로필입니다."
            )

    def accept_load_preset(self):
        self.choice = "LOAD_PRESET"
        self.selected_preset = self.combo_presets.currentData()
        self.accept()

    def accept_go_settings(self):
        self.choice = "GO_SETTINGS"
        self.accept()


class PresetSelectionDialog(QDialog):
    """
    A premium dark-themed dialog for selecting and loading telemetry presets.
    Supports overwriting or appending/combining presets.
    """
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("📋 기초 프리셋 템플릿 로드/조합")
        self.resize(500, 320)
        self.setWindowFlags(self.windowFlags() & ~Qt.WindowType.WindowContextHelpButtonHint)
        self.init_ui()

    def init_ui(self):
        self.setStyleSheet("""
            QDialog {
                background-color: #0f1015;
                border: 1px solid #272a38;
                border-radius: 8px;
            }
            QLabel {
                color: #e2e8f0;
                font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            }
            QComboBox {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 4px;
                padding: 6px;
                color: #ffffff;
                font-weight: bold;
            }
            QComboBox QAbstractItemView {
                background-color: #0f1015;
                border: 1px solid #272a38;
                selection-background-color: #38bdf8;
                selection-color: #0f1015;
                color: #ffffff;
            }
            QRadioButton {
                color: #a0a5b5;
                font-weight: bold;
                spacing: 6px;
            }
            QRadioButton::indicator {
                width: 14px;
                height: 14px;
                border: 1px solid #272a38;
                border-radius: 7px;
                background-color: #0b0b0d;
            }
            QRadioButton::indicator:checked {
                background-color: #38bdf8;
                border-color: #38bdf8;
            }
            QPushButton {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
                color: #38bdf8;
                font-weight: bold;
                padding: 8px 16px;
            }
            QPushButton:hover {
                background-color: #222530;
                border-color: #38bdf8;
            }
            QPushButton#btn_primary {
                background-color: #059669;
                border-color: #10b981;
                color: white;
            }
            QPushButton#btn_primary:hover {
                background-color: #047857;
                border-color: #059669;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        title_lbl = QLabel("📋 텔레메트리 프리셋 로드/조합")
        title_lbl.setStyleSheet("font-size: 16px; font-weight: bold; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # Combo selection
        layout.addWidget(QLabel("기초 프리셋 템플릿 선택:"))
        self.combo_presets = QComboBox()
        self.combo_presets.addItem("⚡ DAB Converter (듀얼 액티브 브릿지 컨버터)", "DAB")
        self.combo_presets.addItem("🔋 EV Battery BMS (전기차 배터리 제어)", "BMS")
        self.combo_presets.addItem("🔌 Arduino General Plotter (간단한 센서 플로터)", "ARDUINO")
        self.combo_presets.addItem("🏠 기본 ESS 에너지 저장 장치 (예제 프로필)", "ESS")
        self.combo_presets.setView(QListView())
        self.combo_presets.currentIndexChanged.connect(self.update_summary)
        layout.addWidget(self.combo_presets)

        # Summary box
        self.summary_lbl = QLabel()
        self.summary_lbl.setStyleSheet("background-color: #12131a; border: 1px solid #272a38; border-radius: 4px; padding: 10px; font-size: 11px; line-height: 1.5; color: #8e94a6;")
        self.summary_lbl.setWordWrap(True)
        layout.addWidget(self.summary_lbl)

        # Mode Selection
        layout.addWidget(QLabel("적용 방식 선택:"))
        mode_layout = QHBoxLayout()
        self.radio_combine = QRadioButton("기존 구성에 추가 및 조합 (Combine/Append)")
        self.radio_combine.setChecked(True)
        self.radio_overwrite = QRadioButton("기존 구성 전체 덮어쓰기 (Overwrite)")
        
        self.bg = QButtonGroup(self)
        self.bg.addButton(self.radio_combine)
        self.bg.addButton(self.radio_overwrite)
        
        mode_layout.addWidget(self.radio_combine)
        mode_layout.addWidget(self.radio_overwrite)
        layout.addLayout(mode_layout)

        layout.addStretch()

        # Action Buttons
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        self.btn_apply = QPushButton("✅ 프리셋 적용하기")
        self.btn_apply.setObjectName("btn_primary")
        self.btn_cancel = QPushButton("취소")
        
        btn_layout.addWidget(self.btn_apply)
        btn_layout.addWidget(self.btn_cancel)
        layout.addLayout(btn_layout)

        self.btn_apply.clicked.connect(self.accept)
        self.btn_cancel.clicked.connect(self.reject)
        
        self.update_summary()

    def update_summary(self):
        preset_type = self.combo_presets.currentData()
        if preset_type == "DAB":
            self.summary_lbl.setText(
                "<b>⚡ DAB Converter 듀얼 액티브 브릿지 컨버터 프리셋</b><br/>"
                "- 서브시스템: 1개 (DabNode)<br/>"
                "- 텔레메트리 변수: 9개 (입력/출력 전압, 전류, MOSFET/변압기 온도 등)<br/>"
                "- 특물 수식: 입력 전력(Pin), 출력 전력(Pout), 변환 효율(Eff) 실시간 자동 계산"
            )
        elif preset_type == "BMS":
            self.summary_lbl.setText(
                "<b>🔋 EV Battery BMS 전기차 배터리 모니터 프리셋</b><br/>"
                "- 서브시스템: 1개 (BmsNode)<br/>"
                "- 텔레메트리 변수: 5개 (최저/최고 셀 전압, 팩 전류, 최고 온도, SoC 등)<br/>"
                "- 실시간 안전 알림: OVP(셀 최대 전압), OCP(팩 전류), OTP(최고 온도) 임계 경보 작동"
            )
        elif preset_type == "ARDUINO":
            self.summary_lbl.setText(
                "<b>🔌 Arduino General 범용 시리얼 플로터 프리셋</b><br/>"
                "- 서브시스템: 1개 (ArduinoNode)<br/>"
                "- 텔레메트리 변수: 3개 (Sensor val1, val2, val3)<br/>"
                "- 간편 통합: 복잡한 수식 없이 원격 아두이노 장치 로깅 파싱 즉시 가동"
            )
        elif preset_type == "ESS":
            self.summary_lbl.setText(
                "<b>🏠 ESS 에너지 저장 장치 (기본 예제 프로필)</b><br/>"
                "- 서브시스템: 2개 (EngineNode, BatteryNode)<br/>"
                "- 텔레메트리 변수: 총 12개 이상 (상세 버스 전력, MOSFET 온도 등)<br/>"
                "- 복합 연산: 전력 공식 3종 내장 및 다채널 시뮬레이션 환경 구축"
            )


class DashboardWindow(QMainWindow):
    """
    Succinct Main GUI Frame for the Universal MCU Monitoring system.
    Starting from a blank slate, this orchestrates ConfigManager profiles, 
    MultiPort Background threads, parses DataRouter packets, and houses modular 
    plugins inside customized nested QDockWidgets dynamically.
    """
    def __init__(self):
        super().__init__()
        self.setWindowTitle("📦 Universal General-Purpose MCU Telemetry Monitor Console")
        self.resize(1600, 950)
        self.setDockNestingEnabled(True)
        
        # GUI Global state flags
        self.plots_paused = False
        self.plot_auto_scale = True
        
        # Initialize Core Manager layers
        self.config_manager = ConfigManager()
        self.serial_manager = MultiPortSerialManager()
        self.data_router = DataRouter()
        self.plugin_manager = PluginManager(self)
        
        # Load local persistently saved profile
        self.config_data = self.config_manager.load_config()
        self.data_router.set_config(self.config_data)
        
        # Central Main Stacked Widget
        from PyQt6.QtWidgets import QStackedWidget
        self.main_stack = QStackedWidget()
        self.setCentralWidget(self.main_stack)
        
        # Stack 0: Central Tab Widget containing Waveforms and Grid Overview
        self.central_tab_widget = QTabWidget()
        self.central_tab_widget.setObjectName("central_tab_widget")
        self.main_stack.addWidget(self.central_tab_widget)
        
        # Stack 1: Full-Screen Workspace Setup Tab Widget
        self.setup_tab = WorkspaceSetupTab(self)
        self.main_stack.addWidget(self.setup_tab)
        
        # Initialize Visual GUI
        self.init_ui()
        self.apply_premium_dark_styling()
        
        # Hook infrastructure slots
        self.data_router.error_logged.connect(self.log_to_diagnostic)
        self.serial_manager.raw_packet_received.connect(self.data_router.route_packet)
        
        # Dynamically discover plugins and load
        self.plugin_manager.discover_plugins()
        self.load_plugins_from_profile()
        
        # Refresh and automatically launch active ports listening threads
        self.restart_serial_handlers_from_profile()
        
        # Diagnostic print
        self.log_to_diagnostic("INFO: Dashboard System initialisation completed successfully.")
        
        # Blank state prompt
        if not self.config_data.get("subsystems", []):
            QTimer.singleShot(500, self.prompt_workspace_wizard)

    def init_ui(self):
        # 1. Premium Ribbon controls bar at the top of the QMainWindow
        self.init_ribbon_bar()
        self.setMenuWidget(self.ribbon_bar)
        
    def init_ribbon_bar(self):
        self.ribbon_bar = QTabWidget()
        self.ribbon_bar.setFixedHeight(125)
        self.ribbon_bar.setObjectName("RibbonBar")
        
        def create_ribbon_group(title):
            group_frame = QFrame()
            group_frame.setStyleSheet("""
                QFrame { background-color: #121216; border: 1px solid #23232e; border-radius: 6px; }
            """)
            layout = QVBoxLayout(group_frame)
            layout.setContentsMargins(8, 4, 8, 4)
            layout.setSpacing(2)
            
            lbl_title = QLabel(title)
            lbl_title.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl_title.setStyleSheet("font-size: 9px; color: #75758a; font-weight: bold; border: none; background: transparent;")
            
            content_widget = QWidget()
            content_widget.setStyleSheet("border: none; background: transparent;")
            content_layout = QGridLayout(content_widget)
            content_layout.setContentsMargins(0, 0, 0, 0)
            content_layout.setSpacing(6)
            
            layout.addWidget(content_widget, 1)
            layout.addWidget(lbl_title)
            return group_frame, content_layout

        # ----------------------------------------------------
        # TAB 1: HOME (🏠 홈 & 연결)
        # ----------------------------------------------------
        tab_home = QWidget()
        tab_home_layout = QHBoxLayout(tab_home)
        tab_home_layout.setContentsMargins(6, 6, 6, 6)
        tab_home_layout.setSpacing(10)
        
        # Group A: Active Ports checklists
        frm_ports, lay_ports = create_ribbon_group("🔌 Concurrently Connected USB COM Ports")
        self.ports_checkbox_widget = QWidget()
        self.ports_checkbox_lay = QHBoxLayout(self.ports_checkbox_widget)
        self.ports_checkbox_lay.setContentsMargins(0,0,0,0)
        self.ports_checkbox_lay.setSpacing(8)
        lay_ports.addWidget(self.ports_checkbox_widget, 0, 0, 1, 2)
        
        btn_refresh_ports = QPushButton("🔄 Auto Scan Ports")
        btn_refresh_ports.clicked.connect(self.scan_physical_com_ports)
        lay_ports.addWidget(btn_refresh_ports, 1, 0)
        tab_home_layout.addWidget(frm_ports)
        
        # Group B: Profile config wizards & resets
        frm_spec, lay_spec = create_ribbon_group("⚙️ Profile Config & Layout Reset")
        
        btn_reset_docks = QPushButton("🎛️ Standard Docks Layout reset")
        btn_reset_docks.clicked.connect(self.reset_dock_layout)
        lay_spec.addWidget(btn_reset_docks, 0, 0)
        
        btn_save_profile = QPushButton("💾 Save Profile As...")
        btn_save_profile.clicked.connect(self.save_profile_as)
        lay_spec.addWidget(btn_save_profile, 0, 1)
        
        btn_load_profile = QPushButton("📂 Load Profile...")
        btn_load_profile.clicked.connect(self.load_profile)
        lay_spec.addWidget(btn_load_profile, 1, 1)
        
        tab_home_layout.addWidget(frm_spec)
        
        # Group C: Scopes & Graphs control
        frm_scop, lay_scop = create_ribbon_group("📊 Scopes Waveforms Control")
        
        self.btn_pause = QPushButton("⏸️ Pause Plots")
        self.btn_pause.setCheckable(True)
        self.btn_pause.clicked.connect(self.toggle_plots_pausing)
        lay_scop.addWidget(self.btn_pause, 0, 0)
        
        self.btn_scale = QPushButton("🔍 Auto Scale Y Limit")
        self.btn_scale.setCheckable(True)
        self.btn_scale.setChecked(True)
        self.btn_scale.clicked.connect(self.toggle_plots_auto_scale)
        lay_scop.addWidget(self.btn_scale, 1, 0)
        tab_home_layout.addWidget(frm_scop)
        
        tab_home_layout.addStretch()
        self.ribbon_bar.addTab(tab_home, "🏠 홈")

        # ----------------------------------------------------
        # TAB 2: SETTINGS TAB (⚙️ 설정 탭)
        # ----------------------------------------------------
        self.tab_settings = QWidget()
        self.tab_settings_layout = QHBoxLayout(self.tab_settings)
        lbl_hint = QLabel("🔧 Configuration options are active in the settings view below. Modify COM ports, subsystems, dynamic equations, and active plugins.")
        lbl_hint.setStyleSheet("font-size: 11px; color: #38bdf8; font-weight: bold; padding-left: 15px;")
        self.tab_settings_layout.addWidget(lbl_hint)
        self.tab_settings_layout.addStretch()
        self.ribbon_bar.addTab(self.tab_settings, "⚙️ 설정")
        
        # Connect ribbon bar index change to toggle central main stack
        self.ribbon_bar.currentChanged.connect(self.on_ribbon_tab_changed)

    def load_plugins_from_profile(self):
        """
        Loads saved active plugins list or toggles all available by default.
        Populates the checklist container inside the full-screen Settings tab workspace.
        """
        if not hasattr(self, "setup_tab") or not hasattr(self.setup_tab, "plugins_checkbox_lay"):
            return
            
        layout = self.setup_tab.plugins_checkbox_lay
        
        # Clear previous items
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        # For each discovered plugin in available lists
        enabled_list = self.config_data.get("active_plugins", None)
        if enabled_list is None:
            enabled_list = list(self.plugin_manager.available_plugins.keys())
            
        for p_id, p_class in self.plugin_manager.available_plugins.items():
            # Get inst name
            inst = p_class(self)
            
            # Create a checkable row widget
            chk_row = QWidget()
            chk_row_lay = QHBoxLayout(chk_row)
            chk_row_lay.setContentsMargins(10, 6, 10, 6)
            chk_row_lay.setSpacing(15)
            
            chk = QCheckBox(f"🧩 {inst.name}")
            chk.setToolTip(inst.description)
            chk.setChecked(p_id in enabled_list)
            chk.setStyleSheet("font-size: 12px; font-weight: bold; color: #38bdf8;")
            chk.stateChanged.connect(lambda state, pid=p_id: self.on_plugin_checkbox_toggled(pid, state == 2))
            
            desc_lbl = QLabel(inst.description)
            desc_lbl.setStyleSheet("font-size: 11px; color: #8e94a6;")
            
            chk_row_lay.addWidget(chk, 0)
            chk_row_lay.addWidget(desc_lbl, 1)
            
            layout.addWidget(chk_row)
            
        # Add dynamic installer button to install external plugins
        btn_install = QPushButton("➕ 새 플러그인 파일(.py) 추가/설치...")
        btn_install.setStyleSheet("background-color: #0284c7; border-color: #0369a1; color: white; padding: 10px; font-size: 11px; font-weight: bold; border-radius: 4px;")
        btn_install.clicked.connect(self.install_custom_plugin)
        
        # Add installer button row
        install_row = QWidget()
        install_row_lay = QHBoxLayout(install_row)
        install_row_lay.setContentsMargins(10, 15, 10, 10)
        install_row_lay.addWidget(btn_install)
        install_row_lay.addStretch()
        
        layout.addWidget(install_row)
        layout.addStretch()
        
        # Load states persistently inside manager
        self.plugin_manager.load_active_plugins_from_config(enabled_list)
        self.sync_central_tabs()

    def install_custom_plugin(self):
        """
        Prompts the user to select a .py file to install as a dynamic extension plugin.
        Copies the file to the user AppData plugins folder and reloads active configurations.
        """
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            "🧩 Dynamic Extension Plugin File (.py) 선택",
            "",
            "Python Files (*.py)"
        )
        if file_path:
            try:
                filename = self.plugin_manager.install_plugin(file_path)
                self.log_to_diagnostic(f"SUCCESS: Custom extension plugin '{filename}' successfully installed.")
                QMessageBox.information(
                    self, 
                    "성공", 
                    f"플러그인이 성공적으로 추가 및 설치되었습니다!\n\n파일명: {filename}\n이제 플러그인 체크박스를 통해 활성화할 수 있습니다."
                )
                self.load_plugins_from_profile()
            except Exception as e:
                self.log_to_diagnostic(f"ERROR: Plugin installation failed: {str(e)}")
                QMessageBox.critical(self, "에러", f"플러그인 설치 실패:\n{str(e)}")

    def on_plugin_checkbox_toggled(self, plugin_id, checked):
        self.plugin_manager.toggle_plugin(plugin_id, checked)
        self.sync_central_tabs()

    def scan_physical_com_ports(self):
        """
        Scans all physical system VCP COM serial ports and draws checkable choices dynamically.
        """
        # Clear dynamic checkboxes
        while self.ports_checkbox_lay.count():
            item = self.ports_checkbox_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        # Fetch physical ports
        import serial.tools.list_ports
        ports = [p.device for p in serial.tools.list_ports.comports()]
        
        # Fetch configured ports from config
        configured_ports = [p["port"] for p in self.config_data.get("ports", [])]
        
        # Merge physical + configured ports dynamically
        all_ports = sorted(list(set(ports + configured_ports)))
        
        if not all_ports:
            lbl = QLabel("No VCP Ports Found.")
            lbl.setStyleSheet("color: #75758a; font-weight: bold;")
            self.ports_checkbox_lay.addWidget(lbl)
            return
            
        for port in all_ports:
            chk = QCheckBox(port)
            is_active = port in self.serial_manager.active_threads
            chk.setChecked(is_active)
            chk.stateChanged.connect(lambda state, p=port: self.toggle_port_listening_state(p, state == 2))
            
            # Style checkbox with subtle green indicator if active
            if is_active:
                chk.setStyleSheet("color: #00ff66; font-weight: bold;")
            self.ports_checkbox_lay.addWidget(chk)

    def toggle_port_listening_state(self, port_name, checked):
        """
        Starts or stops background threads listening on a single port.
        """
        if checked:
            # Find baudrate configuration for port, or default to 115200
            baud = 115200
            for p in self.config_data.get("ports", []):
                if p["port"] == port_name:
                    baud = p["baudrate"]
                    break
            self.serial_manager.connect_port(port_name, baud)
            self.log_to_diagnostic(f"INFO: Commencing VCP listening thread on {port_name} at {baud} baud.")
        else:
            self.serial_manager.disconnect_port(port_name)
            self.log_to_diagnostic(f"INFO: Safely stopped listening thread on {port_name}.")
            
        # Re-scan physical ports to update colors
        QTimer.singleShot(100, self.scan_physical_com_ports)

    def restart_serial_handlers_from_profile(self):
        """
        Restarts listening threads for all ports specified inside the profile.
        """
        # Stop any active VCP serial threads
        self.serial_manager.disconnect_all()
        
        for p in self.config_data.get("ports", []):
            self.serial_manager.connect_port(p["port"], p["baudrate"])
            
        # Scan system to align GUI checkboxes
        self.scan_physical_com_ports()

    def open_profile_wizard(self):
        """
        Switches the top ribbon bar index to settings tab, triggering full-screen view.
        """
        self.ribbon_bar.setCurrentIndex(1)

    def on_ribbon_tab_changed(self, index):
        """
        Toggles between central dashboard plots and the full-screen Workspace Setup Tab
        based on active top-level Ribbon Tab choices (Home vs Settings).
        """
        if index == 1: # Settings Ribbon Tab
            self.main_stack.setCurrentIndex(1)
            # Fetch and synchronize active profile data right before entering editing state
            self.setup_tab.load_configuration_into_ui()
        else: # Home tab
            self.main_stack.setCurrentIndex(0)

    def save_profile_as(self):
        """
        Exports the current workspace profile layout configuration to a user-specified JSON file.
        """
        file_path, _ = QFileDialog.getSaveFileName(
            self,
            "💾 Save Workspace Profile As...",
            "",
            "JSON Files (*.json)"
        )
        if file_path:
            try:
                with open(file_path, 'w', encoding='utf-8') as f:
                    json.dump(self.config_data, f, indent=4)
                self.log_to_diagnostic(f"SUCCESS: Exported workspace profile to {file_path}")
                QMessageBox.information(self, "Success", f"Workspace profile successfully saved to:\n{file_path}")
            except Exception as e:
                self.log_to_diagnostic(f"ERROR: Failed to save workspace profile: {str(e)}")
                QMessageBox.critical(self, "Error", f"Failed to save profile:\n{str(e)}")

    def load_profile(self):
        """
        Imports and dynamically loads a workspace profile configuration from a JSON file.
        """
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            "📂 Load Workspace Profile...",
            "",
            "JSON Files (*.json)"
        )
        if file_path:
            try:
                with open(file_path, 'r', encoding='utf-8') as f:
                    loaded_data = json.load(f)
                
                # Basic schema validation
                required_keys = ["ports", "subsystems", "routing_rules", "linking_formulas"]
                missing = [k for k in required_keys if k not in loaded_data]
                if missing:
                    # Provide default placeholders if some fields are absent
                    for k in missing:
                        loaded_data[k] = []
                
                # Apply new loaded configuration
                self.apply_new_workspace_configuration(loaded_data)
                self.log_to_diagnostic(f"SUCCESS: Loaded workspace profile from {file_path}")
                QMessageBox.information(self, "Success", f"Workspace profile successfully loaded and applied from:\n{file_path}")
            except Exception as e:
                self.log_to_diagnostic(f"ERROR: Failed to load workspace profile: {str(e)}")
                QMessageBox.critical(self, "Error", f"Failed to load profile:\n{str(e)}")

    def prompt_workspace_wizard(self):
        dialog = FirstTimeWelcomeDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            if dialog.choice == "LOAD_PRESET":
                preset_type = dialog.selected_preset
                self.load_preset_by_type(preset_type)
            elif dialog.choice == "GO_SETTINGS":
                self.open_profile_wizard()

    def load_preset_by_type(self, preset_type):
        """
        Loads one of the predefined presets by code name.
        """
        if preset_type == "DAB":
            self.apply_preset(DAB_CONVERTER_PRESET, combine=False)
            QMessageBox.information(self, "성공", "⚡ DAB Converter 프리셋이 성공적으로 로드되었습니다!")
        elif preset_type == "BMS":
            self.apply_preset(EV_BMS_PRESET, combine=False)
            QMessageBox.information(self, "성공", "🔋 EV Battery BMS 프리셋이 성공적으로 로드되었습니다!")
        elif preset_type == "ARDUINO":
            self.apply_preset(ARDUINO_PLOTTER_PRESET, combine=False)
            QMessageBox.information(self, "성공", "🔌 아두이노 범용 센서 플로터 프리셋이 성공적으로 로드되었습니다!")
        elif preset_type == "ESS":
            # Load example_profile.json
            from path_resolver import get_bundle_dir
            import os
            example_path = os.path.join(get_bundle_dir(), "example_profile.json")
            if os.path.exists(example_path):
                try:
                    with open(example_path, 'r', encoding='utf-8') as f:
                        loaded_data = json.load(f)
                    
                    required_keys = ["ports", "subsystems", "routing_rules", "linking_formulas"]
                    for k in required_keys:
                        if k not in loaded_data:
                            loaded_data[k] = []
                    
                    self.apply_new_workspace_configuration(loaded_data)
                    self.log_to_diagnostic(f"SUCCESS: Loaded welcome example profile from {example_path}")
                    QMessageBox.information(self, "성공", "기본 ESS 에너지 저장 장치(예제 프로필)가 성공적으로 로드되었습니다!")
                except Exception as e:
                    self.log_to_diagnostic(f"ERROR: Failed to load welcome example profile: {str(e)}")
                    QMessageBox.critical(self, "에러", f"예제 프로필을 불러오지 못했습니다:\n{str(e)}")
            else:
                QMessageBox.warning(self, "경고", f"예제 프로필 파일을 찾을 수 없습니다:\n{example_path}")

    def apply_preset(self, preset_dict, combine=False):
        """
        Loads a preset into self.config_data.
        If combine is True, appends subsystems, ports, routing rules, and linking formulas.
        Otherwise, overwrites self.config_data entirely.
        """
        import copy
        if not combine:
            # Overwrite
            self.config_data = copy.deepcopy(preset_dict)
        else:
            # Combine/Append
            # Merge ports safely (no duplicates)
            existing_ports = {p["port"] for p in self.config_data.get("ports", [])}
            for p in preset_dict.get("ports", []):
                if p["port"] not in existing_ports:
                    self.config_data.setdefault("ports", []).append(copy.deepcopy(p))
            
            # Merge subsystems safely
            existing_subs = {s["name"] for s in self.config_data.get("subsystems", [])}
            for s in preset_dict.get("subsystems", []):
                if s["name"] not in existing_subs:
                    self.config_data.setdefault("subsystems", []).append(copy.deepcopy(s))
            
            # Merge routing rules safely
            existing_rules = {(r["port"], r["pattern"]) for r in self.config_data.get("routing_rules", [])}
            for r in preset_dict.get("routing_rules", []):
                if (r["port"], r["pattern"]) not in existing_rules:
                    self.config_data.setdefault("routing_rules", []).append(copy.deepcopy(r))
            
            # Merge linking formulas safely
            existing_formulas = {(f["target_sub"], f["target_var"]) for f in self.config_data.get("linking_formulas", [])}
            for f in preset_dict.get("linking_formulas", []):
                if (f["target_sub"], f["target_var"]) not in existing_formulas:
                    self.config_data.setdefault("linking_formulas", []).append(copy.deepcopy(f))
                    
        # Apply the merged/overwritten config
        self.apply_new_workspace_configuration(self.config_data)

    def apply_new_workspace_configuration(self, new_config):
        """
        Called when profile details are applied and saved from the embedded settings tab or dialogue.
        Re-initializes structures and models cleanly.
        """
        self.config_data = new_config
        self.config_manager.config_data = new_config
        self.config_manager.save_config()
        
        # 1. Update data router subsystems schemas
        self.data_router.set_config(new_config)
        
        # 2. Synchronize visual UI state of the embedded setup tab
        if hasattr(self, "setup_tab"):
            self.setup_tab.load_configuration_into_ui()
        
        # 3. Re-trigger plugins visualization rebuilds
        for plugin in self.plugin_manager.active_plugins.values():
            if hasattr(plugin, "rebuild_ui"):
                plugin.rebuild_ui()
            if hasattr(plugin, "rebuild_plots"):
                plugin.rebuild_plots()
        self.sync_central_tabs()
                
        # 4. Restart serial handlers to align with new port mappings
        self.restart_serial_handlers_from_profile()
        self.log_to_diagnostic("SUCCESS: Dynamic general-purpose subsystems schemas successfully applied.")

    def save_workspace_config(self):
        """
        Utility called by safety alarms plugin to save threshold limits.
        """
        subsystems_list = []
        for name, sub in self.data_router.subsystems.items():
            vars_list = []
            for v in sub.variables:
                vars_list.append({
                    "name": v["name"],
                    "display_name": v["display_name"],
                    "column_index": v["column_index"],
                    "gain": v["gain"],
                    "offset": v["offset"],
                    "unit": v["unit"],
                    "is_numerical": v["is_numerical"]
                })
            
            subsystems_list.append({
                "name": sub.name,
                "display_name": sub.display_name,
                "variables": vars_list,
                "thresholds": sub.thresholds,
                "temp_mosfet_var": sub.temp_mosfet_var,
                "temp_transformer_var": sub.temp_transformer_var
            })
            
        self.config_data["subsystems"] = subsystems_list
        self.config_manager.config_data["subsystems"] = subsystems_list
        self.config_manager.save_config()

    def toggle_plots_pausing(self, paused):
        self.plots_paused = paused
        if paused:
            self.btn_pause.setText("▶️ Resume Plots")
            self.log_to_diagnostic("INFO: Time-series scopes charts paused.")
        else:
            self.btn_pause.setText("⏸️ Pause Plots")
            self.log_to_diagnostic("INFO: Scopes charts resumed.")

    def toggle_plots_auto_scale(self, checked):
        self.plot_auto_scale = checked
        # Propagate scaling adjustment to active Trend charts
        for p in self.plugin_manager.active_plugins.values():
            if hasattr(p, "apply_plot_scaling"):
                p.apply_plot_scaling()

    def reset_dock_layout(self):
        """
        Stacks QDockWidgets vertically and side-by-side in standard grids.
        """
        docks = []
        for p in self.plugin_manager.active_plugins.values():
            dock = p.get_dock_widget()
            if dock:
                docks.append(dock)
                
        if not docks:
            return
            
        # Float all, then split
        for d in docks:
            d.setFloating(False)
            self.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, d)
            
        # Stack nicely
        if len(docks) >= 2:
            self.splitDockWidget(docks[0], docks[1], Qt.Orientation.Horizontal)
        if len(docks) >= 3:
            # Try to place terminal at the bottom span
            for d in docks:
                if d.objectName() == "dock_terminal_logs":
                    self.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, d)
                    break
        self.log_to_diagnostic("INFO: Dock widgets standard grid layout successfully reset.")
        self.sync_central_tabs()

    @pyqtSlot(str)
    def log_to_diagnostic(self, text):
        print(text)
        # Push to MCU Debug Terminal Plugin if active
        term = self.plugin_manager.active_plugins.get("mcu_terminal")
        if term and hasattr(term, "log_to_diagnostic"):
            term.log_to_diagnostic(text)

    def sync_central_tabs(self):
        """
        Synchronizes central tabs based on active plugin states for Trend Waveforms,
        Live Grid Overview, Parameter Calibration, Hardware Terminal, and Service Console.
        """
        plugins_config = [
            ("trend_charts", "📈 Telemetry Waveforms"),
            ("telemetry_cards", "📊 Live Grid Overview"),
            ("parameter_manager", "🔧 Parameter Calibration"),
            ("mcu_terminal", "📺 Hardware Terminal"),
            ("service_console", "🌐 Service Console")
        ]
        
        # Track existing tab indices by looking at widget references or titles
        existing_widgets = []
        for idx in range(self.central_tab_widget.count()):
            existing_widgets.append(self.central_tab_widget.widget(idx))
            
        for plugin_id, tab_title in plugins_config:
            plugin = self.plugin_manager.active_plugins.get(plugin_id)
            if plugin and hasattr(plugin, "container") and plugin.container:
                container = plugin.container
                if container not in existing_widgets:
                    self.central_tab_widget.addTab(container, tab_title)
                # Hide its dock widget if it exists to avoid duplication
                dock = plugin.get_dock_widget()
                if dock:
                    dock.setVisible(False)
            else:
                # Remove tab if it exists
                for idx in range(self.central_tab_widget.count()):
                    if self.central_tab_widget.tabText(idx) == tab_title:
                        self.central_tab_widget.removeTab(idx)
                        break

    def apply_premium_dark_styling(self):
        qss = """
        QMainWindow { background-color: #0e0f12; }
        QWidget { color: #a0a5b5; font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; }
        
        /* Ribbon frame */
        #RibbonBar { background-color: #13141a; border-bottom: 1px solid #272a38; }
        
        /* Tab Widget inside the central pane and other widgets */
        QTabWidget::pane { border: 1px solid #272a38; background-color: #13141a; border-radius: 4px; top: -1px; }
        QTabBar::tab { background-color: #1b1c24; color: #8e94a6; padding: 6px 16px; border-top-left-radius: 4px; border-top-right-radius: 4px; border: 1px solid #272a38; border-bottom: none; font-weight: bold; }
        QTabBar::tab:selected { background-color: #13141a; color: #38bdf8; border-bottom: 2px solid #38bdf8; }
        QTabBar::tab:hover { color: #ffffff; background-color: #222530; }
        
        QMainWindow::separator { background-color: #0e0f12; width: 6px; height: 6px; }
        QMainWindow::separator:hover { background-color: #38bdf8; }
        
        /* Dock Widgets */
        QDockWidget { background-color: #0e0f12; color: #e2e8f0; font-weight: bold; border: none; }
        QDockWidget::title { background-color: #1e202b; padding: 6px 10px; border: 1px solid #272a38; border-bottom: 1px solid #13141a; border-top-left-radius: 4px; border-top-right-radius: 4px; color: #38bdf8; font-weight: bold; }
        QDockWidget::close-button, QDockWidget::float-button { background-color: #1e202b; border: 1px solid #272a38; border-radius: 3px; padding: 2px; }
        QDockWidget::close-button:hover { background-color: #dc2626; border: 1px solid #ef4444; }
        QDockWidget::float-button:hover { background-color: #38bdf8; border: 1px solid #7dd3fc; }
        QDockWidget > QWidget { background-color: #13141a; border: 1px solid #272a38; border-bottom-left-radius: 4px; border-bottom-right-radius: 4px; }
        
        /* Standard Group Boxes */
        QGroupBox { border: 1px solid #272a38; border-radius: 4px; margin-top: 10px; padding-top: 12px; font-weight: bold; color: #38bdf8; }
        QGroupBox::title { subcontrol-origin: margin; subcontrol-position: top left; left: 10px; padding: 0 5px; background-color: #13141a; }
        
        /* Inputs & Controls */
        QLineEdit, QComboBox, QDoubleSpinBox, QSpinBox, QTextEdit { background-color: #0a0b0d; border: 1px solid #272a38; border-radius: 4px; padding: 3px; color: #ffffff; }
        QLineEdit:focus, QComboBox:focus, QDoubleSpinBox:focus, QSpinBox:focus, QTextEdit:focus { border-color: #38bdf8; }
        QComboBox QAbstractItemView { background-color: #13141a; border: 1px solid #272a38; selection-background-color: #1e202b; selection-color: #38bdf8; }
        
        /* Buttons */
        QPushButton { background-color: #1b1c24; border: 1px solid #272a38; border-radius: 4px; padding: 5px 12px; color: #38bdf8; font-weight: bold; }
        QPushButton:hover { background-color: #222530; border-color: #38bdf8; }
        QPushButton:pressed { background-color: #38bdf8; color: #0e0f12; }
        QPushButton:disabled { background-color: #14151b; border-color: #1e202b; color: #64748b; }
        
        /* Checkboxes */
        QCheckBox { spacing: 6px; font-weight: bold; color: #8e94a6; }
        QCheckBox::indicator { width: 13px; height: 13px; border: 1px solid #272a38; background-color: #0a0b0d; border-radius: 3px; }
        QCheckBox::indicator:checked { background-color: #38bdf8; border-color: #38bdf8; }
        QCheckBox:hover { color: #ffffff; }
        
        /* Table / Grid */
        QTableWidget { background-color: #0a0b0d; border: 1px solid #272a38; gridline-color: #1f212a; border-radius: 4px; }
        QHeaderView::section { background-color: #1b1c24; color: #38bdf8; font-weight: bold; border: 1px solid #272a38; padding: 4px; }
        
        /* ScrollBar */
        QScrollBar:vertical { border: none; background: #0e0f12; width: 10px; margin: 0px; }
        QScrollBar::handle:vertical { background: #272a38; min-height: 20px; border-radius: 5px; }
        QScrollBar::handle:vertical:hover { background: #38bdf8; }
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0px; }
        """
        self.setStyleSheet(qss)

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = DashboardWindow()
    window.show()
    sys.exit(app.exec())
