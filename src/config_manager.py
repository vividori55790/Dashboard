# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular ConfigManager class.
# ======================================================================

import os
import json
from path_resolver import get_config_path

class ConfigManager:
    """
    Manages persistent saving and loading of the dashboard configuration.
    Starts as a blank slate, allowing complete customization for any MCU,
    saving all ports, subsystems, variables, and formulas to config.json.
    """
    def __init__(self, config_filepath=None):
        if config_filepath is None:
            self.config_filepath = get_config_path()
        else:
            self.config_filepath = config_filepath
            
        self.config_data = {
            "ports": [],          # e.g., [{"port": "COM3", "baudrate": 115200}]
            "subsystems": [],     # List of subsystem definition dicts
            "routing_rules": [],  # Slicing/splitting/prefix rules
            "linking_formulas": [] # Cross-system linking formulas
        }

    def load_config(self):
        """
        Loads the config.json configuration file from disk.
        """
        if not os.path.exists(self.config_filepath):
            # Save a blank skeleton config if missing
            self.save_config()
            return self.config_data
            
        try:
            with open(self.config_filepath, 'r', encoding='utf-8') as f:
                self.config_data = json.load(f)
        except Exception as e:
            print(f"Error loading configuration file: {str(e)}")
            
        return self.config_data

    def save_config(self):
        """
        Saves the current configuration data to config.json persistently.
        """
        try:
            with open(self.config_filepath, 'w', encoding='utf-8') as f:
                json.dump(self.config_data, f, indent=4)
            return True
        except Exception as e:
            print(f"Error saving configuration file: {str(e)}")
            return False

    def add_port(self, port_name, baudrate=115200):
        if not any(p["port"] == port_name for p in self.config_data["ports"]):
            self.config_data["ports"].append({"port": port_name, "baudrate": int(baudrate)})
            self.save_config()

    def remove_port(self, port_name):
        self.config_data["ports"] = [p for p in self.config_data["ports"] if p["port"] != port_name]
        self.save_config()

    def set_subsystems(self, subsystems_list):
        """
        Overwrites the registered subsystems configuration.
        Each entry should represent a Subsystem's configurations.
        """
        self.config_data["subsystems"] = subsystems_list
        self.save_config()

    def set_routing_rules(self, rules_list):
        self.config_data["routing_rules"] = rules_list
        self.save_config()

    def set_linking_formulas(self, formulas_list):
        self.config_data["linking_formulas"] = formulas_list
        self.save_config()
