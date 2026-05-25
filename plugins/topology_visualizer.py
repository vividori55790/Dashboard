# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-25)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.0.0 (2026-05-25) - Antigravity: Initial creation of Subsystem Interconnection Topology Visualizer.
# ======================================================================

import time
import math
from PyQt6.QtWidgets import (
    QDockWidget, QWidget, QVBoxLayout, QHBoxLayout, QLabel, 
    QComboBox, QLineEdit, QPushButton, QFrame, QListWidget, 
    QListWidgetItem, QSplitter
)
from PyQt6.QtCore import Qt, QTimer, pyqtSlot, QPointF
from PyQt6.QtGui import QPainter, QColor, QPen, QBrush, QPainterPath, QLinearGradient, QFont
from plugins.base_plugin import BasePlugin

class TopologyPaintWidget(QFrame):
    """
    Renders live dynamic subsystems as node blocks and draws Bezier
    interconnection links with animated flowing neon particles representing
    physical quantity transfers.
    """
    def __init__(self, main_window, plugin):
        super().__init__()
        self.main_window = main_window
        self.plugin = plugin
        self.setStyleSheet("background-color: #08090d; border: 1px solid #1e293b; border-radius: 8px;")
        self.setMinimumSize(450, 400)
        
        # Anim state
        self.animation_step = 0
        self.anim_timer = QTimer(self)
        self.anim_timer.timeout.connect(self.advance_animation)
        self.anim_timer.start(40) # 25 FPS
        
        self.node_positions = {}
        self.last_update_times = {}

    def advance_animation(self):
        self.animation_step = (self.animation_step + 1) % 100
        self.update()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        router = self.main_window.data_router
        if not router.subsystems:
            # Draw placeholder
            painter.setPen(QColor("#64748b"))
            painter.setFont(QFont("Segoe UI", 11, QFont.Weight.Bold))
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "모니터링할 서브시스템 노드가 존재하지 않습니다.\n설정에서 노드를 먼저 추가해 주세요.")
            return

        # 1. Update node layout dynamic positions
        sub_names = sorted(list(router.subsystems.keys()))
        width = self.width()
        height = self.height()
        
        for i, name in enumerate(sub_names):
            # Auto grid layout based on count
            col = i % 2
            row = i // 2
            
            # Subsystems coordinate grid spacing
            x = 80 + col * (width - 240) if len(sub_names) > 1 else (width - 160) // 2
            y = 60 + row * 160
            self.node_positions[name] = (x, y)

        # 2. Draw Connection Links
        links = self.main_window.config_data.get("subsystem_links", [])
        for link in links:
            src_sub = link.get("source_sub")
            src_var = link.get("source_var")
            tgt_sub = link.get("target_sub")
            tgt_var = link.get("target_var")
            label = link.get("label", "물리량")
            
            if src_sub not in self.node_positions or tgt_sub not in self.node_positions:
                continue
                
            p_src = self.node_positions[src_sub]
            p_tgt = self.node_positions[tgt_sub]
            
            # Block dimensions: 160x90
            sx, sy = p_src[0], p_src[1]
            tx, ty = p_tgt[0], p_tgt[1]
            
            # Determine neat endpoints depending on placement
            if sx + 180 < tx: # Source is left of Target
                p1 = (sx + 160, sy + 45)
                p2 = (tx, ty + 45)
            elif sx > tx + 180: # Source is right of Target
                p1 = (sx, sy + 45)
                p2 = (tx + 160, ty + 45)
            else: # Vertical or overlapping layout
                if sy < ty:
                    p1 = (sx + 80, sy + 90)
                    p2 = (tx + 80, ty)
                else:
                    p1 = (sx + 80, sy)
                    p2 = (tx + 80, ty + 90)
                    
            # Compute Bezier Curve
            path = QPainterPath()
            path.moveTo(p1[0], p1[1])
            
            # Bezier control points for S-Curve
            cx1 = p1[0] + (p2[0] - p1[0]) * 0.5
            cy1 = p1[1]
            cx2 = p1[0] + (p2[0] - p1[0]) * 0.5
            cy2 = p2[1]
            path.cubicTo(cx1, cy1, cx2, cy2, p2[0], p2[1])
            
            # Active state checks
            live_val = 0.0
            unit = ""
            subsystem = router.subsystems.get(src_sub)
            if subsystem:
                live_val = subsystem.latest_values.get(src_var, 0.0)
                # Find unit
                for v in subsystem.variables:
                    if v["name"] == src_var:
                        unit = v.get("unit", "")
                        break
                        
            # Link color highlighting if value flows
            try:
                is_flowing = abs(float(live_val)) > 0.001
            except:
                is_flowing = False
                
            flow_color = QColor("#10b981") if is_flowing else QColor("#475569") # Neon green active, Slate inactive
            
            # Draw baseline path
            pen = QPen(flow_color, 2, Qt.PenStyle.SolidLine)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawPath(path)
            
            # Draw moving animated neon dots
            if is_flowing:
                painter.setPen(Qt.PenStyle.NoPen)
                for offset in [0.0, 0.25, 0.5, 0.75]:
                    percent = (self.animation_step / 100.0 + offset) % 1.0
                    pt = path.pointAtPercent(percent)
                    
                    # Neon particle glow
                    painter.setBrush(QBrush(QColor(16, 185, 129, 200)))
                    painter.drawEllipse(QPointF(pt.x(), pt.y()), 4.5, 4.5)
                    painter.setBrush(QBrush(QColor("#34d399")))
                    painter.drawEllipse(QPointF(pt.x(), pt.y()), 2.5, 2.5)

            # Draw central information pill
            mid_pt = path.pointAtPercent(0.5)
            val_text = f"{live_val} {unit}" if isinstance(live_val, str) else f"{live_val:.2f} {unit}"
            pill_text = f"{label}: {val_text}"
            
            font = QFont("Segoe UI", 8, QFont.Weight.Bold)
            painter.setFont(font)
            fm = painter.fontMetrics()
            text_width = fm.horizontalAdvance(pill_text)
            text_height = fm.height()
            
            # Draw pill border & background
            pill_rect = QFrame().rect()
            px = mid_pt.x() - text_width / 2 - 8
            py = mid_pt.y() - text_height / 2 - 4
            pw = text_width + 16
            ph = text_height + 8
            
            painter.setPen(QPen(flow_color, 1))
            painter.setBrush(QBrush(QColor("#0f172a"))) # deep dark blue fill
            painter.drawRoundedRect(int(px), int(py), int(pw), int(ph), 4, 4)
            
            painter.setPen(QColor("#f1f5f9") if is_flowing else QColor("#94a3b8"))
            painter.drawText(int(px + 8), int(py + text_height + 1), pill_text)

        # 3. Draw Subsystem Node Blocks
        now = time.time()
        for name, sub in router.subsystems.items():
            if name not in self.node_positions:
                continue
                
            x, y = self.node_positions[name]
            
            # Active glow check
            last_time = self.last_update_times.get(name, 0.0)
            is_active = (now - last_time < 3.0) and last_time > 0.0
            
            # Box Gradient
            grad = QLinearGradient(QPointF(x, y), QPointF(x, y + 90))
            grad.setColorAt(0, QColor("#1e293b"))
            grad.setColorAt(1, QColor("#0f172a"))
            
            # Draw Node rounded rect
            painter.setPen(QPen(QColor("#38bdf8") if is_active else QColor("#334155"), 2 if is_active else 1.2))
            painter.setBrush(QBrush(grad))
            painter.drawRoundedRect(x, y, 160, 90, 8, 8)
            
            # Draw Title & Icon
            painter.setPen(QColor("#ffffff"))
            painter.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
            painter.drawText(x + 10, y + 22, sub.display_name[:18])
            
            # Draw dynamic main stats
            painter.setPen(QColor("#94a3b8"))
            painter.setFont(QFont("Segoe UI", 8))
            
            vars_to_show = [v for v in sub.variables if v["is_numerical"]][:3]
            for idx, var in enumerate(vars_to_show):
                val = sub.latest_values.get(var["name"], 0.0)
                val_str = f"{val:.1f} {var['unit']}" if isinstance(val, (int, float)) else f"{val} {var['unit']}"
                painter.drawText(x + 12, y + 42 + idx * 14, f"• {var['display_name'][:10]}: {val_str}")
                
            # Active small neon LED indicator
            if is_active:
                painter.setPen(Qt.PenStyle.NoPen)
                painter.setBrush(QBrush(QColor("#38bdf8")))
                painter.drawEllipse(x + 145, y + 15, 6, 6)

    def log_telemetry_event(self, sub_name):
        self.last_update_times[sub_name] = time.time()


class LinkConfigPanel(QWidget):
    """
    Control configuration form panel to add and delete subsystem links.
    """
    def __init__(self, main_window, plugin):
        super().__init__()
        self.main_window = main_window
        self.plugin = plugin
        self.setStyleSheet("""
            QWidget { background-color: #0b0c10; color: #e2e8f0; }
            QLabel { font-size: 11px; font-weight: bold; color: #94a3b8; }
            QComboBox, QLineEdit {
                background-color: #131924;
                border: 1px solid #2d3748;
                border-radius: 4px;
                padding: 6px;
                color: #ffffff;
                font-size: 11px;
            }
            QPushButton {
                background-color: #0d9488;
                border: 1px solid #0f766e;
                border-radius: 4px;
                color: white;
                font-weight: bold;
                padding: 8px;
                font-size: 11px;
            }
            QPushButton:hover {
                background-color: #0f766e;
            }
            QListWidget {
                background-color: #0d1117;
                border: 1px solid #21262d;
                border-radius: 4px;
                color: #c9d1d9;
                font-size: 11px;
            }
        """)
        self.init_ui()
        self.refresh_combos()
        self.refresh_links_list()

    def init_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 12, 12, 12)
        layout.setSpacing(10)
        
        # Add Link Header
        lbl_title = QLabel("⚙️ 전기적 연결 관계 구성")
        lbl_title.setStyleSheet("font-size: 13px; color: #38bdf8; font-weight: bold; padding-bottom: 2px;")
        layout.addWidget(lbl_title)
        
        # Source Subsystem Dropdowns
        layout.addWidget(QLabel("소스 서브시스템 (출력):"))
        self.combo_src_sub = QComboBox()
        self.combo_src_sub.currentIndexChanged.connect(self.on_src_sub_changed)
        layout.addWidget(self.combo_src_sub)
        
        layout.addWidget(QLabel("소스 텔레메트리 변수:"))
        self.combo_src_var = QComboBox()
        layout.addWidget(self.combo_src_var)
        
        # Target Subsystem Dropdowns
        layout.addWidget(QLabel("타깃 서브시스템 (입력):"))
        self.combo_tgt_sub = QComboBox()
        self.combo_tgt_sub.currentIndexChanged.connect(self.on_tgt_sub_changed)
        layout.addWidget(self.combo_tgt_sub)
        
        layout.addWidget(QLabel("타깃 텔레메트리 변수:"))
        self.combo_tgt_var = QComboBox()
        layout.addWidget(self.combo_tgt_var)
        
        # Link Label Name
        layout.addWidget(QLabel("연결선 정보 라벨명:"))
        self.edit_label = QLineEdit()
        self.edit_label.setPlaceholderText("예: DC Link 전압 전송, Battery Current")
        layout.addWidget(self.edit_label)
        
        # Add Button
        self.btn_add = QPushButton("⚡ 연결 링크 추가하기")
        self.btn_add.clicked.connect(self.add_connection_link)
        layout.addWidget(self.btn_add)
        
        # Active Connections List Section
        layout.addWidget(QLabel("현재 활성화된 연결 구조 목록:"))
        self.list_links = QListWidget()
        layout.addWidget(self.list_links, 1)
        
        # Delete Button
        self.btn_del = QPushButton("❌ 선택한 연결 삭제")
        self.btn_del.setStyleSheet("background-color: #ef4444; border: 1px solid #dc2626;")
        self.btn_del.clicked.connect(self.delete_connection_link)
        layout.addWidget(self.btn_del)

    def refresh_combos(self):
        router = self.main_window.data_router
        self.combo_src_sub.blockSignals(True)
        self.combo_tgt_sub.blockSignals(True)
        
        self.combo_src_sub.clear()
        self.combo_tgt_sub.clear()
        
        for name, sub in router.subsystems.items():
            self.combo_src_sub.addItem(sub.display_name, name)
            self.combo_tgt_sub.addItem(sub.display_name, name)
            
        self.combo_src_sub.blockSignals(False)
        self.combo_tgt_sub.blockSignals(False)
        
        self.on_src_sub_changed()
        self.on_tgt_sub_changed()

    def on_src_sub_changed(self):
        sub_name = self.combo_src_sub.currentData()
        self.combo_src_var.clear()
        if not sub_name:
            return
            
        router = self.main_window.data_router
        sub = router.subsystems.get(sub_name)
        if sub:
            for var in sub.variables:
                self.combo_src_var.addItem(f"{var['display_name']} ({var['name']})", var["name"])

    def on_tgt_sub_changed(self):
        sub_name = self.combo_tgt_sub.currentData()
        self.combo_tgt_var.clear()
        if not sub_name:
            return
            
        router = self.main_window.data_router
        sub = router.subsystems.get(sub_name)
        if sub:
            for var in sub.variables:
                self.combo_tgt_var.addItem(f"{var['display_name']} ({var['name']})", var["name"])

    def refresh_links_list(self):
        self.list_links.clear()
        links = self.main_window.config_data.get("subsystem_links", [])
        for idx, link in enumerate(links):
            src_sub = link.get("source_sub")
            src_var = link.get("source_var")
            tgt_sub = link.get("target_sub")
            tgt_var = link.get("target_var")
            label = link.get("label", "링크")
            
            display_text = f"[{label}] {src_sub}.{src_var} ➔ {tgt_sub}.{tgt_var}"
            item = QListWidgetItem(display_text)
            item.setData(Qt.ItemDataRole.UserRole, idx)
            self.list_links.addItem(item)

    def add_connection_link(self):
        src_sub = self.combo_src_sub.currentData()
        src_var = self.combo_src_var.currentData()
        tgt_sub = self.combo_tgt_sub.currentData()
        tgt_var = self.combo_tgt_var.currentData()
        label = self.edit_label.text().strip()
        
        if not src_sub or not src_var or not tgt_sub or not tgt_var:
            return
            
        if not label:
            label = f"{src_var} ➔ {tgt_var}"
            
        links = self.main_window.config_data.setdefault("subsystem_links", [])
        
        # Prevent duplication
        for l in links:
            if l["source_sub"] == src_sub and l["source_var"] == src_var and l["target_sub"] == tgt_sub and l["target_var"] == tgt_var:
                return
                
        links.append({
            "source_sub": src_sub,
            "source_var": src_var,
            "target_sub": tgt_sub,
            "target_var": tgt_var,
            "label": label
        })
        
        self.main_window.config_manager.save_config()
        self.refresh_links_list()
        self.edit_label.clear()
        self.plugin.paint_widget.update()

    def delete_connection_link(self):
        selected_item = self.list_links.currentItem()
        if not selected_item:
            return
            
        idx = selected_item.data(Qt.ItemDataRole.UserRole)
        links = self.main_window.config_data.get("subsystem_links", [])
        
        if 0 <= idx < len(links):
            links.pop(idx)
            self.main_window.config_manager.save_config()
            self.refresh_links_list()
            self.plugin.paint_widget.update()


class TopologyVisualizerPlugin(BasePlugin):
    """
    Custom modular plugin presenting the physical interconnection flow layout
    between separate subsystems (e.g. DAB converter and PSFB / battery BMS).
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "topology_visualizer"
        self.name = "Topology Flow Interconnector"
        self.description = "Displays the physical block diagram layout and animates power/energy flow lines connecting separate systems."
        
    def on_enable(self):
        self.dock_widget = QDockWidget("⚡ 서브시스템 연결 토폴로지", self.main_window)
        self.dock_widget.setObjectName("TopologyVisualizerDock")
        self.dock_widget.setAllowedAreas(Qt.DockWidgetArea.AllDockWidgetAreas)
        
        container = QWidget()
        container.setStyleSheet("background-color: #08090d;")
        main_layout = QHBoxLayout(container)
        main_layout.setContentsMargins(6, 6, 6, 6)
        
        # Horizontal Splitter between Visualizer and Config Panel
        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.setStyleSheet("QSplitter::handle { background-color: #1e293b; width: 4px; }")
        
        self.paint_widget = TopologyPaintWidget(self.main_window, self)
        splitter.addWidget(self.paint_widget)
        
        self.config_panel = LinkConfigPanel(self.main_window, self)
        splitter.addWidget(self.config_panel)
        
        # Set dynamic proportions (Visualizer gets most space)
        splitter.setSizes([500, 220])
        main_layout.addWidget(splitter)
        
        self.dock_widget.setWidget(container)
        
        # Default dock visual registration
        self.main_window.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, self.dock_widget)
        self.dock_widget.setVisible(True)
        
        # Connect live telemetry signals
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        super().on_disable()

    def rebuild_ui(self):
        """
        Invoked when configuration manager schemas change, keeping lists synchronous.
        """
        if hasattr(self, "config_panel") and self.config_panel:
            self.config_panel.refresh_combos()
            self.config_panel.refresh_links_list()
        if hasattr(self, "paint_widget") and self.paint_widget:
            self.paint_widget.update()

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        """
        Triggers graphics rendering refresh and records data activity updates.
        """
        if hasattr(self, "paint_widget") and self.paint_widget:
            self.paint_widget.log_telemetry_event(subsystem_name)
            self.paint_widget.update()
