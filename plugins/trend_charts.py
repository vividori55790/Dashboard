# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular Trend Waveforms plugin.
# ======================================================================

from PyQt6.QtWidgets import QDockWidget, QWidget, QVBoxLayout, QTabWidget, QHBoxLayout, QPushButton, QLabel
from PyQt6.QtCore import pyqtSlot
import pyqtgraph as pg
from plugins.base_plugin import BasePlugin

class TrendChartsPlugin(BasePlugin):
    """
    Renders dynamic real-time scrolling charts (Voltages, Currents, Power, Control)
    using high-performance PyQtGraph. Auto-detects variable mapping categories based on labels.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "trend_charts"
        self.name = "Trend Waveforms"
        self.description = "Plots real-time rolling telemetry trends in separate specialized scopes."
        
        self.plots = {}         # category -> PlotWidget
        self.curves = {}        # (sub_name, var_name) -> pyqtgraph PlotDataItem
        self.categories = ["Voltages", "Currents", "Power/Efficiency", "Control Parameters"]
        
        self.visible_subsystems = set()
        self.visible_variables = set()

    def on_enable(self):
        self.container = QWidget()
        self.layout = QVBoxLayout(self.container)
        self.layout.setContentsMargins(6, 6, 6, 6)
        
        # Premium Filter Toolbar
        filter_lay = QHBoxLayout()
        title_lbl = QLabel(f"📈 {self.name}")
        title_lbl.setStyleSheet("font-weight: bold; color: #38bdf8; font-size: 11px;")
        
        btn_filter = QPushButton("⚙️ 필터 설정")
        btn_filter.setStyleSheet("""
            QPushButton {
                background-color: #1b1c24;
                border: 1px solid #272a38;
                border-radius: 4px;
                color: #a0a5b5;
                font-size: 10px;
                padding: 4px 8px;
            }
            QPushButton:hover {
                color: #38bdf8;
                border-color: #38bdf8;
                background-color: #222530;
            }
        """)
        btn_filter.clicked.connect(self.show_filter_dialog)
        
        filter_lay.addWidget(title_lbl)
        filter_lay.addStretch()
        filter_lay.addWidget(btn_filter)
        self.layout.addLayout(filter_lay)
        
        self.tabs = QTabWidget()
        self.layout.addWidget(self.tabs)
        
        self.rebuild_plots()
        
        # Connect to data updates
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)

    def show_filter_dialog(self):
        from dashboard import PluginFilterDialog
        dlg = PluginFilterDialog(self, self.main_window)
        dlg.exec()

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if hasattr(self, "container") and self.container:
            self.container.deleteLater()
            self.container = None
        super().on_disable()

    def rebuild_plots(self):
        """
        Scans all dynamic variables, categorizes them, creates PlotWidgets, and adds curves.
        """
        self.curves.clear()
        self.plots.clear()
        
        while self.tabs.count():
            self.tabs.removeTab(0)

        router = self.main_window.data_router
        if not router.subsystems:
            return

        # Setup standard PyQtGraph configuration
        pg.setConfigOption('background', '#0f0f12')
        pg.setConfigOption('foreground', '#b5b5c3')
        pg.setConfigOption('antialias', True)

        # Create PlotWidgets for each category
        for cat in self.categories:
            p_widget = pg.PlotWidget(title=f"{cat} Trend")
            p_widget.showGrid(x=True, y=True, alpha=0.3)
            p_widget.addLegend()
            
            # Label axis
            if cat == "Voltages":
                p_widget.setLabel('left', 'Voltage', units='V')
            elif cat == "Currents":
                p_widget.setLabel('left', 'Current', units='A')
            elif cat == "Power/Efficiency":
                p_widget.setLabel('left', 'Power (W) / Eff (%)')
            else:
                p_widget.setLabel('left', 'Value')
            p_widget.setLabel('bottom', 'Time', units='s')
            
            self.plots[cat] = p_widget
            self.tabs.addTab(p_widget, cat)

        # Map dynamic variables to plots
        neon_colors = ["#00d2ff", "#00ff66", "#ba68c8", "#ff9100", "#ffea00", "#e040fb", "#ff3366", "#00e5ff"]
        color_idx = 0

        # Initialize default filter states if empty
        if not self.visible_subsystems:
            self.visible_subsystems = set(router.subsystems.keys())
        if not self.visible_variables:
            all_vars = set()
            for sub in router.subsystems.values():
                for v in sub.variables:
                    all_vars.add(v["name"])
            self.visible_variables = all_vars

        for sub_name, sub in router.subsystems.items():
            if sub_name not in self.visible_subsystems:
                continue
                
            for var in sub.variables:
                if not var["is_numerical"]:
                    continue
                    
                var_name = var["name"]
                if var_name not in self.visible_variables:
                    continue
                    
                unit = var["unit"].upper()
                d_name = var["display_name"].lower()
                
                # Intelligent Auto-classification
                if "V" in unit or "volt" in d_name or var_name.startswith("v"):
                    cat = "Voltages"
                elif "A" in unit or "curr" in d_name or var_name.startswith("i"):
                    cat = "Currents"
                elif "W" in unit or "%" in unit or "pow" in d_name or "eff" in d_name or var_name.startswith("p"):
                    cat = "Power/Efficiency"
                else:
                    cat = "Control Parameters"
                    
                p_widget = self.plots[cat]
                color = neon_colors[color_idx % len(neon_colors)]
                color_idx += 1
                
                # Add curve
                curve = p_widget.plot(
                    pen=pg.mkPen(color=color, width=2),
                    name=f"[{sub.display_name}] {var['display_name']}"
                )
                self.curves[(sub_name, var_name)] = curve

        # Apply initial scale limits based on main window config
        self.apply_plot_scaling()

    def apply_plot_scaling(self):
        paused = getattr(self.main_window, "plots_paused", False)
        auto_scale = getattr(self.main_window, "plot_auto_scale", False)
        
        for p_widget in self.plots.values():
            if auto_scale:
                p_widget.enableAutoRange(y=True)
            else:
                p_widget.enableAutoRange(y=True) # Fallback to auto-scaling generally for dynamic variables

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        """
        Pushes dynamic time-series buffers to the PyqtGraph curves.
        """
        if getattr(self.main_window, "plots_paused", False):
            return
            
        router = self.main_window.data_router
        if subsystem_name not in router.subsystems:
            return
            
        sub = router.subsystems[subsystem_name]
        t_buf = sub.time_buffer
        if not t_buf:
            return
            
        for var_name in data.keys():
            key = (subsystem_name, var_name)
            if key in self.curves and var_name in sub.buffers:
                v_buf = sub.buffers[var_name]
                if len(t_buf) == len(v_buf):
                    self.curves[key].setData(t_buf, v_buf)
