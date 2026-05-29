# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.2 (2026-05-29)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.2 (2026-05-29) - Antigravity: Added connection_status_changed slot & PC-led resync historical request logic.
# * v2.0.1 (2026-05-29) - Antigravity: Injected main_window reference to DataRouter to support dual-track parsing plugin pipeline.
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
    QTabWidget, QDockWidget, QCheckBox, QFrame, QListView, QListWidget, QLineEdit, QDialog,
    QDialogButtonBox, QMessageBox, QScrollArea, QSplitter, QColorDialog
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
        header_widget.setObjectName("header_widget")
        header_lay = QVBoxLayout(header_widget)
        header_lay.setContentsMargins(15, 10, 15, 10)
        header_lay.setSpacing(2)
        
        title_lbl = QLabel("🛠️ Enterprise Embedded Workspace Configurator")
        title_lbl.setObjectName("title_lbl")
        desc_lbl = QLabel("Establish physical USB COM ports list, register logical microcontroller subsystems, assign routing signatures, and build mathematical formulas.")
        desc_lbl.setObjectName("desc_lbl")
        
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
        self.setup_theme_tab()
        self.setup_dev_tools_tab()
        
        # 3. Actions Footer Panel
        btn_panel = QWidget()
        btn_panel.setObjectName("footer_panel")
        btn_lay = QHBoxLayout(btn_panel)
        btn_lay.setContentsMargins(15, 8, 15, 8)
        btn_lay.setSpacing(10)
        
        btn_apply = QPushButton("✅ Apply & Commit Changes")
        btn_apply.setObjectName("btn_apply")
        btn_apply.clicked.connect(self.validate_and_save)
        
        btn_save_as = QPushButton("💾 Save Profile As...")
        btn_save_as.setObjectName("btn_save_as")
        btn_save_as.clicked.connect(self.main_window.save_profile_as)
        
        btn_load = QPushButton("📂 Load Profile...")
        btn_load.setObjectName("btn_load")
        btn_load.clicked.connect(self.main_window.load_profile)
        
        btn_presets = QPushButton("📋 프리셋 템플릿 로드/조합...")
        btn_presets.setObjectName("btn_presets")
        btn_presets.clicked.connect(self.open_preset_dialog)
        
        btn_revert = QPushButton("🔄 Revert Uncommitted Changes")
        btn_revert.setObjectName("btn_revert")
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
        selected_sub_name = self.list_subs.currentItem().text() if self.list_subs.currentItem() else ""
        
        self.list_subs.blockSignals(True)
        self.list_subs.clear()
        for s in self.config_data.get("subsystems", []):
            self.list_subs.addItem(s["name"])
        self.list_subs.blockSignals(False)
        
        # Trigger selection reload for subsystems details
        if self.list_subs.count() > 0:
            matched_items = self.list_subs.findItems(selected_sub_name, Qt.MatchFlag.MatchExactly)
            if matched_items:
                self.list_subs.setCurrentItem(matched_items[0])
            else:
                self.list_subs.setCurrentRow(self.list_subs.count() - 1)
            self.on_subsystem_selection_changed(self.list_subs.currentItem().text())
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
        
        # 6. Theme Config Tab synchronization
        theme_cfg = self.config_data.get("theme_config", {
            "window_bg": "#0e0f12",
            "card_bg": "#13141a",
            "border": "#272a38",
            "accent": "#38bdf8",
            "text": "#a0a5b5"
        })
        
        # Load theme_cfg colors into self.custom_colors
        if hasattr(self, "custom_colors") and self.custom_colors:
            for key, val in theme_cfg.items():
                if key in self.custom_colors:
                    self.custom_colors[key]["color"] = val
                    if self.custom_colors[key]["preview"]:
                        self.custom_colors[key]["preview"].setStyleSheet(f"background-color: {val}; border: 1px solid #475569; border-radius: 4px;")
                        
            # Match against presets to set combo box
            presets = {
                "Classic Dark": {"window_bg": "#0e0f12", "card_bg": "#13141a", "border": "#272a38", "accent": "#38bdf8", "text": "#a0a5b5"},
                "Cyberpunk Neon": {"window_bg": "#09090e", "card_bg": "#12121e", "border": "#ff007f", "accent": "#00f0ff", "text": "#e2e8f0"},
                "Emerald Forest": {"window_bg": "#0b0f0c", "card_bg": "#121a14", "border": "#203a27", "accent": "#10b981", "text": "#d1fae5"},
                "Sunset Amber": {"window_bg": "#110d0b", "card_bg": "#1c1512", "border": "#36251c", "accent": "#f97316", "text": "#fed7aa"},
                "Arctic Slate": {"window_bg": "#0f1115", "card_bg": "#181c24", "border": "#2e3545", "accent": "#60a5fa", "text": "#e2e8f0"}
            }
            
            matched_idx = -1
            for idx, (p_name, p_colors) in enumerate(presets.items()):
                if all(theme_cfg.get(k) == p_colors[k] for k in p_colors):
                    matched_idx = idx
                    break
                    
            if hasattr(self, "combo_theme_presets") and self.combo_theme_presets:
                self.combo_theme_presets.blockSignals(True)
                self.combo_theme_presets.setCurrentIndex(matched_idx)
                self.combo_theme_presets.blockSignals(False)
            
            self.update_live_preview()
            self.apply_setup_tab_theme()

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
        
        self.list_subs = QListWidget()
        self.list_subs.setObjectName("list_subs")
        self.list_subs.currentTextChanged.connect(self.on_subsystem_selection_changed)
        left_lay.addWidget(self.list_subs, 1)
        
        btn_sub_lay = QHBoxLayout()
        btn_add_sub = QPushButton("➕ Add Sub")
        btn_add_sub.clicked.connect(self.add_new_subsystem)
        btn_del_sub = QPushButton("➖ Remove")
        btn_del_sub.clicked.connect(self.remove_selected_subsystem)
        btn_sub_lay.addWidget(btn_add_sub)
        btn_sub_lay.addWidget(btn_del_sub)
        left_lay.addLayout(btn_sub_lay)
        
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
        self.main_window.show_quick_subsystem_dialog()

    def remove_selected_subsystem(self):
        cur_item = self.list_subs.currentItem()
        if not cur_item:
            return
        cur_id = cur_item.text()
        
        self.config_data["subsystems"] = [s for s in self.config_data["subsystems"] if s["name"] != cur_id]
        row = self.list_subs.row(cur_item)
        self.list_subs.takeItem(row)
        
        # Select another item if possible
        if self.list_subs.count() > 0:
            self.list_subs.setCurrentRow(0)
        else:
            self.rebuild_subsystem_details_panel("")

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
        btn_del_v.clicked.connect(lambda: self.del_variable_row(sub))
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
        old_state = self.tbl_vars.blockSignals(True)
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
        
        self.tbl_vars.blockSignals(old_state)
        if not old_state:
            self.save_current_variables_to_state(sub)

    def del_variable_row(self, sub):
        cur = self.tbl_vars.currentRow()
        if cur >= 0:
            old_state = self.tbl_vars.blockSignals(True)
            self.tbl_vars.removeRow(cur)
            self.tbl_vars.blockSignals(old_state)
            self.save_current_variables_to_state(sub)

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
        lay.setSpacing(12)
        
        # Header Info
        desc = QLabel("🧩 통합 플러그인 센터 (IDE Plugins Manager)")
        desc.setObjectName("lbl_plugins_header")
        lay.addWidget(desc)
        
        # Splitter Layout
        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.setObjectName("plugin_splitter")
        
        # 1. LEFT PANEL (Plugin Catalog List)
        left_panel = QWidget()
        left_panel.setObjectName("left_panel")
        left_lay = QVBoxLayout(left_panel)
        left_lay.setContentsMargins(10, 10, 10, 10)
        left_lay.setSpacing(8)
        
        # Search Box
        self.plugin_search = QLineEdit()
        self.plugin_search.setObjectName("plugin_search")
        self.plugin_search.setPlaceholderText("🔍 플러그인 이름 또는 키워드 검색...")
        self.plugin_search.textChanged.connect(self.on_plugin_search_changed)
        left_lay.addWidget(self.plugin_search)
        
        # Category Tabs (Installed vs Marketplace)
        cat_lay = QHBoxLayout()
        cat_lay.setSpacing(6)
        
        self.btn_cat_installed = QPushButton("설치됨")
        self.btn_cat_installed.setObjectName("btn_cat_installed")
        self.btn_cat_installed.setCheckable(True)
        self.btn_cat_installed.setChecked(True)
        self.btn_cat_installed.clicked.connect(self.on_plugin_cat_installed_clicked)
        
        self.btn_cat_market = QPushButton("마켓플레이스")
        self.btn_cat_market.setObjectName("btn_cat_market")
        self.btn_cat_market.setCheckable(True)
        self.btn_cat_market.clicked.connect(self.on_plugin_cat_market_clicked)
        
        cat_lay.addWidget(self.btn_cat_installed)
        cat_lay.addWidget(self.btn_cat_market)
        left_lay.addLayout(cat_lay)
        
        # Scroll area for items list
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("border: none; background: transparent;")
        
        scroll_widget = QWidget()
        scroll_widget.setStyleSheet("background: transparent;")
        self.plugins_checkbox_lay = QVBoxLayout(scroll_widget)
        self.plugins_checkbox_lay.setContentsMargins(0, 0, 0, 0)
        self.plugins_checkbox_lay.setSpacing(6)
        
        scroll.setWidget(scroll_widget)
        left_lay.addWidget(scroll, 1)
        
        # Manual Local install link/label in left panel bottom
        btn_manual = QPushButton("📂 로컬 플러그인 파일(.py) 수동 설치...")
        btn_manual.setObjectName("btn_manual")
        btn_manual.clicked.connect(self.install_custom_plugin)
        left_lay.addWidget(btn_manual)
        
        splitter.addWidget(left_panel)
        
        # 2. RIGHT PANEL (Master Detail View)
        self.plugin_detail_panel = QWidget()
        self.plugin_detail_panel.setObjectName("plugin_detail_panel")
        self.detail_lay = QVBoxLayout(self.plugin_detail_panel)
        self.detail_lay.setContentsMargins(20, 20, 20, 20)
        self.detail_lay.setSpacing(15)
        
        # Selected plugin ID tracker
        self.selected_plugin_id = None
        
        self.show_no_plugin_selected()
        splitter.addWidget(self.plugin_detail_panel)
        
        splitter.setSizes([320, 480])
        lay.addWidget(splitter, 1)
        
        self.tabs.addTab(tab, "🧩 플러그인 관리")

    def on_plugin_search_changed(self, text):
        self.main_window.load_plugins_from_profile()

    def on_plugin_cat_installed_clicked(self):
        self.btn_cat_installed.setChecked(True)
        self.btn_cat_market.setChecked(False)
        self.btn_cat_installed.style().unpolish(self.btn_cat_installed)
        self.btn_cat_installed.style().polish(self.btn_cat_installed)
        self.btn_cat_market.style().unpolish(self.btn_cat_market)
        self.btn_cat_market.style().polish(self.btn_cat_market)
        self.main_window.load_plugins_from_profile()

    def on_plugin_cat_market_clicked(self):
        self.btn_cat_installed.setChecked(False)
        self.btn_cat_market.setChecked(True)
        self.btn_cat_installed.style().unpolish(self.btn_cat_installed)
        self.btn_cat_installed.style().polish(self.btn_cat_installed)
        self.btn_cat_market.style().unpolish(self.btn_cat_market)
        self.btn_cat_market.style().polish(self.btn_cat_market)
        self.main_window.load_plugins_from_profile()

    def show_no_plugin_selected(self):
        # Clear layout
        while self.detail_lay.count():
            item = self.detail_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        lbl_hint = QLabel("👈 좌측에서 플러그인을 선택하면 상세 정보가 이곳에 나타납니다.")
        lbl_hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lbl_hint.setStyleSheet("font-size: 11px; color: #64748b; font-weight: bold; margin-top: 120px; border: none; background: transparent;")
        self.detail_lay.addWidget(lbl_hint)
        self.detail_lay.addStretch()

    def show_plugin_details(self, pid, pdata):
        # Clear layout
        while self.detail_lay.count():
            item = self.detail_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        # 1. Header Card Info
        hdr_frame = QWidget()
        hdr_frame.setStyleSheet("background: transparent; border: none;")
        hdr_lay = QHBoxLayout(hdr_frame)
        hdr_lay.setContentsMargins(0, 0, 0, 0)
        
        lbl_icon = QLabel(pdata["icon"])
        lbl_icon.setStyleSheet("font-size: 32px; padding: 5px; border: none; background: transparent;")
        hdr_lay.addWidget(lbl_icon)
        
        lbl_info = QWidget()
        lbl_info.setStyleSheet("background: transparent; border: none;")
        lbl_info_lay = QVBoxLayout(lbl_info)
        lbl_info_lay.setContentsMargins(0, 0, 0, 0)
        lbl_info_lay.setSpacing(4)
        
        lbl_name = QLabel(pdata["name"])
        lbl_name.setObjectName("lbl_plugin_name")
        lbl_info_lay.addWidget(lbl_name)
        
        lbl_meta = QLabel(f"버전: {pdata['version']}  |  제작자: {pdata['author']}")
        lbl_meta.setObjectName("lbl_plugin_meta")
        lbl_info_lay.addWidget(lbl_meta)
        
        hdr_lay.addWidget(lbl_info, 1)
        self.detail_lay.addWidget(hdr_frame)
        
        # 2. Action row
        act_frame = QWidget()
        act_frame.setStyleSheet("background: transparent; border: none;")
        act_lay = QHBoxLayout(act_frame)
        act_lay.setContentsMargins(0, 0, 0, 0)
        act_lay.setSpacing(12)
        
        if pdata["installed"]:
            # Active Switch Button (Enable/Disable toggle)
            self.btn_toggle_active = QPushButton("✅ 활성화 상태" if pdata["active"] else "⬜ 비활성화 상태")
            self.btn_toggle_active.setObjectName("btn_toggle_active")
            self.btn_toggle_active.setCheckable(True)
            self.btn_toggle_active.setChecked(pdata["active"])
            # Connect toggle handler safely
            self.btn_toggle_active.clicked.connect(lambda checked, p=pid: self.main_window.on_plugin_checkbox_toggled(p, checked))
            act_lay.addWidget(self.btn_toggle_active)
            
            # Uninstall Button (Only for external plugins - i.e. NOT internal ones)
            is_internal = pid in ["telemetry_cards", "trend_charts", "service_console", "parameter_manager", "mcu_terminal", "topology_visualizer"]
            if not is_internal:
                btn_del = QPushButton("❌ 플러그인 삭제")
                btn_del.setObjectName("btn_plugin_del")
                btn_del.clicked.connect(lambda checked, p=pid: self.install_plugin_in_detail(p)) # will call uninstall via helper
                btn_del.clicked.disconnect()
                btn_del.clicked.connect(lambda checked, p=pid: self.uninstall_plugin_in_detail(p))
                act_lay.addWidget(btn_del)
        else:
            # Install Button for marketplace
            btn_inst = QPushButton("🌐 플러그인 온라인 설치")
            btn_inst.setObjectName("btn_plugin_inst")
            btn_inst.clicked.connect(lambda checked, p=pid: self.install_plugin_in_detail(p))
            act_lay.addWidget(btn_inst)
            
        act_lay.addStretch()
        self.detail_lay.addWidget(act_frame)
        
        # Divider line
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setStyleSheet("background-color: #272a38; max-height: 1px; border: none;")
        self.detail_lay.addWidget(line)
        
        # 3. Description Area
        lbl_desc_title = QLabel("상세 정보 및 기능 설명")
        lbl_desc_title.setStyleSheet("font-size: 12px; font-weight: bold; color: #e2e8f0; border: none; background: transparent;")
        self.detail_lay.addWidget(lbl_desc_title)
        
        lbl_desc = QLabel(pdata["description"])
        lbl_desc.setWordWrap(True)
        lbl_desc.setStyleSheet("font-size: 11px; color: #a0a5b5; line-height: 1.5; border: none; background: transparent;")
        self.detail_lay.addWidget(lbl_desc)
        
        # 4. Status Indicator Box
        status_box = QFrame()
        status_box.setStyleSheet("background-color: #12131a; border: 1px solid #272a38; border-radius: 4px; padding: 10px;")
        status_lay = QVBoxLayout(status_box)
        status_lay.setContentsMargins(10, 10, 10, 10)
        
        stat_text = "● 현재 플러그인은 로컬 부팅 모듈로 대기 중입니다."
        if pdata["active"]:
            stat_text = "🟢 현재 플러그인이 실행되고 있으며 메인 윈도우 독(Dock)에 로드되었습니다."
        elif not pdata["installed"]:
            stat_text = "🔵 온라인 스토어에 연결되어 다운로드 즉시 실행 가능합니다."
            
        lbl_stat = QLabel(stat_text)
        lbl_stat.setStyleSheet("font-size: 10px; color: #10b981;" if pdata["active"] else ("font-size: 10px; color: #38bdf8;" if not pdata["installed"] else "font-size: 10px; color: #a0a5b5;"))
        lbl_stat.setStyleSheet(lbl_stat.styleSheet() + " border: none; background: transparent;")
        status_lay.addWidget(lbl_stat)
        self.detail_lay.addWidget(status_box)
        
        self.detail_lay.addStretch()

    def install_plugin_in_detail(self, plugin_id):
        self.main_window.install_plugin_from_ide_view(plugin_id)

    def uninstall_plugin_in_detail(self, plugin_id):
        self.main_window.uninstall_plugin_from_ide_view(plugin_id)

    def install_custom_plugin(self):
        self.main_window.install_custom_plugin()

    def setup_theme_tab(self):
        # Initialize custom colors dictionary with defaults
        self.custom_colors = {
            "window_bg": {"label": "Window Background (창 배경)", "color": "#0e0f12", "btn": None, "preview": None},
            "card_bg": {"label": "Surface / Card Background (카드 표면)", "color": "#13141a", "btn": None, "preview": None},
            "border": {"label": "Component Border Color (테두리선)", "color": "#272a38", "btn": None, "preview": None},
            "accent": {"label": "Neon Highlight / Accent Color (액센트 포인트)", "color": "#38bdf8", "btn": None, "preview": None},
            "text": {"label": "Primary Font / Text Color (메인 텍스트)", "color": "#a0a5b5", "btn": None, "preview": None}
        }
        
        tab = QWidget()
        tab.setObjectName("tab_theme")
        lay = QVBoxLayout(tab)
        lay.setContentsMargins(20, 20, 20, 20)
        lay.setSpacing(15)
        
        # Title/Description
        desc = QLabel("🎨 대시보드 맞춤형 테마 커스터마이저 (Dashboard Theme Settings)")
        desc.setStyleSheet("font-weight: bold; color: #38bdf8; font-size: 14px; border: none; background: transparent;")
        desc_sub = QLabel("원하는 색상 테마 프리셋을 사용하거나, 각 컴포넌트의 색상을 자유롭게 변경하여 사용자만의 전용 모니터링 환경을 완성할 수 있습니다.")
        desc_sub.setStyleSheet("color: #8e94a6; font-size: 11px; border: none; background: transparent;")
        
        lay.addWidget(desc)
        lay.addWidget(desc_sub)
        
        # Splitter Layout
        split = QSplitter(Qt.Orientation.Horizontal)
        split.setStyleSheet("QSplitter::handle { background-color: #272a38; width: 4px; }")
        
        # Left Panel (Theme Customization Controls)
        left_ctrl = QWidget()
        left_ctrl_lay = QVBoxLayout(left_ctrl)
        left_ctrl_lay.setContentsMargins(0, 0, 10, 0)
        left_ctrl_lay.setSpacing(15)
        
        # Presets GroupBox
        grp_presets = QGroupBox("🎨 기본 테마 프리셋 선택")
        grp_presets.setStyleSheet("QGroupBox { font-size: 12px; }")
        preset_lay = QVBoxLayout(grp_presets)
        preset_lay.setSpacing(10)
        preset_lay.setContentsMargins(15, 20, 15, 15)
        
        self.combo_theme_presets = QComboBox()
        self.combo_theme_presets.addItems([
            "Classic Dark (기본 다크)",
            "⚡ Cyberpunk Neon (사이버펑크 네온)",
            "🔋 Emerald Forest (에메랄드 포레스트)",
            "🔥 Sunset Amber (선셋 앰버)",
            "❄️ Arctic Slate (아틱 슬레이트)"
        ])
        self.combo_theme_presets.currentIndexChanged.connect(self.on_theme_preset_changed)
        preset_lay.addWidget(self.combo_theme_presets)
        left_ctrl_lay.addWidget(grp_presets)
        
        # Custom Color Picker GroupBox
        grp_custom = QGroupBox("⚙️ 상세 요소별 커스텀 색상")
        grp_custom.setStyleSheet("QGroupBox { font-size: 12px; }")
        custom_lay = QGridLayout(grp_custom)
        custom_lay.setSpacing(12)
        custom_lay.setContentsMargins(15, 20, 15, 15)
        
        row = 0
        for key, val in self.custom_colors.items():
            lbl = QLabel(val["label"])
            lbl.setStyleSheet("font-weight: bold; color: #e2e8f0; border: none; background: transparent;")
            
            # Color preview block
            preview = QFrame()
            preview.setFixedSize(24, 24)
            preview.setStyleSheet(f"background-color: {val['color']}; border: 1px solid #475569; border-radius: 4px;")
            val["preview"] = preview
            
            # Pick Button
            btn = QPushButton("색상 선택...")
            btn.setStyleSheet("""
                QPushButton {
                    background-color: #1b1c24; border: 1px solid #272a38; color: #a0a5b5; padding: 4px 10px; font-size: 10px;
                }
                QPushButton:hover { border-color: #38bdf8; color: #ffffff; }
            """)
            btn.clicked.connect(lambda checked, k=key: self.pick_custom_color(k))
            val["btn"] = btn
            
            custom_lay.addWidget(lbl, row, 0)
            custom_lay.addWidget(preview, row, 1)
            custom_lay.addWidget(btn, row, 2)
            row += 1
            
        left_ctrl_lay.addWidget(grp_custom)
        left_ctrl_lay.addStretch()
        
        # Right Panel (Interactive Theme Live Preview)
        right_preview = QFrame()
        right_preview.setObjectName("theme_preview_board")
        right_preview.setStyleSheet("background-color: #0c0c0f; border: 1px solid #272a38; border-radius: 8px;")
        right_lay = QVBoxLayout(right_preview)
        right_lay.setContentsMargins(20, 20, 20, 20)
        right_lay.setSpacing(12)
        
        preview_title = QLabel("🖥️ 실시간 적용 미리보기 (Live Preview)")
        preview_title.setStyleSheet("font-weight: bold; font-size: 12px; color: #38bdf8; border: none; background: transparent;")
        right_lay.addWidget(preview_title)
        
        # Sample Card Preview inside
        self.preview_card = QFrame()
        self.preview_card.setObjectName("preview_card")
        self.preview_card.setFrameShape(QFrame.Shape.StyledPanel)
        self.preview_card_lay = QVBoxLayout(self.preview_card)
        self.preview_card_lay.setContentsMargins(15, 15, 15, 15)
        self.preview_card_lay.setSpacing(10)
        
        card_lbl = QLabel("🔌 DAB Subsystem Node")
        card_lbl.setObjectName("preview_card_lbl")
        card_lbl.setStyleSheet("font-weight: bold; font-size: 13px;")
        
        card_desc = QLabel("Vin: 385.2 V  |  Iin: 8.4 A  |  Eff: 96.4 %")
        card_desc.setObjectName("preview_card_desc")
        card_desc.setStyleSheet("font-size: 11px;")
        
        self.preview_card_btn = QPushButton("Active Control Relay")
        self.preview_card_btn.setObjectName("preview_card_btn")
        self.preview_card_btn.setFixedWidth(160)
        
        self.preview_card_lay.addWidget(card_lbl)
        self.preview_card_lay.addWidget(card_desc)
        self.preview_card_lay.addWidget(self.preview_card_btn)
        
        right_lay.addWidget(self.preview_card)
        
        # Sample alarm button
        btn_test_alert = QPushButton("🔔 테마 동기화된 알림창(QMessageBox) 테스트")
        btn_test_alert.setStyleSheet("""
            QPushButton {
                background-color: #1e1e2d; border: 1px solid #38bdf8; color: #38bdf8; padding: 8px; border-radius: 4px; font-weight: bold;
            }
            QPushButton:hover { background-color: #38bdf8; color: #0c0c0e; }
        """)
        btn_test_alert.clicked.connect(self.show_test_alert)
        right_lay.addWidget(btn_test_alert)
        
        right_lay.addStretch()
        
        split.addWidget(left_ctrl)
        split.addWidget(right_preview)
        split.setSizes([450, 450])
        
        lay.addWidget(split)
        self.tabs.addTab(tab, "🎨 테마 설정")

    def setup_dev_tools_tab(self):
        tab = QWidget()
        tab.setObjectName("tab_devtools")
        lay = QVBoxLayout(tab)
        lay.setContentsMargins(25, 25, 25, 25)
        lay.setSpacing(20)
        
        # Title/Description
        title_lbl = QLabel("🛠️ 개발자 도구 (Developer Tools & Simulation Controls)")
        title_lbl.setStyleSheet("font-weight: bold; color: #38bdf8; font-size: 16px; border: none; background: transparent;")
        desc_lbl = QLabel("대시보드 기동 모드를 가상 시뮬레이터 모드와 실제 물리 하드웨어 시리얼 모드 간에 실시간으로 전환하고 제어할 수 있습니다.")
        desc_lbl.setStyleSheet("color: #8e94a6; font-size: 12px; border: none; background: transparent;")
        lay.addWidget(title_lbl)
        lay.addWidget(desc_lbl)
        
        # Status Card Group
        self.grp_sim_status = QGroupBox("📡 현재 시스템 드라이버 모드")
        self.grp_sim_status.setStyleSheet("QGroupBox { font-size: 12px; }")
        status_lay = QVBoxLayout(self.grp_sim_status)
        status_lay.setContentsMargins(20, 25, 20, 20)
        status_lay.setSpacing(15)
        
        self.lbl_sim_status_val = QLabel("")
        self.lbl_sim_status_val.setStyleSheet("font-size: 14px; font-weight: bold;")
        status_lay.addWidget(self.lbl_sim_status_val)
        
        self.lbl_sim_desc = QLabel("")
        self.lbl_sim_desc.setStyleSheet("color: #a2a2b5; font-size: 11px;")
        status_lay.addWidget(self.lbl_sim_desc)
        
        lay.addWidget(self.grp_sim_status)
        
        # Action Panel
        grp_action = QGroupBox("⚙️ 백그라운드 드라이버 런타임 제어")
        grp_action.setStyleSheet("QGroupBox { font-size: 12px; }")
        action_lay = QVBoxLayout(grp_action)
        action_lay.setContentsMargins(20, 25, 20, 20)
        action_lay.setSpacing(15)
        
        self.btn_toggle_sim = QPushButton("")
        self.btn_toggle_sim.setMinimumHeight(45)
        self.btn_toggle_sim.clicked.connect(self.toggle_simulation_mode)
        action_lay.addWidget(self.btn_toggle_sim)
        
        warning_lbl = QLabel("⚠️ 주의: 전환 시 기존의 모든 백그라운드 리드 스레드가 완전히 해제된 후, 새로운 드라이버 스펙(가상/물리)으로 실시간 재배치 및 자동 연결이 트리거됩니다.")
        warning_lbl.setStyleSheet("color: #fca5a5; font-size: 11px; font-weight: bold;")
        action_lay.addWidget(warning_lbl)
        
        lay.addWidget(grp_action)
        lay.addStretch()
        
        self.tabs.addTab(tab, "🛠️ 개발자 도구")
        
        # Load active status in UI
        self.update_sim_status_ui()

    def is_simulation_active(self):
        return self.main_window.is_simulation_active()

    def update_sim_status_ui(self):
        active = self.is_simulation_active()
        if active:
            self.lbl_sim_status_val.setText("🟢 가상 시뮬레이션 모드 활성화됨 (Virtual Emulation Driver Active)")
            self.lbl_sim_status_val.setStyleSheet("font-size: 14px; font-weight: bold; color: #10b981;")
            self.lbl_sim_desc.setText(
                "현재 STM32/ESP32 가상 포트(COM3: Engine, COM4: Battery 등) 시뮬레이션 데이터 스트림이 백그라운드 스레드에서 구동되고 있습니다.\n"
                "가상 장치 데이터 패킷이 CSV Prefix 형식으로 Router를 통과하여 실시간 차트 및 계측 카드를 갱신합니다."
            )
            self.btn_toggle_sim.setText("🔌 물리 하드웨어 시리얼 모드로 즉시 전환 (Switch to Physical Hardware Serial)")
            self.btn_toggle_sim.setStyleSheet("""
                QPushButton {
                    background-color: #1e1b4b; border: 1px solid #6366f1; color: #a5b4fc; font-size: 12px; font-weight: bold;
                }
                QPushButton:hover { background-color: #312e81; border-color: #818cf8; color: #ffffff; }
            """)
        else:
            self.lbl_sim_status_val.setText("🔵 물리 하드웨어 시리얼 모드 활성화됨 (Physical Hardware Serial Mode Active)")
            self.lbl_sim_status_val.setStyleSheet("font-size: 14px; font-weight: bold; color: #38bdf8;")
            self.lbl_sim_desc.setText(
                "현재 PC에 실제로 연결되는 물리적인 COM VCP 직렬 장치(물리 센서, MCU) 드라이버(pyserial)가 로드된 상태입니다.\n"
                "실제 MCU 장치를 연결하고 'Com Ports 설정' 탭에서 체크를 활성화하여 실시간 계측을 수행할 수 있습니다."
            )
            self.btn_toggle_sim.setText("⚡ 가상 시뮬레이션 모드로 즉시 전환 (Switch to Virtual Simulation)")
            self.btn_toggle_sim.setStyleSheet("""
                QPushButton {
                    background-color: #064e3b; border: 1px solid #10b981; color: #6ee7b7; font-size: 12px; font-weight: bold;
                }
                QPushButton:hover { background-color: #065f46; border-color: #34d399; color: #ffffff; }
            """)

    def toggle_simulation_mode(self):
        import simulation_mock
        active = self.is_simulation_active()
        
        QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)
        try:
            if active:
                simulation_mock.restore_physical_serial()
                self.main_window.log_to_diagnostic("SYSTEM: Switched to Physical pyserial Driver Mode.")
            else:
                simulation_mock.setup_simulation_mock()
                self.main_window.log_to_diagnostic("SYSTEM: Switched to Virtual Simulation Emulation Mode.")
            
            # Restart listening thread handler cleanly inside dashboard main_window
            self.main_window.restart_serial_handlers_from_profile()
            
            # Update UI in setup tab
            self.update_sim_status_ui()
            
            # Re-scan physical ports to update COM Ports list checkbox in UI immediately
            self.main_window.scan_physical_com_ports()
            
            QMessageBox.information(
                self,
                "드라이버 모드 전환 완료",
                "성공적으로 시리얼 백그라운드 드라이버 모드가 전환되었습니다!\n\n"
                f"현재 모드: {'물리 하드웨어 시리얼 모드' if active else '가상 시뮬레이션 모드'}"
            )
        except Exception as e:
            self.main_window.log_to_diagnostic(f"ERROR: Failed to switch driver mode: {str(e)}")
            QMessageBox.critical(self, "드라이버 전환 실패", f"드라이버 스왑 과정 중 에러가 발생했습니다:\n{str(e)}")
        finally:
            QApplication.restoreOverrideCursor()

    def on_theme_preset_changed(self, index):
        preset_name = self.combo_theme_presets.currentText()
        
        name_clean = preset_name
        if "Classic" in preset_name:
            name_clean = "Classic Dark"
        elif "Cyberpunk" in preset_name:
            name_clean = "Cyberpunk Neon"
        elif "Emerald" in preset_name:
            name_clean = "Emerald Forest"
        elif "Sunset" in preset_name:
            name_clean = "Sunset Amber"
        elif "Arctic" in preset_name:
            name_clean = "Arctic Slate"
            
        presets = {
            "Classic Dark": {
                "window_bg": "#0e0f12",
                "card_bg": "#13141a",
                "border": "#272a38",
                "accent": "#38bdf8",
                "text": "#a0a5b5"
            },
            "Cyberpunk Neon": {
                "window_bg": "#09090e",
                "card_bg": "#12121e",
                "border": "#ff007f",
                "accent": "#00f0ff",
                "text": "#e2e8f0"
            },
            "Emerald Forest": {
                "window_bg": "#0b0f0c",
                "card_bg": "#121a14",
                "border": "#203a27",
                "accent": "#10b981",
                "text": "#d1fae5"
            },
            "Sunset Amber": {
                "window_bg": "#110d0b",
                "card_bg": "#1c1512",
                "border": "#36251c",
                "accent": "#f97316",
                "text": "#fed7aa"
            },
            "Arctic Slate": {
                "window_bg": "#0f1115",
                "card_bg": "#181c24",
                "border": "#2e3545",
                "accent": "#60a5fa",
                "text": "#e2e8f0"
            }
        }
        
        if name_clean in presets:
            colors = presets[name_clean]
            for key, color in colors.items():
                self.custom_colors[key]["color"] = color
                self.custom_colors[key]["preview"].setStyleSheet(f"background-color: {color}; border: 1px solid #475569; border-radius: 4px;")
            self.update_live_preview()
            
    def pick_custom_color(self, key):
        current_hex = self.custom_colors[key]["color"]
        color = QColorDialog.getColor(QColor(current_hex), self, f"Select {self.custom_colors[key]['label']}")
        if color.isValid():
            hex_name = color.name()
            self.custom_colors[key]["color"] = hex_name
            self.custom_colors[key]["preview"].setStyleSheet(f"background-color: {hex_name}; border: 1px solid #475569; border-radius: 4px;")
            
            # De-select preset combo since color is now custom
            self.combo_theme_presets.blockSignals(True)
            self.combo_theme_presets.setCurrentIndex(-1)
            self.combo_theme_presets.blockSignals(False)
            
            self.update_live_preview()
            
    def update_live_preview(self):
        win_bg = self.custom_colors["window_bg"]["color"]
        card_bg = self.custom_colors["card_bg"]["color"]
        border = self.custom_colors["border"]["color"]
        accent = self.custom_colors["accent"]["color"]
        text = self.custom_colors["text"]["color"]
        
        preview_board = self.findChild(QFrame, "theme_preview_board")
        if preview_board:
            preview_board.setStyleSheet(f"""
                QFrame#theme_preview_board {{
                    background-color: {win_bg};
                    border: 1px solid {border};
                    border-radius: 8px;
                }}
            """)
            
        if hasattr(self, "preview_card") and self.preview_card:
            self.preview_card.setStyleSheet(f"""
                QFrame#preview_card {{
                    background-color: {card_bg};
                    border: 1px solid {border};
                    border-radius: 6px;
                }}
                QLabel {{
                    color: {text};
                    border: none;
                    background: transparent;
                }}
                QLabel#preview_card_lbl {{
                    color: {accent};
                    font-weight: bold;
                }}
            """)
            
        if hasattr(self, "preview_card_btn") and self.preview_card_btn:
            self.preview_card_btn.setStyleSheet(f"""
                QPushButton#preview_card_btn {{
                    background-color: {card_bg};
                    border: 1px solid {border};
                    color: {accent};
                    font-weight: bold;
                    padding: 6px 14px;
                    border-radius: 4px;
                }}
                QPushButton#preview_card_btn:hover {{
                    background-color: {win_bg};
                    border-color: {accent};
                }}
                QPushButton#preview_card_btn:pressed {{
                    background-color: {accent};
                    color: {win_bg};
                }}
            """)
            
    def show_test_alert(self):
        QMessageBox.information(self, "🔔 알림창 테마 동기화 테스트", "사용자가 커스텀 설정한 테마 컬러와 일관되게 어울리는 멋진 다크 스타일시트(QMessageBox)가 적용되었습니다!")

    def apply_setup_tab_theme(self):
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
        
        self.setStyleSheet(f"""
            WorkspaceSetupTab {{ background-color: {win_bg}; }}
            QWidget {{ color: {text}; font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; }}
            
            /* Tabs styling */
            QTabWidget::pane {{ border: 1px solid {border}; background-color: {card_bg}; border-radius: 6px; top: -1px; }}
            QTabWidget > QWidget {{ background-color: {card_bg}; }}
            QTabBar::tab {{ background-color: {win_bg}; color: {text}; padding: 8px 20px; border: 1px solid {border}; border-bottom: none; font-weight: bold; border-top-left-radius: 4px; border-top-right-radius: 4px;}}
            QTabBar::tab:selected {{ background-color: {card_bg}; color: {accent}; border-bottom: 2px solid {accent}; }}
            QTabBar::tab:hover {{ color: #ffffff; background-color: {border}; }}
            
            /* ScrollArea styling */
            QScrollArea {{ background-color: {card_bg}; border: none; }}
            QScrollArea::viewport {{ background-color: {card_bg}; }}
            #right_container {{ background-color: {card_bg}; }}
            
            /* Nested widgets inside tabs */
            QWidget#left_panel {{ background-color: {win_bg}; border-right: 1px solid {border}; }}
            
            /* Table Widget */
            QTableWidget {{ background-color: {win_bg}; border: 1px solid {border}; gridline-color: {border}; border-radius: 4px; color: #ffffff; }}
            QHeaderView::section {{ background-color: {card_bg}; color: {accent}; font-weight: bold; border: 1px solid {border}; padding: 4px; }}
            QTableWidget QTableCornerButton::section {{ background-color: {card_bg}; border: 1px solid {border}; }}
            
            /* List Widget (Subsystems List View) */
            QListWidget {{
                background-color: {win_bg};
                border: 1px solid {border};
                border-radius: 4px;
                color: #ffffff;
                padding: 4px;
            }}
            QListWidget::item {{
                padding: 8px 12px;
                border-bottom: 1px solid {card_bg};
                border-radius: 3px;
                font-weight: bold;
                color: {text};
            }}
            QListWidget::item:hover {{
                background-color: {card_bg};
                color: {accent};
            }}
            QListWidget::item:selected {{
                background-color: {border};
                color: #ffffff;
                border-left: 3px solid {accent};
            }}
            
            /* Inputs & Controls */
            QLineEdit, QComboBox, QDoubleSpinBox, QSpinBox {{ background-color: {win_bg}; border: 1px solid {border}; border-radius: 4px; padding: 4px; color: #ffffff; }}
            QLineEdit:focus, QComboBox:focus, QDoubleSpinBox:focus, QSpinBox:focus {{ border-color: {accent}; }}
            QComboBox QAbstractItemView {{ background-color: {card_bg}; border: 1px solid {border}; selection-background-color: {win_bg}; selection-color: {accent}; color: #ffffff; }}
            
            /* Group Box */
            QGroupBox {{ border: 1px solid {border}; border-radius: 6px; margin-top: 10px; padding-top: 12px; font-weight: bold; color: {accent}; }}
            QGroupBox::title {{ subcontrol-origin: margin; subcontrol-position: top left; left: 10px; padding: 0 5px; background-color: {card_bg}; }}
            
            /* Buttons */
            QPushButton {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 4px; color: {accent}; font-weight: bold; padding: 6px 14px; }}
            QPushButton:hover {{ background-color: {win_bg}; border-color: {accent}; }}
            QPushButton:pressed {{ background-color: {accent}; color: {win_bg}; }}
            
            /* Checkboxes */
            QCheckBox {{ spacing: 6px; font-weight: bold; color: {text}; background: transparent; }}
            QCheckBox::indicator {{ width: 13px; height: 13px; border: 1px solid {border}; background-color: {win_bg}; border-radius: 3px; }}
            QCheckBox::indicator:checked {{ background-color: {accent}; border-color: {accent}; }}
            
            /* Labels */
            QLabel {{ background: transparent; }}
            
            /* Workspace Config Tab Specific Headers */
            QWidget#header_widget {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 6px; }}
            QLabel#title_lbl {{ font-size: 15px; font-weight: bold; color: {accent}; border: none; background: transparent; }}
            QLabel#desc_lbl {{ font-size: 11px; color: {text}; border: none; background: transparent; }}
            
            /* Workspace Config Tab Footer Panel */
            QWidget#footer_panel {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 6px; }}
            QPushButton#btn_apply {{ background-color: {accent}; border: 1px solid {accent}; color: {win_bg}; font-weight: bold; padding: 6px 18px; }}
            QPushButton#btn_apply:hover {{ background-color: {card_bg}; color: {accent}; }}
            QPushButton#btn_save_as {{ background-color: {card_bg}; border: 1px solid {border}; color: {accent}; font-weight: bold; padding: 6px 18px; }}
            QPushButton#btn_save_as:hover {{ background-color: {win_bg}; border-color: {accent}; }}
            QPushButton#btn_load, QPushButton#btn_presets, QPushButton#btn_revert {{ background-color: {card_bg}; border: 1px solid {border}; color: {text}; font-weight: bold; padding: 6px 18px; }}
            QPushButton#btn_load:hover, QPushButton#btn_presets:hover, QPushButton#btn_revert:hover {{ background-color: {win_bg}; border-color: {accent}; color: #ffffff; }}
            
            /* IDE Plugins Manager Styles */
            QLineEdit#plugin_search {{ background-color: {win_bg}; border: 1px solid {border}; border-radius: 4px; padding: 8px 12px; color: #ffffff; font-size: 11px; }}
            QLineEdit#plugin_search:focus {{ border-color: {accent}; }}
            
            QPushButton#btn_cat_installed, QPushButton#btn_cat_market {{ background-color: {card_bg}; border: 1px solid {border}; color: {text}; font-size: 10px; font-weight: bold; padding: 5px; border-radius: 4px; }}
            QPushButton#btn_cat_installed:checked, QPushButton#btn_cat_market:checked {{ background-color: {accent}; border-color: {accent}; color: {win_bg}; }}
            
            QPushButton#btn_manual {{ background-color: {card_bg}; border: 1px dashed {border}; color: {text}; font-size: 10px; padding: 6px; border-radius: 4px; }}
            QPushButton#btn_manual:hover {{ border-color: {accent}; color: #ffffff; }}
            
            QWidget#plugin_detail_panel {{ background-color: {win_bg}; border: 1px solid {border}; border-radius: 6px; }}
            QLabel#lbl_plugin_name {{ font-size: 16px; font-weight: bold; color: {accent}; border: none; background: transparent; }}
            QLabel#lbl_plugin_meta {{ font-size: 10px; color: {text}; border: none; background: transparent; }}
            
            /* Dynamic Action Toggle switch inside Plugin detail view */
            QPushButton#btn_toggle_active {{
                background-color: {accent}50; border: 1px solid {accent}; color: white; padding: 8px 16px; font-weight: bold; font-size: 11px; border-radius: 4px;
            }}
            QPushButton#btn_toggle_active:!checked {{
                background-color: {card_bg}; border-color: {border}; color: {text};
            }}
            QPushButton#btn_plugin_del {{ background-color: #7f1d1d; border: 1px solid #f87171; color: white; padding: 8px 16px; font-weight: bold; font-size: 11px; border-radius: 4px; }}
            QPushButton#btn_plugin_del:hover {{ background-color: #b91c1c; }}
            QPushButton#btn_plugin_inst {{ background-color: {accent}; border: 1px solid {accent}; color: {win_bg}; padding: 10px 20px; font-weight: bold; font-size: 11px; border-radius: 4px; }}
            QPushButton#btn_plugin_inst:hover {{ background-color: {card_bg}; color: {accent}; }}
        """)

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

        # 4. Save Theme configuration
        theme_cfg = {}
        for key, val in self.custom_colors.items():
            theme_cfg[key] = val["color"]
        self.config_data["theme_config"] = theme_cfg
        
        # Save and Apply to main window configuration
        self.main_window.apply_new_workspace_configuration(self.config_data)
        QMessageBox.information(self, "성공", "워크스페이스 설정 및 테마 구성이 성공적으로 적용되어 저장되었습니다!")
        
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
        "mcu_terminal",
        "topology_visualizer"
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
        "mcu_terminal",
        "topology_visualizer"
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
        "mcu_terminal",
        "topology_visualizer"
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


class WindowLayoutGuideDialog(QDialog):
    """
    A beautiful dark-themed help dialog explaining how PyQt6 QDockWidget docking, splitting, 
    and floating works to maximize user flexibility and workflow layout control.
    """
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("📐 대시보드 윈도우 레이아웃 배치 가이드")
        self.resize(500, 380)
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
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(15)

        title_lbl = QLabel("📐 PyQt6 QDockWidget 배치 및 분리 요령")
        title_lbl.setStyleSheet("font-size: 16px; font-weight: bold; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # Divider
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        line.setStyleSheet("background-color: #272a38; max-height: 1px;")
        layout.addWidget(line)

        # Guide Content
        guide_text = (
            "<b>1. 윈도우 분리 (Float)</b><br/>"
            "각 윈도우의 상단 타이틀 바를 더블 클릭하거나 마우스로 클릭하여 대시보드 밖으로 드래그하면, "
            "자유롭게 분리하여 보조 모니터로 이동시킬 수 있는 플로팅 윈도우가 됩니다.<br/><br/>"
            "<b>2. 도킹 및 중첩 (Tab Stack & Side-by-Side)</b><br/>"
            "분리된 윈도우를 다시 대시보드 메인 화면 안으로 가져가 모서리 영역에 갖다 대면, "
            "상/하/좌/우 원하는 그리드 영역으로 깔끔하게 도킹됩니다. "
            "만약 다른 윈도우의 한가운데로 드래그하면 <b>탭 형태로 중첩(Tabbed Docking)</b>하여 공간을 획기적으로 절약할 수 있습니다.<br/><br/>"
            "<b>3. 윈도우 닫기 및 재활성화</b><br/>"
            "사용하지 않는 윈도우는 우측 상단의 'X'를 눌러 닫을 수 있으며, 홈 Ribbon의 "
            "<b>'⚡ 퀵 위젯/윈도우 추가'</b> 그룹의 각 버튼을 누르면 언제든지 즉시 다시 화면에 도킹되어 활성화됩니다.<br/><br/>"
            "<b>4. 레이아웃 리셋</b><br/>"
            "도킹 레이아웃이 꼬였거나 초기 정돈된 상태로 돌아가고 싶다면 홈 리본의 <b>'🎛️ Standard Docks Layout reset'</b> 버튼을 눌러주세요."
        )
        content_lbl = QLabel(guide_text)
        content_lbl.setWordWrap(True)
        content_lbl.setStyleSheet("font-size: 11px; line-height: 1.6; color: #a0a5b5;")
        layout.addWidget(content_lbl)

        layout.addStretch()

        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        btn_ok = QPushButton("이해했습니다")
        btn_ok.clicked.connect(self.accept)
        btn_layout.addWidget(btn_ok)
        layout.addLayout(btn_layout)


class PluginFilterDialog(QDialog):
    """
    A premium dark-themed dialog for filtering which subsystems and variables/labels
    are displayed in a specific dock widget.
    """
    def __init__(self, plugin, parent=None):
        super().__init__(parent)
        self.plugin = plugin
        self.main_window = plugin.main_window
        self.setWindowTitle(f"⚙️ {plugin.name} 표시 필터 설정")
        self.resize(380, 480)
        self.setWindowFlags(self.windowFlags() & ~Qt.WindowType.WindowContextHelpButtonHint)
        
        # Initialize default filter states if empty
        router = self.main_window.data_router
        if not hasattr(self.plugin, "visible_subsystems") or not self.plugin.visible_subsystems:
            self.plugin.visible_subsystems = set(router.subsystems.keys())
        if not hasattr(self.plugin, "visible_variables") or not self.plugin.visible_variables:
            all_vars = set()
            for sub in router.subsystems.values():
                for v in sub.variables:
                    all_vars.add(v["name"])
            self.plugin.visible_variables = all_vars
            
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
                font-weight: bold;
                font-size: 11px;
            }
            QCheckBox {
                color: #a0a5b5;
                font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
                font-size: 11px;
                spacing: 8px;
            }
            QCheckBox:hover {
                color: #ffffff;
            }
            QPushButton {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
                color: #38bdf8;
                font-weight: bold;
                padding: 8px 16px;
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
            QScrollArea {
                background-color: #12131a;
                border: 1px solid #272a38;
                border-radius: 4px;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        title_lbl = QLabel(f"🎯 표시 대상 시스템 및 라벨 선택")
        title_lbl.setStyleSheet("font-size: 14px; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # 1. Subsystems Checklist
        layout.addWidget(QLabel("📂 표시할 서브시스템(시스템 포함) 선택:"))
        self.sub_scroll = QScrollArea()
        self.sub_scroll.setWidgetResizable(True)
        sub_content = QWidget()
        sub_content.setStyleSheet("background: transparent;")
        sub_lay = QVBoxLayout(sub_content)
        sub_lay.setContentsMargins(8, 8, 8, 8)
        
        self.sub_boxes = {}
        router = self.main_window.data_router
        for sub_name, sub in router.subsystems.items():
            chk = QCheckBox(f"{sub.display_name} ({sub_name})")
            is_checked = sub_name in self.plugin.visible_subsystems
            chk.setChecked(is_checked)
            sub_lay.addWidget(chk)
            self.sub_boxes[sub_name] = chk
        sub_lay.addStretch()
        self.sub_scroll.setWidget(sub_content)
        layout.addWidget(self.sub_scroll)

        # 2. Variables / Labels Checklist
        layout.addWidget(QLabel("🏷️ 표시할 데이터 라벨(변수) 선택:"))
        self.var_scroll = QScrollArea()
        self.var_scroll.setWidgetResizable(True)
        var_content = QWidget()
        var_content.setStyleSheet("background: transparent;")
        var_lay = QVBoxLayout(var_content)
        var_lay.setContentsMargins(8, 8, 8, 8)

        self.var_boxes = {}
        all_var_names = set()
        for sub in router.subsystems.values():
            for v in sub.variables:
                all_var_names.add(v["name"])

        for v_name in sorted(list(all_var_names)):
            chk = QCheckBox(v_name)
            is_checked = v_name in self.plugin.visible_variables
            chk.setChecked(is_checked)
            var_lay.addWidget(chk)
            self.var_boxes[v_name] = chk
        var_lay.addStretch()
        self.var_scroll.setWidget(var_content)
        layout.addWidget(self.var_scroll)

        # Buttons
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        
        btn_apply = QPushButton("적용")
        btn_apply.setObjectName("btn_primary")
        btn_apply.clicked.connect(self.apply_filter)
        
        btn_cancel = QPushButton("취소")
        btn_cancel.clicked.connect(self.reject)

        btn_layout.addWidget(btn_apply)
        btn_layout.addWidget(btn_cancel)
        layout.addLayout(btn_layout)

    def apply_filter(self):
        # Update plugin filter sets
        self.plugin.visible_subsystems = {name for name, chk in self.sub_boxes.items() if chk.isChecked()}
        self.plugin.visible_variables = {name for name, chk in self.var_boxes.items() if chk.isChecked()}

        # Trigger plugin UI rebuild
        if hasattr(self.plugin, "rebuild_plots"):
            self.plugin.rebuild_plots()
        if hasattr(self.plugin, "rebuild_ui"):
            self.plugin.rebuild_ui()
            
        self.accept()


class QuickWindowCreateDialog(QDialog):
    """
    A premium dark-themed dialog for creating/enabling telemetry widget windows.
    Consolidates multiple buttons into a single clean launcher with detailed dropdown controls.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.main_window = main_window
        self.setWindowTitle("➕ 윈도우/위젯 생성 마법사")
        self.resize(460, 360)
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
                font-size: 11px;
            }
            QComboBox {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 4px;
                padding: 6px;
                color: #38bdf8;
                font-weight: bold;
                font-size: 11px;
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
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        # Header Title
        title_lbl = QLabel("➕ 윈도우/위젯 생성 및 배치")
        title_lbl.setStyleSheet("font-size: 15px; font-weight: bold; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # Divider
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        line.setStyleSheet("background-color: #272a38; max-height: 1px;")
        layout.addWidget(line)

        # Form Layout
        form_lay = QGridLayout()
        form_lay.setSpacing(10)

        # 1. Widget Type Selection
        form_lay.addWidget(QLabel("1. 위젯/윈도우 유형 (Widget Type):"), 0, 0)
        self.combo_type = QComboBox()
        self.combo_type.addItem("📊 실시간 파형 차트 스코프 (Trend Charts)", "trend_charts")
        self.combo_type.addItem("📟 텍스트 수치 카드 (Telemetry Cards)", "telemetry_cards")
        self.combo_type.addItem("🔌 프로토콜 패킷 분석기 (Protocol Analyzer)", "protocol_analyzer")
        self.combo_type.addItem("⚡ 노드 토폴로지 맵 (Topology Visualizer)", "topology_visualizer")
        self.combo_type.setView(QListView())
        self.combo_type.currentIndexChanged.connect(self.update_description)
        form_lay.addWidget(self.combo_type, 0, 1)

        # 2. Target Subsystem Selection
        form_lay.addWidget(QLabel("2. 연동할 서브시스템 (Target Subsystem):"), 1, 0)
        self.combo_sub = QComboBox()
        self.combo_sub.addItem("전체 서브시스템 (All Nodes)", "ALL")
        for s in self.main_window.config_data.get("subsystems", []):
            self.combo_sub.addItem(f"{s.get('display_name', s['name'])} ({s['name']})", s["name"])
        self.combo_sub.setView(QListView())
        form_lay.addWidget(self.combo_sub, 1, 1)

        layout.addLayout(form_lay)

        # Description / Details box
        self.desc_box = QLabel()
        self.desc_box.setWordWrap(True)
        self.desc_box.setStyleSheet("background-color: #12131a; border: 1px solid #272a38; border-radius: 4px; padding: 12px; font-size: 11px; line-height: 1.5; color: #8e94a6;")
        layout.addWidget(self.desc_box)
        
        layout.addStretch()

        # Buttons
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(10)
        btn_layout.addStretch()

        self.btn_create = QPushButton("🚀 윈도우 생성 및 도킹")
        self.btn_create.setObjectName("btn_primary")
        self.btn_create.clicked.connect(self.create_window)

        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.clicked.connect(self.reject)

        btn_layout.addWidget(self.btn_create)
        btn_layout.addWidget(self.btn_cancel)
        layout.addLayout(btn_layout)

        self.update_description()

    def update_description(self):
        plugin_id = self.combo_type.currentData()
        if plugin_id == "trend_charts":
            self.desc_box.setText(
                "<b>📊 실시간 파형 차트 스코프</b><br/>"
                "PyqtGraph 기반의 대용량 고성능 멀티플 스코프 차트입니다. "
                "선택한 서브시스템에서 유입되는 센서/제어기 변수들의 실시간 변동 파형을 시계열 플롯으로 실시간 추적합니다."
            )
        elif plugin_id == "telemetry_cards":
            self.desc_box.setText(
                "<b>📟 텍스트 수치 카드</b><br/>"
                "가장 중요한 MOSFET 온도, VIN 전압, LOAD 전류 등 핵심 변수를 대형 글씨 카드 뷰로 나타냅니다. "
                "임계 한계치(OVP/OCP/OTP) 돌파 시의 비주얼 긴급 경보 기능이 내장되어 있습니다."
            )
        elif plugin_id == "protocol_analyzer":
            self.desc_box.setText(
                "<b>🔌 프로토콜 패킷 분석기</b><br/>"
                "시리얼 VCP 포트로 흘러 들어오는 로우 HEX 바이트 및 텍스트 프레임을 스니핑하여, "
                "헤더, ID, 명령, 페이로드 바이트 단위로 네온 컬러스럽게 하이라이팅 분석해 줍니다."
            )
        elif plugin_id == "topology_visualizer":
            self.desc_box.setText(
                "<b>⚡ 노드 토폴로지 맵</b><br/>"
                "전체 수집 서브시스템 노드들과 연산 수식 노드(Math Node), 조건 노드(Trigger Node) 간의 연결 관계를 "
                "황금 점선 및 은빛 입자 흐름으로 유기적으로 렌더링하고 직관적으로 수식을 배치하게 해줍니다."
            )

    def create_window(self):
        plugin_id = self.combo_type.currentData()
        sub_id = self.combo_sub.currentData()

        # Enable plugin dynamically and update persistent profile choice
        success = self.main_window.plugin_manager.toggle_plugin(plugin_id, True)
        if success:
            # Sync checking in settings tab
            self.main_window.sync_central_tabs()
            
            # If a specific subsystem focus was requested, try to apply it to the plugin if supported
            p_inst = self.main_window.plugin_manager.active_plugins.get(plugin_id)
            if p_inst and hasattr(p_inst, "focus_subsystem") and callable(p_inst.focus_subsystem):
                p_inst.focus_subsystem(sub_id)

            QMessageBox.information(
                self,
                "윈도우 생성 완료",
                f"선택한 위젯 윈도우가 활성화되어 화면 하단에 안전하게 도킹 전개되었습니다!\n\n"
                f"- 도킹 윈도우: '{self.combo_type.currentText().split('(')[0].strip()}'\n"
                f"- 연동 모니터링 노드: '{self.combo_sub.currentText()}'\n\n"
                f"윈도우 상단 타이틀바를 드래그하여 원하는 영역에 중첩 도킹하거나 독립 윈도우로 분리(Float)해 사용할 수 있습니다."
            )
            self.accept()
        else:
            QMessageBox.critical(self, "오류", f"윈도우 위젯 '{plugin_id}' 활성화에 실패했습니다.")


class QuickSubsystemDialog(QDialog):
    """
    An all-in-one minimal dialog for adding a new logical subsystem, VCP mapping, and CSV routing,
    incorporating a smart default variables template for EV converter telemetry nodes.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.main_window = main_window
        self.setWindowTitle("⚡ 퀵 Subsystem 마법사")
        self.resize(520, 640)
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
                font-size: 11px;
            }
            QLineEdit, QComboBox {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 4px;
                padding: 6px;
                color: #38bdf8;
                font-weight: bold;
                font-size: 11px;
            }
            QLineEdit:focus, QComboBox:focus {
                border-color: #38bdf8;
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
                padding: 8px 16px;
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
            QCheckBox {
                color: #a2a2b5;
                font-weight: bold;
                font-size: 11px;
            }
            QCheckBox::indicator {
                width: 13px;
                height: 13px;
                border: 1px solid #272a38;
                background-color: #0b0b0d;
                border-radius: 3px;
            }
            QCheckBox::indicator:checked {
                background-color: #38bdf8;
                border-color: #38bdf8;
            }
            QScrollArea {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
            }
            QScrollArea::viewport {
                background-color: #1b1c24;
            }
            QWidget#sys_widget_container, QWidget#vars_widget_container {
                background-color: #1b1c24;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(10)

        # Header Title
        title_lbl = QLabel("🚀 퀵 Subsystem 마법사 (초스피드 노드 구성)")
        title_lbl.setStyleSheet("font-size: 15px; font-weight: bold; color: #38bdf8;")
        layout.addWidget(title_lbl)

        # Distinct System vs Subsystem Explanation
        desc_box = QLabel(
            "📢 <b>시스템(System) vs 서브시스템(Subsystem) 구분 안내</b><br/>"
            "• <b>시스템(System)</b>: COM3, COM4 등 실제 데이터를 송출하는 물리/가상 하드웨어 포트입니다.<br/>"
            "• <b>서브시스템(Subsystem)</b>: 시스템으로부터 수신한 개별 신호들을 논리적으로 조합하고 가공하여 대시보드(차트/카드)에 시각화하는 모니터링 노드입니다."
        )
        desc_box.setWordWrap(True)
        desc_box.setStyleSheet("""
            background-color: #11131e;
            border: 1px solid #3b82f6;
            border-radius: 6px;
            padding: 10px;
            font-size: 10px;
            line-height: 1.4;
            color: #93c5fd;
        """)
        layout.addWidget(desc_box)

        # Divider
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        line.setStyleSheet("background-color: #272a38; max-height: 1px;")
        layout.addWidget(line)

        # Form Layout
        form_lay = QGridLayout()
        form_lay.setSpacing(8)

        form_lay.addWidget(QLabel("1. 서브시스템 ID (영문 고유식별명):"), 0, 0)
        self.txt_sub_id = QLineEdit("MySubsystemNode")
        form_lay.addWidget(self.txt_sub_id, 0, 1)

        form_lay.addWidget(QLabel("2. 화면 표시 명칭 (Display Label):"), 1, 0)
        self.txt_display_name = QLineEdit("⚡ My Subsystem Node")
        form_lay.addWidget(self.txt_display_name, 1, 1)

        # Port / VCP System Selection
        form_lay.addWidget(QLabel("3. 연동할 대상 시스템(COM 포트) 선택:"), 2, 0)
        self.systems_area = QScrollArea()
        self.systems_area.setFixedHeight(75)
        self.systems_area.setWidgetResizable(True)
        self.systems_area.setStyleSheet("""
            QScrollArea {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
            }
            QScrollArea::viewport {
                background-color: #1b1c24;
            }
        """)
        sys_widget = QWidget()
        sys_widget.setObjectName("sys_widget_container")
        sys_widget.setStyleSheet("background-color: #1b1c24;")
        self.sys_lay = QVBoxLayout(sys_widget)
        self.sys_lay.setContentsMargins(8, 8, 8, 8)
        
        # Scan active COM ports
        import serial.tools.list_ports
        comports_list = serial.tools.list_ports.comports()
        self.sys_checkboxes = []
        
        ports = [p.device for p in comports_list]
        ports_desc = {p.device: f"{p.device}: {p.description}" for p in comports_list}
        
        if not ports:
            ports = ["COM3", "COM4", "COM5"]
            ports_desc = {
                "COM3": "COM3: Mock STM32 Engine controller",
                "COM4": "COM4: Mock ESP32 Battery sensor node",
                "COM5": "COM5: Mock Auxiliary sensor stream"
            }
            
        for port in ports:
            chk = QCheckBox(ports_desc.get(port, port))
            chk.setProperty("port_name", port)
            chk.setStyleSheet("color: #e2e8f0; font-weight: bold; background: transparent;")
            if len(self.sys_checkboxes) == 0:
                chk.setChecked(True)
            # When target port is toggled, update visible variables checklist dynamically!
            chk.stateChanged.connect(lambda state: self.update_variables_list())
            self.sys_lay.addWidget(chk)
            self.sys_checkboxes.append(chk)
            
        self.sys_lay.addStretch()
        self.systems_area.setWidget(sys_widget)
        form_lay.addWidget(self.systems_area, 2, 1)

        # Variables Selection Scroll Area
        form_lay.addWidget(QLabel("4. 가져올 시스템 신호(변수) 선택:"), 3, 0)
        self.vars_area = QScrollArea()
        self.vars_area.setFixedHeight(120)
        self.vars_area.setWidgetResizable(True)
        self.vars_area.setStyleSheet("""
            QScrollArea {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 6px;
            }
            QScrollArea::viewport {
                background-color: #1b1c24;
            }
        """)
        vars_widget = QWidget()
        vars_widget.setObjectName("vars_widget_container")
        vars_widget.setStyleSheet("background-color: #1b1c24;")
        self.vars_lay = QVBoxLayout(vars_widget)
        self.vars_lay.setContentsMargins(8, 8, 8, 8)
        self.vars_area.setWidget(vars_widget)
        form_lay.addWidget(self.vars_area, 3, 1)

        # Variables Selection Helper Buttons
        var_btns_lay = QHBoxLayout()
        btn_sel_all = QPushButton("전체 선택")
        btn_sel_all.setStyleSheet("padding: 2px 6px; font-size: 10px; background-color: #1e1e2d;")
        btn_sel_all.clicked.connect(self.select_all_vars)
        btn_clear_all = QPushButton("전체 해제")
        btn_clear_all.setStyleSheet("padding: 2px 6px; font-size: 10px; background-color: #1e1e2d;")
        btn_clear_all.clicked.connect(self.clear_all_vars)
        var_btns_lay.addWidget(btn_sel_all)
        var_btns_lay.addWidget(btn_clear_all)
        var_btns_lay.addStretch()
        form_lay.addLayout(var_btns_lay, 4, 1)

        form_lay.addWidget(QLabel("5. 라우팅 패킷 시그니처 (Prefix):"), 5, 0)
        self.txt_prefix = QLineEdit("MYSUB")
        form_lay.addWidget(self.txt_prefix, 5, 1)

        layout.addLayout(form_lay)

        # Guide/Hint Box
        hint_lbl = QLabel(
            "💡 <b>스마트 패킷 데이터 바인딩</b><br/>"
            "선택한 시스템의 신호들은 딥 카피를 거쳐 새 서브시스템의 독자적 계측 신호로 등록됩니다. "
            "신호들의 컬럼 인덱스(column_index)는 수신 정합성을 위해 0부터 순차 중복 없이 자동 재부여됩니다."
        )
        hint_lbl.setWordWrap(True)
        hint_lbl.setStyleSheet("background-color: #12131a; border: 1px solid #272a38; border-radius: 4px; padding: 10px; font-size: 10px; line-height: 1.5; color: #8e94a6;")
        layout.addWidget(hint_lbl)

        layout.addStretch()

        # Buttons
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(10)
        btn_layout.addStretch()

        self.btn_create = QPushButton("🚀 만들고 즉시 화면에 띄우기")
        self.btn_create.setObjectName("btn_primary")
        self.btn_create.clicked.connect(self.create_and_apply)

        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.clicked.connect(self.reject)

        btn_layout.addWidget(self.btn_create)
        btn_layout.addWidget(self.btn_cancel)
        layout.addLayout(btn_layout)

        # Initial available signals and checkbox updates
        self.all_available_signals = self.get_available_signals()
        self.update_variables_list()

    def get_available_signals(self):
        signals = []
        routing_rules = self.main_window.config_data.get("routing_rules", [])
        subsystems = self.main_window.config_data.get("subsystems", [])
        
        for sub in subsystems:
            sub_name = sub["name"]
            sub_disp = sub.get("display_name", sub_name)
            
            # 1. Look up ports mapped in routing rules
            mapped_ports = [rule["port"] for rule in routing_rules if rule.get("target") == sub_name]
            
            # 2. Look up systems directly listed in the subsystem
            for p in sub.get("systems", []):
                if p not in mapped_ports:
                    mapped_ports.append(p)
                    
            # 3. Infer from subsystem ID / name (e.g. AutoSub_COM3 -> COM3)
            import re
            for p in re.findall(r'COM\d+', sub_name):
                if p not in mapped_ports:
                    mapped_ports.append(p)
                    
            # 4. Infer from subsystem display name (e.g. Auto Node COM3 -> COM3)
            for p in re.findall(r'COM\d+', sub_disp):
                if p not in mapped_ports:
                    mapped_ports.append(p)
                    
            # 5. Default fallback for ArduinoNode to COM6
            if not mapped_ports and sub_name == "ArduinoNode":
                mapped_ports = ["COM6"]
                
            port_str = ", ".join(mapped_ports) if mapped_ports else "Unmapped"
            source_label = f"System {port_str} ({sub_disp})"
            
            for var in sub.get("variables", []):
                signals.append({
                    "source_label": source_label,
                    "ports": mapped_ports,
                    "var": var
                })
                
        if not signals:
            templates = [
                {"name": "temp", "display_name": "Core Temperature", "unit": "°C", "gain": 1.0, "offset": 0.0, "is_numerical": True, "column_index": "0"},
                {"name": "voltage", "display_name": "Voltage Input", "unit": "V", "gain": 1.0, "offset": 0.0, "is_numerical": True, "column_index": "1"},
                {"name": "current", "display_name": "Current Load", "unit": "A", "gain": 1.0, "offset": 0.0, "is_numerical": True, "column_index": "2"},
                {"name": "rpm", "display_name": "Engine RPM", "unit": "RPM", "gain": 1.0, "offset": 0.0, "is_numerical": True, "column_index": "0"},
                {"name": "efficiency", "display_name": "System Efficiency", "unit": "%", "gain": 1.0, "offset": 0.0, "is_numerical": True, "column_index": "1"}
            ]
            for t in templates:
                signals.append({
                    "source_label": "Default Template",
                    "ports": [],
                    "var": t
                })
        return signals

    def update_variables_list(self):
        # Clear old variable checkboxes
        while self.vars_lay.count():
            item = self.vars_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        self.vars_checkboxes = []
        
        # Retrieve checked COM ports/systems
        selected_ports = [chk.property("port_name") for chk in self.sys_checkboxes if chk.isChecked()]
        
        # Filter and add signals
        for sig in self.all_available_signals:
            var_info = sig["var"]
            source = sig["source_label"]
            sig_ports = sig.get("ports", [])
            
            # Show if:
            # 1. No ports associated with the signal (default template)
            # 2. Or there is an overlap between sig_ports and selected_ports
            if not sig_ports or any(p in selected_ports for p in sig_ports):
                lbl_txt = f"[{source}] {var_info['name']} ({var_info.get('display_name', '')}) {var_info.get('unit', '')}"
                chk = QCheckBox(lbl_txt)
                chk.setProperty("var_dict", var_info)
                chk.setChecked(True)
                chk.setStyleSheet("color: #e2e8f0; font-weight: bold; background: transparent;")
                self.vars_lay.addWidget(chk)
                self.vars_checkboxes.append(chk)
                
        self.vars_lay.addStretch()

    def select_all_vars(self):
        for chk in self.vars_checkboxes:
            chk.setChecked(True)
            
    def clear_all_vars(self):
        for chk in self.vars_checkboxes:
            chk.setChecked(False)

    def create_and_apply(self):
        sub_id = self.txt_sub_id.text().strip()
        disp_name = self.txt_display_name.text().strip()
        prefix = self.txt_prefix.text().strip()

        selected_ports = [chk.property("port_name") for chk in self.sys_checkboxes if chk.isChecked()]

        # Retrieve checked variables
        selected_variables = []
        for chk in self.vars_checkboxes:
            if chk.isChecked():
                orig_var = chk.property("var_dict")
                new_var = {
                    "name": orig_var["name"],
                    "display_name": orig_var.get("display_name", orig_var["name"]),
                    "column_index": str(len(selected_variables)), # Sequence from 0
                    "gain": orig_var.get("gain", 1.0),
                    "offset": orig_var.get("offset", 0.0),
                    "unit": orig_var.get("unit", ""),
                    "is_numerical": orig_var.get("is_numerical", True)
                }
                selected_variables.append(new_var)

        if not sub_id or not disp_name or not selected_ports or not prefix:
            QMessageBox.warning(self, "입력 오류", "모든 항목을 올바르게 기재하고 연동할 시스템을 하나 이상 선택해야 합니다.")
            return

        if not selected_variables:
            QMessageBox.warning(self, "입력 오류", "서브시스템에 포함할 시스템 신호(변수)를 하나 이상 선택해야 합니다.")
            return

        # Check duplication
        for s in self.main_window.config_data.get("subsystems", []):
            if s["name"] == sub_id:
                QMessageBox.warning(self, "중복 오류", f"서브시스템 ID '{sub_id}'는 이미 존재합니다!")
                return

        # Identify safety thresholds automatically
        temp_var = next((v["name"] for v in selected_variables if "temp" in v["name"].lower()), "")
        volt_var = next((v["name"] for v in selected_variables if "volt" in v["name"].lower()), "")
        curr_var = next((v["name"] for v in selected_variables if "curr" in v["name"].lower()), "")

        # Create subsystem structure
        new_sub = {
            "name": sub_id,
            "display_name": disp_name,
            "systems": selected_ports,
            "variables": selected_variables,
            "thresholds": {
                "ovp_var": volt_var,
                "ovp_val": 100.0,
                "ocp_var": curr_var,
                "ocp_val": 50.0,
                "otp_var": temp_var,
                "otp_val": 85.0
            },
            "temp_mosfet_var": temp_var,
            "temp_transformer_var": ""
        }
        self.main_window.config_data.setdefault("subsystems", []).append(new_sub)

        # Loop and register rules for each checked system port
        for port in selected_ports:
            port_exists = False
            for p in self.main_window.config_data.get("ports", []):
                if p["port"] == port:
                    port_exists = True
                    break
            if not port_exists:
                self.main_window.config_data.setdefault("ports", []).append({
                    "port": port,
                    "baudrate": 115200
                })

            self.main_window.config_data.setdefault("routing_rules", []).append({
                "port": port,
                "type": "PREFIX",
                "pattern": prefix,
                "target": sub_id
            })

        # Save config and apply
        self.main_window.apply_new_workspace_configuration(self.main_window.config_data)

        # Open charts and cards plugins automatically
        self.main_window.plugin_manager.enable_plugin("trend_charts")
        self.main_window.plugin_manager.enable_plugin("telemetry_cards")

        QMessageBox.information(
            self, 
            "성공", 
            f"서브시스템 '{disp_name}'가 성공적으로 생성 및 저장되었습니다!\n"
            f"선택한 시스템 [{', '.join(selected_ports)}] 및 패킷 헤더 [{prefix}]와 자동 연동되었습니다.\n"
            f"선택 신호 {len(selected_variables)}개가 순차 컬럼 매핑으로 딥 카피되었습니다."
        )
        self.accept()



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
        from PyQt6.QtGui import QIcon
        if os.path.exists("Logo_Gemini.png"):
            self.setWindowIcon(QIcon("Logo_Gemini.png"))
        self.setDockNestingEnabled(True)
        
        # GUI Global state flags
        self.plots_paused = False
        self.plot_auto_scale = True
        
        # Initialize Core Manager layers
        self.config_manager = ConfigManager()
        self.serial_manager = MultiPortSerialManager()
        self.data_router = DataRouter()
        self.data_router.main_window = self
        self.plugin_manager = PluginManager(self)
        self.serial_last_states = {}
        
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
        self.serial_manager.connection_status_changed.connect(self.on_serial_connection_changed)
        
        # Dynamically discover plugins and load
        self.plugin_manager.discover_plugins()
        self.load_plugins_from_profile()
        
        # Refresh and automatically launch active ports listening threads
        self.restart_serial_handlers_from_profile()
        
        # Diagnostic print
        self.log_to_diagnostic("INFO: Dashboard System initialisation completed successfully.")
        
        # Initialize Plug & Play processed ports set
        self.pnp_processed_ports = set()
        
        # Setup 2-second background timer for Plug & Play physical VCP ports auto-sensing
        self.pnp_timer = QTimer(self)
        self.pnp_timer.timeout.connect(self.scan_pnp_ports)
        self.pnp_timer.start(2000)

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
            group_frame.setObjectName("RibbonGroup")
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
        
        btn_quick_preset = QPushButton("📋 프리셋 로드")
        btn_quick_preset.setStyleSheet("color: #f59e0b; font-weight: bold;")
        btn_quick_preset.clicked.connect(lambda: self.setup_tab.open_preset_dialog() if hasattr(self, "setup_tab") else None)
        lay_spec.addWidget(btn_quick_preset, 1, 0)
        
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
        
        # Group D: ⚡ 퀵 위젯/윈도우 추가 (Quick Add Docks)
        frm_quick_add, lay_quick_add = create_ribbon_group("⚡ 퀵 위젯/윈도우 추가")
        
        btn_quick_create = QPushButton("➕ 윈도우 생성/추가")
        btn_quick_create.setStyleSheet("color: #38bdf8; font-weight: bold; padding: 10px;")
        btn_quick_create.clicked.connect(self.show_quick_window_dialog)
        lay_quick_add.addWidget(btn_quick_create, 0, 0, 2, 1) # span 2 rows
        
        btn_layout_help = QPushButton("📐 도움말")
        btn_layout_help.setStyleSheet("color: #a0a5b5; font-weight: bold; padding: 10px;")
        btn_layout_help.clicked.connect(self.show_layout_guide)
        lay_quick_add.addWidget(btn_layout_help, 0, 1, 2, 1) # span 2 rows
        
        tab_home_layout.addWidget(frm_quick_add)
        
        # Group E: 🚀 퀵 Subsystem 마법사 (Quick Subsystem Wizard)
        frm_quick_sub, lay_quick_sub = create_ribbon_group("🚀 퀵 Subsystem 마법사")
        
        btn_quick_sub_add = QPushButton("➕ Subsystem 퀵 추가")
        btn_quick_sub_add.setStyleSheet("color: #34d399; font-weight: bold; padding: 10px;")
        btn_quick_sub_add.clicked.connect(self.show_quick_subsystem_dialog)
        lay_quick_sub.addWidget(btn_quick_sub_add, 0, 0, 2, 1)
        
        tab_home_layout.addWidget(frm_quick_sub)
        
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

    def scan_pnp_ports(self):
        """
        Background scanning method running every 2 seconds to auto-detect new
        hardware or mock VCP COM ports. If an unregistered port is discovered, prompts
        the user to automatically set up the workspace for that port.
        """
        import serial.tools.list_ports
        # 1. Fetch system ports with descriptions
        comports_list = serial.tools.list_ports.comports()
        ports_info = {p.device: f"{p.device}: {p.description}" for p in comports_list}
        ports = [p.device for p in comports_list]
        
        # 2. Fetch configured ports from config
        configured_ports = {p["port"] for p in self.config_data.get("ports", [])}
        
        # Find unregistered physical/mock ports
        new_ports = [p for p in ports if p not in configured_ports]
        
        # Clean up processed ports that are no longer physically connected,
        # so unplugging and plugging them back in will trigger PnP again
        for p in list(self.pnp_processed_ports):
            if p not in ports:
                self.pnp_processed_ports.remove(p)
                
        for port in new_ports:
            # Avoid duplicate prompts
            if port in self.pnp_processed_ports:
                continue
                
            self.pnp_processed_ports.add(port)
            display_text = ports_info.get(port, port)
            self.log_to_diagnostic(f"PnP: Detected unregistered serial port: {display_text}")
            
            # Show a premium dark notification dialog to the user
            reply = QMessageBox.question(
                self,
                "🔌 플러그 앤 플레이 (Plug & Play) 감지",
                f"새로운 VCP 시리얼 장치 <b>[{display_text}]</b>가 연결된 것을 감지했습니다!\n\n"
                f"이 장치를 모니터링 대시보드에 즉시 등록하고,\n"
                f"기본 계측 화면 및 라우팅 룰을 자동으로 셋업하시겠습니까?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.Yes
            )
            
            if reply == QMessageBox.StandardButton.Yes:
                self.auto_setup_new_port(port)

    def auto_setup_new_port(self, port):
        """
        Automatically adds the serial port, registers a logical subsystem skeleton,
        configures a prefix-based data routing rule, and enables default plugins.
        """
        # Add port configuration
        self.config_data.setdefault("ports", []).append({
            "port": port,
            "baudrate": 115200
        })
        
        # Subsystem Name
        sub_id = f"AutoSub_{port}"
        
        # Create default subsystem structure
        new_sub = {
            "name": sub_id,
            "display_name": f"🔌 Auto Node {port}",
            "variables": [
                {"name": "temp", "display_name": "Core Temperature", "column_index": "0", "gain": 1.0, "offset": 0.0, "unit": "°C", "is_numerical": True},
                {"name": "voltage", "display_name": "Voltage Input", "column_index": "1", "gain": 1.0, "offset": 0.0, "unit": "V", "is_numerical": True},
                {"name": "current", "display_name": "Current Load", "column_index": "2", "gain": 1.0, "offset": 0.0, "unit": "A", "is_numerical": True}
            ],
            "thresholds": {"ovp_var": "voltage", "ovp_val": 100.0, "ocp_var": "current", "ocp_val": 50.0, "otp_var": "temp", "otp_val": 85.0},
            "temp_mosfet_var": "temp",
            "temp_transformer_var": ""
        }
        self.config_data.setdefault("subsystems", []).append(new_sub)
        
        # Add Prefix routing rule matching 'AUTO'
        self.config_data.setdefault("routing_rules", []).append({
            "port": port,
            "type": "PREFIX",
            "pattern": "AUTO",
            "target": sub_id
        })
        
        # Save config and apply
        self.apply_new_workspace_configuration(self.config_data)
        
        # Enable default plugins to populate screen with charts and numerical card docks
        self.plugin_manager.enable_plugin("trend_charts")
        self.plugin_manager.enable_plugin("telemetry_cards")
        
        QMessageBox.information(
            self,
            "오토 셋업 완료",
            f"장치 <b>[{port}]</b>의 오토 셋업이 성공적으로 마쳤습니다!\n\n"
            f"- 서브시스템 등록: '{sub_id}'\n"
            f"- 패킷 CSV 라우팅 Prefix: 'AUTO' (AUTO,temp,volt,curr... 형태로 송출 가능)\n"
            f"- 자동 계측 화면이 화면에 바로 구성되었습니다."
        )

    def scan_physical_com_ports(self):
        """
        Scans all physical system VCP COM serial ports and draws checkable choices dynamically.
        """
        # Clear dynamic checkboxes
        while self.ports_checkbox_lay.count():
            item = self.ports_checkbox_lay.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
                
        # Fetch physical ports info with descriptions
        import serial.tools.list_ports
        comports_list = serial.tools.list_ports.comports()
        ports_info = {p.device: f"{p.device}: {p.description}" for p in comports_list}
        ports = [p.device for p in comports_list]
        
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
            display_text = ports_info.get(port, f"{port}: Offline/Configured Port")
            chk = QCheckBox(display_text)
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

    @pyqtSlot(str, bool, str)
    def on_serial_connection_changed(self, port_name, connected, message):
        """
        Receives notifications of port connection state transitions from MultiPortSerialManager.
        Logs to diagnostic console and intercepts reconnections to trigger seamless historical data sync requests.
        """
        self.log_to_diagnostic(f"SERIAL [{port_name}]: {message}")
        
        # 1. Capture last known state and detect transition from Disconnected (False) to Connected (True)
        prev_state = self.serial_last_states.get(port_name)
        
        if connected and prev_state is False:
            self.log_to_diagnostic(f"INFO: Reconnection detected on {port_name}! Scanning for target subsystems to request resync...")
            
            router = self.data_router
            target_subs = []
            for rule in router.routing_rules:
                if rule["port"] == port_name:
                    target_sub_name = rule.get("target")
                    if target_sub_name in router.subsystems:
                        target_subs.append(router.subsystems[target_sub_name])
                        
            # Trigger resync request for each matched subsystem using its last recorded timestamp
            for sub in target_subs:
                last_time = 0.0
                if sub.time_buffer:
                    last_time = sub.time_buffer[-1]
                
                # Emit PC-led resync command string over COM port
                # Protocol specification: $CMD,REQ_RESYNC,[last_timestamp]\n
                sync_cmd = f"$CMD,REQ_RESYNC,{last_time:.3f}"
                self.serial_manager.send_command(port_name, sync_cmd)
                self.log_to_diagnostic(f"INFO: Dispatched resync request to {port_name} for subsystem '{sub.name}' from timestamp {last_time:.3f}s")
                
        # 2. Update tracking state
        self.serial_last_states[port_name] = connected

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

    def is_simulation_active(self):
        import sys
        import simulation_mock
        serial_module = sys.modules.get('serial')
        if serial_module is None:
            return False
        return getattr(serial_module, 'Serial', None) == simulation_mock.MockSerialPort

    def toggle_simulation_mode(self):
        import simulation_mock
        active = self.is_simulation_active()
        
        QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)
        try:
            if active:
                simulation_mock.restore_physical_serial()
                self.log_to_diagnostic("SYSTEM: Switched to Physical pyserial Driver Mode.")
            else:
                simulation_mock.setup_simulation_mock()
                self.log_to_diagnostic("SYSTEM: Switched to Virtual Simulation Emulation Mode.")
            
            # Restart listening thread handler cleanly
            self.restart_serial_handlers_from_profile()
            
            # Update UI in setup tab if loaded
            if hasattr(self, "setup_tab") and hasattr(self.setup_tab, "update_sim_status_ui"):
                self.setup_tab.update_sim_status_ui()
            
            # Update ribbon button text if needed
            self.update_ribbon_sim_button_style()
            
            # Re-scan physical ports to update COM Ports list checkbox in UI immediately
            self.scan_physical_com_ports()
            
            QMessageBox.information(
                self,
                "드라이버 모드 전환 완료",
                "성공적으로 시리얼 백그라운드 드라이버 모드가 전환되었습니다!\n\n"
                f"현재 모드: {'물리 하드웨어 시리얼 모드' if active else '가상 시뮬레이션 모드'}"
            )
        except Exception as e:
            self.log_to_diagnostic(f"ERROR: Failed to switch driver mode: {str(e)}")
            QMessageBox.critical(self, "드라이버 전환 실패", f"드라이버 스왑 과정 중 에러가 발생했습니다:\n{str(e)}")
        finally:
            QApplication.restoreOverrideCursor()

    def update_ribbon_sim_button_style(self):
        if not hasattr(self, "btn_ribbon_sim_toggle"):
            return
        active = self.is_simulation_active()
        if active:
            self.btn_ribbon_sim_toggle.setText("🟢 에뮬레이터 ON")
            self.btn_ribbon_sim_toggle.setStyleSheet("""
                QPushButton {
                    background-color: #064e3b; border: 1px solid #10b981; color: #34d399; font-weight: bold; padding: 10px;
                }
                QPushButton:hover { background-color: #065f46; border-color: #6ee7b7; color: #ffffff; }
            """)
        else:
            self.btn_ribbon_sim_toggle.setText("🔵 에뮬레이터 OFF")
            self.btn_ribbon_sim_toggle.setStyleSheet("""
                QPushButton {
                    background-color: #1e1b4b; border: 1px solid #6366f1; color: #a5b4fc; font-weight: bold; padding: 10px;
                }
                QPushButton:hover { background-color: #312e81; border-color: #818cf8; color: #ffffff; }
            """)

    def show_layout_guide(self):
        dlg = WindowLayoutGuideDialog(self)
        dlg.exec()

    def show_quick_subsystem_dialog(self):
        dlg = QuickSubsystemDialog(self)
        dlg.exec()

    def show_quick_window_dialog(self):
        dlg = QuickWindowCreateDialog(self)
        dlg.exec()

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
        theme_cfg = self.config_data.get("theme_config", {
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
        
        qss = f"""
        QMainWindow {{ background-color: {win_bg}; }}
        QWidget {{ color: {text}; font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; }}
        
        /* Ribbon frame */
        #RibbonBar {{ background-color: {card_bg}; border-bottom: 1px solid {border}; }}
        QFrame#RibbonGroup {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 6px; }}
        QFrame#RibbonGroup QLabel {{ color: {text}; }}
        
        /* Tab Widget inside the central pane and other widgets */
        QTabWidget::pane {{ border: 1px solid {border}; background-color: {card_bg}; border-radius: 4px; top: -1px; }}
        QTabBar::tab {{ background-color: {win_bg}; color: {text}; padding: 6px 16px; border-top-left-radius: 4px; border-top-right-radius: 4px; border: 1px solid {border}; border-bottom: none; font-weight: bold; }}
        QTabBar::tab:selected {{ background-color: {card_bg}; color: {accent}; border-bottom: 2px solid {accent}; }}
        QTabBar::tab:hover {{ color: #ffffff; background-color: {border}; }}
        
        QMainWindow::separator {{ background-color: {win_bg}; width: 6px; height: 6px; }}
        QMainWindow::separator:hover {{ background-color: {accent}; }}
        
        /* Dock Widgets */
        QDockWidget {{ background-color: {win_bg}; color: #ffffff; font-weight: bold; border: none; }}
        QDockWidget::title {{ background-color: {card_bg}; padding: 6px 10px; border: 1px solid {border}; border-bottom: 1px solid {card_bg}; border-top-left-radius: 4px; border-top-right-radius: 4px; color: {accent}; font-weight: bold; }}
        QDockWidget::close-button, QDockWidget::float-button {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 3px; padding: 2px; }}
        QDockWidget::close-button:hover {{ background-color: #dc2626; border: 1px solid #ef4444; }}
        QDockWidget::float-button:hover {{ background-color: {accent}; border: 1px solid {accent}; }}
        QDockWidget > QWidget {{ background-color: {card_bg}; border: 1px solid {border}; border-bottom-left-radius: 4px; border-bottom-right-radius: 4px; }}
        
        /* Standard Group Boxes */
        QGroupBox {{ border: 1px solid {border}; border-radius: 4px; margin-top: 10px; padding-top: 12px; font-weight: bold; color: {accent}; }}
        QGroupBox::title {{ subcontrol-origin: margin; subcontrol-position: top left; left: 10px; padding: 0 5px; background-color: {card_bg}; }}
        
        /* Inputs & Controls */
        QLineEdit, QComboBox, QDoubleSpinBox, QSpinBox, QTextEdit {{ background-color: {win_bg}; border: 1px solid {border}; border-radius: 4px; padding: 3px; color: #ffffff; }}
        QLineEdit:focus, QComboBox:focus, QDoubleSpinBox:focus, QSpinBox:focus, QTextEdit:focus {{ border-color: {accent}; }}
        QComboBox QAbstractItemView {{ background-color: {card_bg}; border: 1px solid {border}; selection-background-color: {win_bg}; selection-color: {accent}; color: #ffffff; }}
        
        /* Buttons */
        QPushButton {{ background-color: {card_bg}; border: 1px solid {border}; border-radius: 4px; padding: 5px 12px; color: {accent}; font-weight: bold; }}
        QPushButton:hover {{ background-color: {win_bg}; border-color: {accent}; }}
        QPushButton:pressed {{ background-color: {accent}; color: {win_bg}; }}
        QPushButton:disabled {{ background-color: {card_bg}; border-color: {border}; color: #64748b; }}
        
        /* Checkboxes */
        QCheckBox {{ spacing: 6px; font-weight: bold; color: {text}; }}
        QCheckBox::indicator {{ width: 13px; height: 13px; border: 1px solid {border}; background-color: {win_bg}; border-radius: 3px; }}
        QCheckBox::indicator:checked {{ background-color: {accent}; border-color: {accent}; }}
        QCheckBox:hover {{ color: #ffffff; }}
        
        /* Table / Grid */
        QTableWidget {{ background-color: {win_bg}; border: 1px solid {border}; gridline-color: {border}; border-radius: 4px; color: #ffffff; }}
        QHeaderView::section {{ background-color: {card_bg}; color: {accent}; font-weight: bold; border: 1px solid {border}; padding: 4px; }}
        
        /* ScrollBar */
        QScrollBar:vertical {{ border: none; background: {win_bg}; width: 10px; margin: 0px; }}
        QScrollBar::handle:vertical {{ background: {border}; min-height: 20px; border-radius: 5px; }}
        QScrollBar::handle:vertical:hover {{ background: {accent}; }}
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{ height: 0px; }}
        
        /* QMessageBox Premium Dark Styling */
        QMessageBox {{
            background-color: {card_bg};
            border: 1px solid {border};
        }}
        QMessageBox QLabel {{
            color: #ffffff;
            font-size: 12px;
        }}
        QMessageBox QPushButton {{
            background-color: {win_bg};
            border: 1px solid {border};
            color: {accent};
            font-weight: bold;
            padding: 5px 15px;
            border-radius: 4px;
            min-width: 70px;
        }}
        QMessageBox QPushButton:hover {{
            background-color: {card_bg};
            border-color: {accent};
        }}
        QMessageBox QPushButton:pressed {{
            background-color: {accent};
            color: {win_bg};
        }}
        
        /* QListWidget Styling */
        QListWidget {{
            background-color: {win_bg};
            border: 1px solid {border};
            border-radius: 4px;
            color: #ffffff;
            padding: 4px;
        }}
        QListWidget::item {{
            padding: 8px 12px;
            border-bottom: 1px solid {card_bg};
            border-radius: 3px;
            font-weight: bold;
            color: {text};
        }}
        QListWidget::item:hover {{
            background-color: {card_bg};
            color: {accent};
        }}
        QListWidget::item:selected {{
            background-color: {border};
            color: #ffffff;
            border-left: 3px solid {accent};
        }}
        
        /* QDialog and QInputDialog Styling (Add Subsystem Screen) */
        QDialog, QInputDialog {{
            background-color: {card_bg};
            border: 1px solid {border};
        }}
        QDialog QLabel, QInputDialog QLabel {{
            color: #ffffff;
            font-size: 12px;
            background: transparent;
        }}
        QDialog QPushButton, QInputDialog QPushButton {{
            background-color: {win_bg};
            border: 1px solid {border};
            color: {accent};
            font-weight: bold;
            padding: 5px 15px;
            border-radius: 4px;
            min-width: 70px;
        }}
        QDialog QPushButton:hover, QInputDialog QPushButton:hover {{
            background-color: {card_bg};
            border-color: {accent};
        }}
        QDialog QPushButton:pressed, QInputDialog QPushButton:pressed {{
            background-color: {accent};
            color: {win_bg};
        }}
        QDialog QLineEdit, QInputDialog QLineEdit {{
            background-color: {win_bg};
            border: 1px solid {border};
            border-radius: 4px;
            padding: 4px 8px;
            color: #ffffff;
        }}
        QDialog QLineEdit:focus, QInputDialog QLineEdit:focus {{
            border-color: {accent};
        }}
        """
        self.setStyleSheet(qss)
        
        # Keep config setup tab style sheet updated synchronously as well if it exists
        if hasattr(self, "setup_tab"):
            self.setup_tab.apply_setup_tab_theme()

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = DashboardWindow()
    window.show()
    sys.exit(app.exec())
