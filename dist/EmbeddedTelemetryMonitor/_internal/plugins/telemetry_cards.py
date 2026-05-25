# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular Telemetry Cards plugin.
# ======================================================================

from PyQt6.QtWidgets import QDockWidget, QWidget, QHBoxLayout, QGroupBox, QGridLayout, QLabel
from PyQt6.QtCore import Qt, pyqtSlot
from plugins.base_plugin import BasePlugin

class TelemetryCardsPlugin(BasePlugin):
    """
    Renders beautiful, dynamic visual visualizer cards for all numerical telemetry fields
    defined across the active subsystems. Fits side-by-side inside a QDockWidget.
    """
    def __init__(self, main_window):
        super().__init__(main_window)
        self.plugin_id = "telemetry_cards"
        self.name = "Telemetry Cards"
        self.description = "Displays real-time numerical readings in highly stylized card widgets."
        
        self.card_widgets = {} # (sub_name, var_name) -> QLabel(value_readout)
        self.group_boxes = {}  # sub_name -> QGroupBox

    def on_enable(self):
        self.container = QWidget()
        self.layout = QHBoxLayout(self.container)
        self.layout.setContentsMargins(6, 6, 6, 6)
        self.layout.setSpacing(8)
        
        self.rebuild_ui()
        
        # Connect data receiver
        self.main_window.data_router.telemetry_routed.connect(self.on_telemetry_routed)

    def on_disable(self):
        try:
            self.main_window.data_router.telemetry_routed.disconnect(self.on_telemetry_routed)
        except:
            pass
        if hasattr(self, "container") and self.container:
            self.container.deleteLater()
            self.container = None
        super().on_disable()

    def rebuild_ui(self):
        """
        Dynamically builds the visual card panels based on the active subsystems configuration.
        """
        # Clear existing
        self.card_widgets.clear()
        self.group_boxes.clear()
        
        while self.layout.count():
            item = self.layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

        router = self.main_window.data_router
        if not router.subsystems:
            # Placeholder if no subsystems configured
            lbl = QLabel("No active subsystems configured. Open Subsystem Config Editor to get started!")
            lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl.setStyleSheet("font-size: 13px; color: #75758a;")
            self.layout.addWidget(lbl)
            return

        # Choose a curated premium color list for subsystems
        colors = ["#00d2ff", "#ba68c8", "#ff9100", "#00ff66", "#e040fb", "#ffea00"]
        
        for idx, (sub_name, sub) in enumerate(router.subsystems.items()):
            color = colors[idx % len(colors)]
            
            group = QGroupBox(f"⚡ {sub.display_name}")
            group_lay = QGridLayout(group)
            group_lay.setContentsMargins(6, 12, 6, 6)
            group_lay.setSpacing(6)
            
            self.group_boxes[sub_name] = group
            
            # Draw variable panels in 2-row layout dynamically
            col = 0
            row = 0
            for var in sub.variables:
                var_name = var["name"]
                
                # Card Container
                card = QWidget()
                card_lay = QGridLayout(card)
                card_lay.setContentsMargins(8, 8, 8, 8)
                card_lay.setSpacing(2)
                
                lbl_name = QLabel(var["display_name"])
                lbl_name.setStyleSheet("font-size: 9px; font-weight: bold; color: #8c8c9e; text-transform: uppercase;")
                
                lbl_unit = QLabel(var["unit"])
                lbl_unit.setStyleSheet("font-size: 9px; font-weight: bold; color: #546e7a; background-color: #0b0b0d; padding: 1px 4px; border-radius: 3px;")
                
                lbl_val = QLabel("OFFLINE")
                lbl_val.setAlignment(Qt.AlignmentFlag.AlignCenter)
                lbl_val.setStyleSheet(f"font-size: 15px; font-weight: bold; color: {color}; font-family: 'Consolas', monospace;")
                
                card_lay.addWidget(lbl_name, 0, 0, Qt.AlignmentFlag.AlignLeft)
                card_lay.addWidget(lbl_unit, 0, 1, Qt.AlignmentFlag.AlignRight)
                card_lay.addWidget(lbl_val, 1, 0, 1, 2, Qt.AlignmentFlag.AlignCenter)
                
                card.setStyleSheet("background-color: #0b0b0d; border: 1px solid #1c1c24; border-radius: 6px;")
                
                # Cache for fast updates
                self.card_widgets[(sub_name, var_name)] = lbl_val
                
                # Grid placement: 4 variables per row
                group_lay.addWidget(card, row, col)
                col += 1
                if col >= 4:
                    col = 0
                    row += 1
                    
            self.layout.addWidget(group)

    @pyqtSlot(str, dict)
    def on_telemetry_routed(self, subsystem_name, data):
        """
        Receives real-time data from DataRouter and pushes updates to the GUI widgets.
        """
        for var_name, val in data.items():
            key = (subsystem_name, var_name)
            if key in self.card_widgets:
                lbl = self.card_widgets[key]
                
                # Format float nicely or display directly
                if isinstance(val, float):
                    lbl.setText(f"{val:.2f}")
                else:
                    lbl.setText(str(val))
