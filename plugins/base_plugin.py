# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.1.0 (2026-05-29)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.1.0 (2026-05-29) - Antigravity: Added optional parse_data() and generate_command() interface methods for dual-routing optimization.
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized base class for dynamic plugins.
# ======================================================================

from PyQt6.QtCore import QObject

class BasePlugin(QObject):
    """
    Standard interface that all dynamically loadable plugins must inherit.
    Handles GUI registration and lifecycle callbacks.
    """
    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window
        self.plugin_id = "base_plugin"
        self.name = "Base Plugin"
        self.description = "Baseline plugin template"
        self.dock_widget = None

    def on_enable(self):
        """
        Triggered when the user enables this plugin or the app boots up.
        Establish signals, UI injections, and threads here.
        """
        pass

    def on_disable(self):
        """
        Triggered when the user disables this plugin.
        Dismantle UI elements, disconnect slots, and release threads.
        """
        if self.dock_widget:
            self.main_window.removeDockWidget(self.dock_widget)
            self.dock_widget.deleteLater()
            self.dock_widget = None

    def get_dock_widget(self):
        """
        Returns the main visual QDockWidget provided by this plugin, if any.
        """
        return self.dock_widget

    def parse_data(self, raw_line: str) -> dict | None:
        """
        [OPTIONAL]
        Parses incoming raw packet string from serial/network connections.
        Should return a dictionary containing either:
          - A sub-structured dict: {"subsystem": "SubsystemID", "data": {"var_name": value, ...}}
          - A simple flat dict: {"var_name": value, ...} (which routes to the rule's target subsystem)
        Return None if this packet is not meant for this plugin, allowing the router 
        to fallback to default high-speed parser tracks.
        """
        return None

    def generate_command(self, *args, **kwargs) -> bytes | None:
        """
        [OPTIONAL]
        Generates binary/string command packet to transmit to MCU nodes.
        Returns the raw bytes stream to send, or None.
        """
        return None

