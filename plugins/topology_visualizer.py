# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-29)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-29) - Antigravity: Completed Phase 3 with Pure Canvas Drag & Drop, JSON coordinate serialization, and Virtual Math Nodes Neon Flow.
# * v1.0.0 (2026-05-25) - Antigravity: Initial creation of Subsystem Interconnection Topology Visualizer.
# ======================================================================

import time
import math
import re
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
    Supports Pure Canvas Drag & Drop and Virtual Math Nodes neon flows.
    """
    def __init__(self, main_window, plugin):
        super().__init__()
        self.main_window = main_window
        self.plugin = plugin
        self.setStyleSheet("background-color: #06070a; border: 1px solid #1e293b; border-radius: 8px;")
        self.setMinimumSize(450, 400)
        self.setMouseTracking(True)
        
        # Anim state
        self.animation_step = 0
        self.anim_timer = QTimer(self)
        self.anim_timer.timeout.connect(self.advance_animation)
        self.anim_timer.start(40) # 25 FPS
        
        self.node_positions = {}
        self.last_update_times = {}
        
        # Drag and drop states
        self.dragged_node = None
        self.drag_offset = QPointF(0, 0)

    def advance_animation(self):
        self.animation_step = (self.animation_step + 1) % 100
        self.update()

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            pos = event.position()
            # 1. Check if clicked on any node block (160x90)
            for name, npos in self.node_positions.items():
                nx, ny = npos
                # Math Nodes are rendered smaller as hexagons/circles (80x80)
                is_math = name.startswith("MATH_")
                w, h = (120, 70) if is_math else (160, 90)
                if nx <= pos.x() <= nx + w and ny <= pos.y() <= ny + h:
                    self.dragged_node = name
                    self.drag_offset = QPointF(pos.x() - nx, pos.y() - ny)
                    break
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if self.dragged_node:
            pos = event.position()
            nx = pos.x() - self.drag_offset.x()
            ny = pos.y() - self.drag_offset.y()
            
            # Boundary containment
            is_math = self.dragged_node.startswith("MATH_")
            w, h = (120, 70) if is_math else (160, 90)
            nx = max(10, min(nx, self.width() - w - 10))
            ny = max(10, min(ny, self.height() - h - 10))
            
            self.node_positions[self.dragged_node] = (int(nx), int(ny))
            
            # Serialize coordinate to config data
            saved_positions = self.main_window.config_data.setdefault("topology_node_positions", {})
            saved_positions[self.dragged_node] = (int(nx), int(ny))
            
            self.update()
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event):
        if self.dragged_node:
            self.dragged_node = None
            self.main_window.config_manager.save_config()
        super().mouseReleaseEvent(event)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        router = self.main_window.data_router
        if not router.subsystems:
            painter.setPen(QColor("#64748b"))
            painter.setFont(QFont("Segoe UI", 11, QFont.Weight.Bold))
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "모니터링할 서브시스템 노드가 존재하지 않습니다.\n설정에서 노드를 먼저 추가해 주세요.")
            return

        # ----------------------------------------------------
        # 1. Establish Layout Coordinates (Subsystems + Virtual Math Nodes)
        # ----------------------------------------------------
        saved_positions = self.main_window.config_data.get("topology_node_positions", {})
        sub_names = sorted(list(router.subsystems.keys()))
        width = self.width()
        height = self.height()
        
        # Resolve physical subsystems coordinates
        for i, name in enumerate(sub_names):
            if name in saved_positions:
                self.node_positions[name] = saved_positions[name]
            elif name not in self.node_positions:
                # Fallback to automatic grid placement
                col = i % 2
                row = i // 2
                x = 50 + col * (width - 220) if len(sub_names) > 1 else (width - 160) // 2
                y = 40 + row * 150
                self.node_positions[name] = (x, y)

        # Resolve Virtual Math Nodes coordinates based on formulas
        formulas = router.linking_formulas
        for i, f in enumerate(formulas):
            target_sub = f.get("target_sub")
            target_var = f.get("target_var")
            formula_name = f"MATH_{target_sub}_{target_var}"
            
            if formula_name in saved_positions:
                self.node_positions[formula_name] = saved_positions[formula_name]
            elif formula_name not in self.node_positions:
                # Place virtual math nodes neatly centered in between columns
                x = (width - 120) // 2
                y = 80 + i * 120
                self.node_positions[formula_name] = (x, y)

        # ----------------------------------------------------
        # 2. Draw Virtual Math Nodes Neon Flows (linking_formulas)
        # ----------------------------------------------------
        for f in formulas:
            target_sub = f.get("target_sub")
            target_var = f.get("target_var")
            formula = f.get("formula", "")
            formula_name = f"MATH_{target_sub}_{target_var}"
            
            if formula_name not in self.node_positions:
                continue
                
            # Scan variables inside math formula [Subsystem].Variable
            refs = re.findall(r'\[([^\]]+)\]\.([a-zA-Z0-9_]+)', formula)
            for ref_sub, ref_var in refs:
                # Draw flow link from ref_sub node to MATH node
                if ref_sub in self.node_positions:
                    p_src = self.node_positions[ref_sub]
                    p_tgt = self.node_positions[formula_name]
                    
                    # Compute endpoints
                    p1 = (p_src[0] + 160, p_src[1] + 45)
                    p2 = (p_tgt[0], p_tgt[1] + 35)
                    
                    path = QPainterPath()
                    path.moveTo(p1[0], p1[1])
                    cx1 = p1[0] + (p2[0] - p1[0]) * 0.5
                    cy1 = p1[1]
                    cx2 = p1[0] + (p2[0] - p1[0]) * 0.5
                    cy2 = p2[1]
                    path.cubicTo(cx1, cy1, cx2, cy2, p2[0], p2[1])
                    
                    # Draw elegant gold dotted linking line
                    pen = QPen(QColor("#d97706"), 1.2, Qt.PenStyle.DashLine) # Golden dotted line
                    painter.setPen(pen)
                    painter.setBrush(Qt.BrushStyle.NoBrush)
                    painter.drawPath(path)
                    
                    # Animated golden flowing particles
                    painter.setPen(Qt.PenStyle.NoPen)
                    for offset in [0.0, 0.5]:
                        percent = (self.animation_step / 100.0 + offset) % 1.0
                        pt = path.pointAtPercent(percent)
                        painter.setBrush(QBrush(QColor(217, 119, 6, 200)))
                        painter.drawEllipse(QPointF(pt.x(), pt.y()), 3.5, 3.5)

            # Draw output link from MATH node to target_sub node
            if target_sub in self.node_positions:
                p_src = self.node_positions[formula_name]
                p_tgt = self.node_positions[target_sub]
                
                p1 = (p_src[0] + 120, p_src[1] + 35)
                p2 = (p_tgt[0], p_tgt[1] + 45)
                
                path = QPainterPath()
                path.moveTo(p1[0], p1[1])
                cx1 = p1[0] + (p2[0] - p1[0]) * 0.5
                cy1 = p1[1]
                cx2 = p1[0] + (p2[0] - p1[0]) * 0.5
                cy2 = p2[1]
                path.cubicTo(cx1, cy1, cx2, cy2, p2[0], p2[1])
                
                pen = QPen(QColor("#38bdf8"), 1.5, Qt.PenStyle.SolidLine) # Skyblue solid flow line
                painter.setPen(pen)
                painter.setBrush(Qt.BrushStyle.NoBrush)
                painter.drawPath(path)
                
                # skyblue glowing flowing particles
                painter.setPen(Qt.PenStyle.NoPen)
                for offset in [0.0, 0.33, 0.66]:
                    percent = (self.animation_step / 100.0 + offset) % 1.0
                    pt = path.pointAtPercent(percent)
                    painter.setBrush(QBrush(QColor(56, 189, 248, 180)))
                    painter.drawEllipse(QPointF(pt.x(), pt.y()), 4.0, 4.0)

        # ----------------------------------------------------
        # 3. Draw Manual Subsystem Connection Links
        # ----------------------------------------------------
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
            
            sx, sy = p_src[0], p_src[1]
            tx, ty = p_tgt[0], p_tgt[1]
            
            # Neat endpoints calculations
            if sx + 180 < tx:
                p1 = (sx + 160, sy + 45)
                p2 = (tx, ty + 45)
            elif sx > tx + 180:
                p1 = (sx, sy + 45)
                p2 = (tx + 160, ty + 45)
            else:
                if sy < ty:
                    p1 = (sx + 80, sy + 90)
                    p2 = (tx + 80, ty)
                else:
                    p1 = (sx + 80, sy)
                    p2 = (tx + 80, ty + 90)
                    
            path = QPainterPath()
            path.moveTo(p1[0], p1[1])
            cx1 = p1[0] + (p2[0] - p1[0]) * 0.5
            cy1 = p1[1]
            cx2 = p1[0] + (p2[0] - p1[0]) * 0.5
            cy2 = p2[1]
            path.cubicTo(cx1, cy1, cx2, cy2, p2[0], p2[1])
            
            live_val = 0.0
            unit = ""
            subsystem = router.subsystems.get(src_sub)
            if subsystem:
                live_val = subsystem.latest_values.get(src_var, 0.0)
                for v in subsystem.variables:
                    if v["name"] == src_var:
                        unit = v.get("unit", "")
                        break
                        
            try:
                is_flowing = abs(float(live_val)) > 0.001
            except:
                is_flowing = False
                
            flow_color = QColor("#10b981") if is_flowing else QColor("#334155")
            
            # Draw curve line
            pen = QPen(flow_color, 2, Qt.PenStyle.SolidLine)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawPath(path)
            
            # Neon active particles
            if is_flowing:
                painter.setPen(Qt.PenStyle.NoPen)
                for offset in [0.0, 0.25, 0.5, 0.75]:
                    percent = (self.animation_step / 100.0 + offset) % 1.0
                    pt = path.pointAtPercent(percent)
                    painter.setBrush(QBrush(QColor(16, 185, 129, 200)))
                    painter.drawEllipse(QPointF(pt.x(), pt.y()), 4.5, 4.5)
                    painter.setBrush(QBrush(QColor("#34d399")))
                    painter.drawEllipse(QPointF(pt.x(), pt.y()), 2.5, 2.5)

            # Central pill info box
            mid_pt = path.pointAtPercent(0.5)
            val_text = f"{live_val} {unit}" if isinstance(live_val, str) else f"{live_val:.2f} {unit}"
            pill_text = f"{label}: {val_text}"
            
            font = QFont("Segoe UI", 8, QFont.Weight.Bold)
            painter.setFont(font)
            fm = painter.fontMetrics()
            text_width = fm.horizontalAdvance(pill_text)
            text_height = fm.height()
            
            px = mid_pt.x() - text_width / 2 - 8
            py = mid_pt.y() - text_height / 2 - 4
            pw = text_width + 16
            ph = text_height + 8
            
            painter.setPen(QPen(flow_color, 1))
            painter.setBrush(QBrush(QColor("#0d1117")))
            painter.drawRoundedRect(int(px), int(py), int(pw), int(ph), 4, 4)
            
            painter.setPen(QColor("#f1f5f9") if is_flowing else QColor("#64748b"))
            painter.drawText(int(px + 8), int(py + text_height + 1), pill_text)

        # ----------------------------------------------------
        # 4. Draw Subsystem Node Blocks
        # ----------------------------------------------------
        now = time.time()
        for name, sub in router.subsystems.items():
            if name not in self.node_positions:
                continue
                
            x, y = self.node_positions[name]
            last_time = self.last_update_times.get(name, 0.0)
            is_active = (now - last_time < 3.0) and last_time > 0.0
            
            grad = QLinearGradient(QPointF(x, y), QPointF(x, y + 90))
            grad.setColorAt(0, QColor("#1e293b"))
            grad.setColorAt(1, QColor("#090d16"))
            
            painter.setPen(QPen(QColor("#38bdf8") if is_active else QColor("#334155"), 2.0 if is_active else 1.2))
            painter.setBrush(QBrush(grad))
            painter.drawRoundedRect(x, y, 160, 90, 8, 8)
            
            painter.setPen(QColor("#ffffff"))
            painter.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
            painter.drawText(x + 10, y + 22, sub.display_name[:18])
            
            painter.setPen(QColor("#94a3b8"))
            painter.setFont(QFont("Segoe UI", 8))
            
            vars_to_show = [v for v in sub.variables if v["is_numerical"]][:3]
            for idx, var in enumerate(vars_to_show):
                val = sub.latest_values.get(var["name"], 0.0)
                val_str = f"{val:.1f} {var['unit']}" if isinstance(val, (int, float)) else f"{val} {var['unit']}"
                painter.drawText(x + 12, y + 42 + idx * 14, f"• {var['display_name'][:10]}: {val_str}")
                
            if is_active:
                painter.setPen(Qt.PenStyle.NoPen)
                painter.setBrush(QBrush(QColor("#38bdf8")))
                painter.drawEllipse(x + 145, y + 15, 6, 6)

        # ----------------------------------------------------
        # 5. Draw Virtual Math Node Hexagons (linking_formulas)
        # ----------------------------------------------------
        for f in formulas:
            target_sub = f.get("target_sub")
            target_var = f.get("target_var")
            formula_name = f"MATH_{target_sub}_{target_var}"
            
            if formula_name not in self.node_positions:
                continue
                
            x, y = self.node_positions[formula_name]
            
            # Golden high-tech hex gradient
            grad = QLinearGradient(QPointF(x, y), QPointF(x, y + 70))
            grad.setColorAt(0, QColor("#78350f"))
            grad.setColorAt(1, QColor("#1c1917"))
            
            painter.setPen(QPen(QColor("#fbbf24"), 1.5))
            painter.setBrush(QBrush(grad))
            
            # Draw beautiful Hexagonal Math Node block (120x70)
            points = [
                QPointF(x + 20, y),
                QPointF(x + 100, y),
                QPointF(x + 120, y + 35),
                QPointF(x + 100, y + 70),
                QPointF(x + 20, y + 70),
                QPointF(x, y + 35)
            ]
            painter.drawPolygon(points)
            
            # Print function title label
            painter.setPen(QColor("#fbbf24"))
            painter.setFont(QFont("Segoe UI", 8, QFont.Weight.Bold))
            painter.drawText(x + 15, y + 25, "🧮 MATH NODE")
            
            # Print resulting dynamic value
            sub = router.subsystems.get(target_sub)
            val = sub.latest_values.get(target_var, 0.0) if sub else 0.0
            val_str = f"{val:.1f}%" if "eff" in target_var.lower() else f"{val:.2f}"
            
            painter.setPen(QColor("#ffffff"))
            painter.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
            painter.drawText(x + 15, y + 45, f"{target_var}: {val_str}")

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
        
        lbl_title = QLabel("⚙️ 전기적 연결 관계 구성")
        lbl_title.setStyleSheet("font-size: 13px; color: #38bdf8; font-weight: bold; padding-bottom: 2px;")
        layout.addWidget(lbl_title)
        
        layout.addWidget(QLabel("소스 서브시스템 (출력):"))
        self.combo_src_sub = QComboBox()
        self.combo_src_sub.currentIndexChanged.connect(self.on_src_sub_changed)
        layout.addWidget(self.combo_src_sub)
        
        layout.addWidget(QLabel("소스 텔레메트리 변수:"))
        self.combo_src_var = QComboBox()
        layout.addWidget(self.combo_src_var)
        
        layout.addWidget(QLabel("타깃 서브시스템 (입력):"))
        self.combo_tgt_sub = QComboBox()
        self.combo_tgt_sub.currentIndexChanged.connect(self.on_tgt_sub_changed)
        layout.addWidget(self.combo_tgt_sub)
        
        layout.addWidget(QLabel("타깃 텔레메트리 변수:"))
        self.combo_tgt_var = QComboBox()
        layout.addWidget(self.combo_tgt_var)
        
        layout.addWidget(QLabel("연결선 정보 라벨명:"))
        self.edit_label = QLineEdit()
        self.edit_label.setPlaceholderText("예: DC Link 전압 전송, Battery Current")
        layout.addWidget(self.edit_label)
        
        self.btn_add = QPushButton("⚡ 연결 링크 추가하기")
        self.btn_add.clicked.connect(self.add_connection_link)
        layout.addWidget(self.btn_add)
        
        layout.addWidget(QLabel("현재 활성화된 연결 구조 목록:"))
        self.list_links = QListWidget()
        layout.addWidget(self.list_links, 1)
        
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
    between separate subsystems. Supports Drag & Drop and virtual Math Nodes flow.
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
        
        splitter = QSplitter(Qt.Orientation.Horizontal)
        splitter.setStyleSheet("QSplitter::handle { background-color: #1e293b; width: 4px; }")
        
        self.paint_widget = TopologyPaintWidget(self.main_window, self)
        splitter.addWidget(self.paint_widget)
        
        self.config_panel = LinkConfigPanel(self.main_window, self)
        splitter.addWidget(self.config_panel)
        
        splitter.setSizes([500, 220])
        main_layout.addWidget(splitter)
        
        self.dock_widget.setWidget(container)
        self.main_window.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, self.dock_widget)
        self.dock_widget.setVisible(True)
        
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        super().on_disable()

    def rebuild_ui(self):
        if hasattr(self, "config_panel") and self.config_panel:
            self.config_panel.refresh_combos()
            self.config_panel.refresh_links_list()
        if hasattr(self, "paint_widget") and self.paint_widget:
            self.paint_widget.update()

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        if hasattr(self, "paint_widget") and self.paint_widget:
            self.paint_widget.log_telemetry_event(subsystem_name)
            self.paint_widget.update()
