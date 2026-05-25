# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular PluginManager.
# ======================================================================

import os
import sys
import importlib
import traceback
from plugins.base_plugin import BasePlugin

class PluginManager:
    """
    Handles dynamic discovery, verification, enabling, disabling,
    and runtime configuration of standard and downloadable plugins.
    """
    def __init__(self, main_window):
        self.main_window = main_window
        self.available_plugins = {} # plugin_id -> Class object
        self.active_plugins = {}    # plugin_id -> Instantiated Plugin object
        
        from path_resolver import get_plugins_directories
        dirs = get_plugins_directories()
        self.internal_plugins_dir = dirs["internal"]
        self.external_plugins_dir = dirs["external"]

    def discover_plugins(self):
        """
        Scans both internal standard plugins and external user plugins directories,
        dynamically loading all modules inheriting from BasePlugin.
        """
        self.available_plugins.clear()
        
        # Ensure directories exist
        for d in [self.internal_plugins_dir, self.external_plugins_dir]:
            if not os.path.exists(d):
                try:
                    os.makedirs(d)
                except Exception as e:
                    print(f"Error creating plugin directory {d}: {e}")
                    
        # Add internal bundle path to sys.path to allow absolute dynamic imports inside plugins
        # (e.g. from plugins.base_plugin import BasePlugin)
        internal_parent = os.path.dirname(self.internal_plugins_dir)
        if internal_parent not in sys.path:
            sys.path.insert(0, internal_parent)

        plugin_files = []
        
        # 1. Scan internal bundled plugins
        if os.path.exists(self.internal_plugins_dir):
            for filename in os.listdir(self.internal_plugins_dir):
                if filename.endswith(".py") and filename not in ["__init__.py", "base_plugin.py"]:
                    path = os.path.join(self.internal_plugins_dir, filename)
                    module_name = filename[:-3]
                    plugin_files.append((path, f"plugins.internal.{module_name}"))
                    
        # 2. Scan external custom user plugins (external overrides internal on name conflict)
        if os.path.exists(self.external_plugins_dir):
            for filename in os.listdir(self.external_plugins_dir):
                if filename.endswith(".py") and filename not in ["__init__.py", "base_plugin.py"]:
                    path = os.path.join(self.external_plugins_dir, filename)
                    module_name = filename[:-3]
                    plugin_files.append((path, f"plugins.external.{module_name}"))

        import importlib.util

        for file_path, full_module_name in plugin_files:
            try:
                # Load module dynamically from absolute file path
                spec = importlib.util.spec_from_file_location(full_module_name, file_path)
                if spec is None or spec.loader is None:
                    continue
                    
                module = importlib.util.module_from_spec(spec)
                sys.modules[full_module_name] = module
                spec.loader.exec_module(module)
                
                # Search classes inside the module
                for attr_name in dir(module):
                    attr = getattr(module, attr_name)
                    if isinstance(attr, type) and attr is not BasePlugin and issubclass(attr, BasePlugin):
                        plugin_inst = attr(self.main_window)
                        p_id = plugin_inst.plugin_id
                        self.available_plugins[p_id] = attr
                        
            except Exception as e:
                print(f"Error loading dynamic plugin package {file_path}: {str(e)}")
                print(traceback.format_exc())

    def install_plugin(self, source_file_path):
        """
        Copies a specified python plugin file into the user's external writable plugins folder,
        then triggers a rediscovery scan.
        """
        import shutil
        if not os.path.exists(source_file_path):
            raise FileNotFoundError(f"Source plugin file not found: {source_file_path}")
            
        if not source_file_path.endswith(".py"):
            raise ValueError("Only Python files (.py) can be installed as plugins.")
            
        filename = os.path.basename(source_file_path)
        dest_path = os.path.join(self.external_plugins_dir, filename)
        
        # Ensure external plugins directory exists
        os.makedirs(self.external_plugins_dir, exist_ok=True)
        
        # Copy the file
        shutil.copy2(source_file_path, dest_path)
        
        # Re-scan for discovering the new plugin
        self.discover_plugins()
        return filename

    def enable_plugin(self, plugin_id):
        """
        Instantiates and activates an available plugin.
        """
        if plugin_id in self.active_plugins:
            return True
            
        if plugin_id not in self.available_plugins:
            return False
            
        try:
            plugin_class = self.available_plugins[plugin_id]
            plugin_instance = plugin_class(self.main_window)
            
            # Enable operations
            plugin_instance.on_enable()
            self.active_plugins[plugin_id] = plugin_instance
            
            # Inject visual QDockWidget if provided
            dock = plugin_instance.get_dock_widget()
            if dock:
                # Add to GUI (default to Bottom area, user can float/move)
                from PyQt6.QtCore import Qt
                self.main_window.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, dock)
                dock.setVisible(True)
                
            return True
        except Exception as e:
            err_msg = f"Failed to activate plugin '{plugin_id}': {str(e)}"
            print(err_msg)
            print(traceback.format_exc())
            if hasattr(self.main_window, "log_to_diagnostic"):
                self.main_window.log_to_diagnostic(f"ERROR: {err_msg}")
            return False

    def disable_plugin(self, plugin_id):
        """
        Deactivates and destroys an enabled plugin.
        """
        if plugin_id not in self.active_plugins:
            return
            
        try:
            plugin_instance = self.active_plugins.pop(plugin_id)
            plugin_instance.on_disable()
        except Exception as e:
            print(f"Error disabling plugin '{plugin_id}': {str(e)}")

    def toggle_plugin(self, plugin_id, enabled):
        """
        Toggles state and saves the choice persistently.
        """
        if enabled:
            success = self.enable_plugin(plugin_id)
            if success:
                self._update_saved_plugin_state(plugin_id, True)
        else:
            self.disable_plugin(plugin_id)
            self._update_saved_plugin_state(plugin_id, False)

    def load_active_plugins_from_config(self, enabled_list):
        """
        Enables plugins that were saved as active in the user profile.
        """
        # If no explicit config, enable all discovered plugins by default
        if enabled_list is None:
            enabled_list = list(self.available_plugins.keys())
            
        for p_id in enabled_list:
            self.enable_plugin(p_id)

    def _update_saved_plugin_state(self, plugin_id, is_enabled):
        """
        Updates the active plugins list inside persistent config.json.
        """
        if hasattr(self.main_window, "config_manager"):
            cm = self.main_window.config_manager
            cfg = cm.config_data
            
            if "active_plugins" not in cfg:
                cfg["active_plugins"] = []
                
            if is_enabled:
                if plugin_id not in cfg["active_plugins"]:
                    cfg["active_plugins"].append(plugin_id)
            else:
                if plugin_id in cfg["active_plugins"]:
                    cfg["active_plugins"].remove(plugin_id)
                    
            cm.save_config()
