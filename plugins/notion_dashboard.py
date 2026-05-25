# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: Production / Python 3.10+ & PyQt6 & NumPy & PyQtGraph
# - Integrity Check: Dynamically loaded Notion-style block/page modular workspace
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.0.0 (2026-05-25) - Antigravity: Initial creation of dynamic Notion-style modular dashboard plugin.
# ======================================================================

import numpy as np
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QComboBox, 
    QLabel, QScrollArea, QGridLayout, QFrame, QMessageBox, QTabWidget
)
from PyQt6.QtCore import Qt, pyqtSlot, pyqtSignal
import pyqtgraph as pg
from plugins.base_plugin import BasePlugin

class NotionBlockWidget(QFrame):
    """
    Individual modular sub-window block modeled after Notion's clean block layout.
    Displays a selected telemetry variable with one of several visualization modes (Graph, FFT, SMA, Stats).
    """
    closed = pyqtSignal(QWidget)

    def __init__(self, parent_plugin):
        super().__init__()
        self.parent_plugin = parent_plugin
        self.main_window = parent_plugin.main_window
        self.sub_name = ""
        self.var_name = ""
        
        # Stylesheet for premium card border and background
        self.setObjectName("NotionBlock")
        self.setStyleSheet("""
            QFrame#NotionBlock {
                background-color: #12131a;
                border: 1px solid #272a38;
                border-radius: 8px;
            }
            QFrame#NotionBlock:hover {
                border-color: #38bdf8;
            }
            QComboBox {
                background-color: #0a0b0d;
                border: 1px solid #272a38;
                border-radius: 4px;
                padding: 4px;
                font-size: 11px;
                color: #ffffff;
            }
            QComboBox:focus {
                border-color: #38bdf8;
            }
            QLabel {
                font-weight: bold;
                color: #8e94a6;
            }
        """)
        
        self.init_ui()
        self.populate_subsystems()

    def init_ui(self):
        # 1. Main layout
        self.layout = QVBoxLayout(self)
        self.layout.setContentsMargins(10, 10, 10, 10)
        self.layout.setSpacing(8)

        # 2. Control Header (Notion-style properties bar)
        self.header_layout = QHBoxLayout()
        self.header_layout.setSpacing(6)

        # A. Subsystem dropdown
        self.header_layout.addWidget(QLabel("Node:"))
        self.combo_sub = QComboBox()
        self.combo_sub.currentIndexChanged.connect(self.on_subsystem_changed)
        self.combo_sub.setFixedWidth(110)
        self.header_layout.addWidget(self.combo_sub)

        # B. Variable dropdown
        self.header_layout.addWidget(QLabel("Prop:"))
        self.combo_var = QComboBox()
        self.combo_var.currentIndexChanged.connect(self.on_variable_changed)
        self.combo_var.setFixedWidth(100)
        self.header_layout.addWidget(self.combo_var)

        # C. Visualization Mode Selector
        self.header_layout.addWidget(QLabel("Type:"))
        self.combo_mode = QComboBox()
        self.combo_mode.addItem("📈 Graph", "GRAPH")
        self.combo_mode.addItem("⚡ FFT Analysis", "FFT")
        self.combo_mode.addItem("📊 Smooth SMA", "SMA")
        self.combo_mode.addItem("🔢 Stats", "STATS")
        self.combo_mode.currentIndexChanged.connect(self.on_mode_changed)
        self.combo_mode.setFixedWidth(100)
        self.header_layout.addWidget(self.combo_mode)

        self.header_layout.addStretch()

        # D. Delete button (❌)
        self.btn_close = QPushButton("❌")
        self.btn_close.setToolTip("Delete this Notion Block")
        self.btn_close.setFixedSize(24, 24)
        self.btn_close.setStyleSheet("""
            QPushButton {
                background-color: transparent;
                border: none;
                font-size: 11px;
                color: #ef4444;
            }
            QPushButton:hover {
                background-color: #2e1014;
                border-radius: 4px;
            }
        """)
        self.btn_close.clicked.connect(lambda: self.closed.emit(self))
        self.header_layout.addWidget(self.btn_close)
        
        self.layout.addLayout(self.header_layout)

        # 3. Main content viewport
        self.view_stack = QTabWidget()
        self.view_stack.tabBar().hide() # Hide tab headers to act like a stacked layout
        self.view_stack.setStyleSheet("background: transparent; border: none;")
        self.layout.addWidget(self.view_stack, 1)

        # Page 0: Plot Area (Graph, FFT, SMA)
        self.plot_container = QWidget()
        self.plot_layout = QVBoxLayout(self.plot_container)
        self.plot_layout.setContentsMargins(0, 0, 0, 0)
        
        # Configure Premium pyqtgraph settings
        pg.setConfigOption('background', '#0a0b0d')
        pg.setConfigOption('foreground', '#8e94a6')
        pg.setConfigOption('antialias', True)
        
        self.plot_widget = pg.PlotWidget()
        self.plot_widget.showGrid(x=True, y=True, alpha=0.15)
        self.plot_layout.addWidget(self.plot_widget)
        
        self.view_stack.addTab(self.plot_container, "PLOT")

        # Page 1: Text Summary Area (Statistics)
        self.stats_container = QFrame()
        self.stats_container.setStyleSheet("background-color: #0a0b0d; border-radius: 6px; border: 1px solid #1e202b;")
        self.stats_layout = QVBoxLayout(self.stats_container)
        self.stats_layout.setContentsMargins(15, 15, 15, 15)
        self.stats_layout.setSpacing(6)
        
        self.lbl_stats_title = QLabel("🔢 Real-time Statistical Variance")
        self.lbl_stats_title.setStyleSheet("font-size: 12px; color: #38bdf8; border-bottom: 1px solid #272a38; padding-bottom: 4px;")
        self.stats_layout.addWidget(self.lbl_stats_title)
        
        self.lbl_mean = QLabel("• Mean (Average): N/A")
        self.lbl_variance = QLabel("• Variance (분산): N/A")
        self.lbl_std = QLabel("• Std Deviation: N/A")
        self.lbl_min_max = QLabel("• Peak-to-Peak (Min/Max): N/A")
        
        for lbl in [self.lbl_mean, self.lbl_variance, self.lbl_std, self.lbl_min_max]:
            lbl.setStyleSheet("font-size: 12px; font-family: Consolas, monospace; color: #ffffff;")
            self.stats_layout.addWidget(lbl)
            
        self.stats_layout.addStretch()
        self.view_stack.addTab(self.stats_container, "STATS")

        # Dynamic plotting handles
        self.curve_raw = self.plot_widget.plot(pen=pg.mkPen(color="#00d2ff", width=2), name="Raw Value")
        self.curve_overlay = self.plot_widget.plot(pen=pg.mkPen(color="#ff9100", width=2), name="Smooth SMA")

    def populate_subsystems(self):
        router = self.main_window.data_router
        self.combo_sub.clear()
        for name in router.subsystems.keys():
            self.combo_sub.addItem(name)
            
        # Select first if exists
        if self.combo_sub.count() > 0:
            self.combo_sub.setCurrentIndex(0)
            self.on_subsystem_changed(0)

    def on_subsystem_changed(self, idx):
        self.sub_name = self.combo_sub.currentText()
        router = self.main_window.data_router
        self.combo_var.clear()
        
        if self.sub_name in router.subsystems:
            sub = router.subsystems[self.sub_name]
            for var in sub.variables:
                if var["is_numerical"]:
                    self.combo_var.addItem(var["name"])
                    
        if self.combo_var.count() > 0:
            self.combo_var.setCurrentIndex(0)
            self.on_variable_changed(0)

    def on_variable_changed(self, idx):
        self.var_name = self.combo_var.currentText()
        self.reset_views()

    def on_mode_changed(self, idx):
        mode = self.combo_mode.currentData()
        if mode == "STATS":
            self.view_stack.setCurrentIndex(1)
        else:
            self.view_stack.setCurrentIndex(0)
        self.reset_views()

    def reset_views(self):
        # Clear plots
        self.curve_raw.setData([], [])
        self.curve_overlay.setData([], [])
        self.plot_widget.setLabel('left', self.var_name if self.var_name else 'Value')
        self.plot_widget.setLabel('bottom', 'Time', units='s')
        
        mode = self.combo_mode.currentData()
        if mode == "FFT":
            self.plot_widget.setLabel('bottom', 'Frequency', units='Hz')
            self.plot_widget.setLabel('left', 'Magnitude (dB)')
        elif mode == "SMA":
            self.curve_overlay.setVisible(True)
        else:
            self.curve_overlay.setVisible(False)

    def update_block_data(self, updated_sub_name, data):
        """
        Receives real-time telemetry updates. Computes mathematical processing
        (FFT, Moving Average, Variance statistics) and renders updates dynamically.
        """
        if updated_sub_name != self.sub_name or not self.var_name:
            return
            
        router = self.main_window.data_router
        if self.sub_name not in router.subsystems:
            return
            
        sub = router.subsystems[self.sub_name]
        t_buf = sub.time_buffer
        
        if self.var_name not in sub.buffers or not t_buf:
            return
            
        v_buf = sub.buffers[self.var_name]
        n = len(v_buf)
        if n < 4:
            return
            
        mode = self.combo_mode.currentData()
        y = np.array(v_buf)
        x = np.array(t_buf)

        if mode == "GRAPH":
            # 1. Standard real-time trend scope
            self.curve_raw.setData(x, y)
            self.curve_overlay.setData([], [])

        elif mode == "FFT":
            # 2. Fast Fourier Transform Frequency Analysis (Engineering-grade diagnostics!)
            # Subtract mean to remove strong DC offset (so frequency spike at 0Hz is removed)
            y_detrend = y - np.mean(y)
            
            # Assume constant sample frequency (mean dt)
            dt = np.mean(np.diff(x)) if len(x) > 1 else 0.02
            if dt <= 0:
                dt = 0.02
                
            freqs = np.fft.fftfreq(n, d=dt)
            fft_vals = np.abs(np.fft.fft(y_detrend))
            
            # Only keep positive frequencies
            half = n // 2
            f_plot = freqs[:half]
            a_plot = fft_vals[:half] / n # Normalize magnitude
            
            # Filter negative or zero frequencies safely
            valid = f_plot > 0
            if np.any(valid):
                # Convert amplitude to dynamic Decibel scale (dB) for engineering diagnostics
                a_db = 20 * np.log10(a_plot[valid] + 1e-6)
                self.curve_raw.setData(f_plot[valid], a_db)
            self.curve_overlay.setData([], [])

        elif mode == "SMA":
            # 3. Simple Moving Average (SMA) smoothing overlay
            window_size = 15
            self.curve_raw.setData(x, y)
            
            if n >= window_size:
                weights = np.ones(window_size) / window_size
                smooth_y = np.convolve(y, weights, mode='valid')
                # Align time indices (convolve shrinks the output by window_size-1)
                x_smooth = x[window_size - 1:]
                self.curve_overlay.setData(x_smooth, smooth_y)
            else:
                self.curve_overlay.setData([], [])

        elif mode == "STATS":
            # 4. Statistical variance summary computation
            mean_val = np.mean(y)
            var_val = np.var(y)
            std_val = np.std(y)
            min_val = np.min(y)
            max_val = np.max(y)
            
            self.lbl_stats_title.setText(f"🔢 {self.sub_name}.{self.var_name} Statistics")
            self.lbl_mean.setText(f"• Mean (Average):  {mean_val:.4f}")
            self.lbl_variance.setText(f"• Variance (분산):  {var_val:.4f}")
            self.lbl_std.setText(f"• Std Deviation:   {std_val:.4f}")
            self.lbl_min_max.setText(f"• Min / Max Range: {min_val:.3f} ~ {max_val:.3f} ({max_val-min_val:.3f})")


class NotionDashboardPlugin(BasePlugin):
    """
    Notion Workspace Plugin.
    Organizes customized dashboard blocks (windows) containing system-to-system
    data visualization widgets, real-time statistical summaries, and FFT analysis.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "notion_dashboard"
        self.name = "Notion Workspace"
        self.description = "Notion-style modular signal sub-windows block workspace. Add pages to track graphs, FFTs, and variance."
        
        self.blocks = []

    def on_enable(self):
        # Main root container
        self.container = QWidget()
        self.main_layout = QVBoxLayout(self.container)
        self.main_layout.setContentsMargins(15, 15, 15, 15)
        self.main_layout.setSpacing(12)

        # 1. Premium Notion-style header bar
        self.header_widget = QFrame()
        self.header_widget.setStyleSheet("""
            QFrame {
                background-color: #12131a;
                border: 1px solid #272a38;
                border-radius: 6px;
                padding: 10px;
            }
            QLabel {
                font-family: 'Segoe UI', sans-serif;
                color: #ffffff;
            }
        """)
        self.header_layout = QHBoxLayout(self.header_widget)
        self.header_layout.setContentsMargins(10, 10, 10, 10)

        # Title/Description
        self.title_layout = QVBoxLayout()
        self.title_layout.setSpacing(4)
        
        self.lbl_title = QLabel("🗒️ Notion-Style System Workspace")
        self.lbl_title.setStyleSheet("font-size: 16px; font-weight: bold; color: #38bdf8;")
        self.title_layout.addWidget(self.lbl_title)
        
        self.lbl_desc = QLabel("Add customization blocks (Notion pages) to dynamically link, filter, perform real-time FFT diagnostics, and calculate variance statistics.")
        self.lbl_desc.setStyleSheet("font-size: 11px; color: #8e94a6;")
        self.title_layout.addWidget(self.lbl_desc)
        
        self.header_layout.addLayout(self.title_layout, 1)

        # Actions
        self.btn_add_block = QPushButton("➕ Add Block (Window Page)")
        self.btn_add_block.setStyleSheet("background-color: #0284c7; border-color: #0369a1; color: white; font-weight: bold; padding: 8px 16px; border-radius: 4px;")
        self.btn_add_block.clicked.connect(self.add_block)
        self.header_layout.addWidget(self.btn_add_block)

        self.btn_clear = QPushButton("🧹 Clear All")
        self.btn_clear.setStyleSheet("background-color: transparent; border: 1px solid #ef4444; color: #ef4444; font-weight: bold; padding: 8px 14px; border-radius: 4px;")
        self.btn_clear.clicked.connect(self.clear_workspace)
        self.header_layout.addWidget(self.btn_clear)

        self.main_layout.addWidget(self.header_widget)

        # 2. Scrollable flex-layout grid for Blocks
        self.scroll_area = QScrollArea()
        self.scroll_area.setWidgetResizable(True)
        self.scroll_area.setStyleSheet("background: transparent; border: none;")
        
        self.grid_container = QWidget()
        self.grid_container.setStyleSheet("background: transparent;")
        self.grid_layout = QGridLayout(self.grid_container)
        self.grid_layout.setContentsMargins(0, 10, 0, 10)
        self.grid_layout.setSpacing(15)
        
        self.scroll_area.setWidget(self.grid_container)
        self.main_layout.addWidget(self.scroll_area, 1)

        # Spawn two default blocks on boot for a gorgeous user experience!
        self.add_block()
        self.add_block()

        # Connect signals
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_updated)

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_updated)
        except:
            pass
        self.clear_workspace(force=True)
        if hasattr(self, "container") and self.container:
            self.container.deleteLater()
            self.container = None
        super().on_disable()

    def add_block(self):
        """
        Creates and inserts a new modular Notion widget block into the scrollable grid layout.
        Arranges blocks in a clean double-column grid layout (2 blocks per row).
        """
        block = NotionBlockWidget(self)
        block.closed.connect(self.remove_block)
        
        # Calculate dynamic grid position
        idx = len(self.blocks)
        row = idx // 2
        col = idx % 2
        
        self.grid_layout.addWidget(block, row, col)
        
        # Height constraint to keep layout neat
        block.setMinimumHeight(280)
        
        self.blocks.append(block)

    def remove_block(self, block):
        """
        Destroys and clears a specific block from the workspace, rearranging remaining blocks.
        """
        if block in self.blocks:
            from PyQt6.QtWidgets import QMessageBox
            node = block.sub_name if block.sub_name else "미정"
            prop = block.var_name if block.var_name else "미정"
            res = QMessageBox.question(
                self.container,
                "➖ 블록 삭제 확인",
                f"선택한 분석 블록(노드: {node}, 변수: {prop})을 워크스페이스에서 삭제하시겠습니까?\n삭제된 블록의 누적 데이터 그래프와 통계 정보는 모두 삭제됩니다.",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No
            )
            if res != QMessageBox.StandardButton.Yes:
                return

            self.blocks.remove(block)
            self.grid_layout.removeWidget(block)
            block.deleteLater()
            self.reorganize_grid()

    def clear_workspace(self, force=False):
        # Double confirmation for destructive workspace clearing
        if not force and self.blocks:
            res = QMessageBox.question(
                self.container,
                "🧹 워크스페이스 비우기 확인",
                "노션 워크스페이스의 모든 분석 블록(FFT, 통계, 그래프 등)을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No
            )
            if res != QMessageBox.StandardButton.Yes:
                return

        for block in list(self.blocks):
            if block in self.blocks:
                self.blocks.remove(block)
                self.grid_layout.removeWidget(block)
                block.deleteLater()
        self.reorganize_grid()

    def reorganize_grid(self):
        """
        Clears all widget layouts and recalculates grid coordinates for a compact 2-column flex flow.
        """
        # Collect remaining blocks
        temp_blocks = list(self.blocks)
        self.blocks.clear()
        
        # Clear grid layout mapping
        for i in reversed(range(self.grid_layout.count())):
            item = self.grid_layout.takeAt(i)
            # Do not delete, just detach
            
        # Re-insert dynamically
        for block in temp_blocks:
            idx = len(self.blocks)
            row = idx // 2
            col = idx % 2
            self.grid_layout.addWidget(block, row, col)
            self.blocks.append(block)

    @pyqtSlot(str, dict)
    def on_telemetry_updated(self, subsystem_name, data):
        """
        Distributes data signals to each block for real-time calculations.
        """
        for block in self.blocks:
            block.update_block_data(subsystem_name, data)

    def rebuild_ui(self):
        """
        Triggers dropdown resets whenever the system configuration profile changes.
        """
        for block in self.blocks:
            block.populate_subsystems()
