# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-22)
# - Target Environment: Production / Python 3.10+
# - Integrity Check: Dynamically resolves resource and configuration folders in production
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.0.0 (2026-05-22) - Antigravity: Created robust path resolution hub for Windows/UAC deployment.
# ======================================================================

import os
import sys
from pathlib import Path

def is_frozen():
    """
    Checks if the application is running inside a PyInstaller frozen bundle.
    """
    return getattr(sys, 'frozen', False) and hasattr(sys, '_MEIPASS')

def get_bundle_dir():
    """
    Gets the root directory of the application bundle.
    In development, this is the folder containing main.py.
    In production, this is the temporary extraction folder (_MEIPASS).
    """
    if is_frozen():
        return Path(sys._MEIPASS)
    # Since path_resolver.py is inside the 'src' directory,
    # its parent directory is the root development directory.
    return Path(os.path.dirname(os.path.abspath(__file__))).parent

def get_writable_app_dir():
    """
    Retrieves the system-appropriate user-writable application directory.
    Under Windows: C:\\Users\\<User>\\AppData\\Roaming\\EmbeddedTelemetryMonitor
    Under macOS/Linux: ~/.config/EmbeddedTelemetryMonitor
    """
    if sys.platform == "win32":
        base = os.environ.get("APPDATA")
    else:
        base = os.path.expanduser("~/.config")

    if not base:
        base = os.path.expanduser("~")

    app_dir = Path(base) / "EmbeddedTelemetryMonitor"
    app_dir.mkdir(parents=True, exist_ok=True)
    return app_dir

def get_config_path():
    """
    Determines the path of the active config.json file.
    Supports a dev fallback: if a writable config.json is present in the current
    working directory, it prioritizes it to support local testing seamlessly.
    """
    # 1. Dev-mode local check: check if config.json exists in CWD and is writable
    local_config = Path("config.json").resolve()
    if not is_frozen() and local_config.exists() and os.access(local_config, os.W_OK):
        return str(local_config)

    # 2. Production fallback: use standard user AppData directory
    app_dir = get_writable_app_dir()
    return str(app_dir / "config.json")

def get_plugins_directories():
    """
    Returns a dictionary of plugin directories:
    - 'internal': Bundled standard plugins directory (read-only in prod).
    - 'external': External user plugins directory in AppData (user-writable).
    """
    # Standard internal plugins folder inside the bundle
    internal_dir = get_bundle_dir() / "plugins"
    
    # External user-writable plugins folder
    external_dir = get_writable_app_dir() / "plugins"
    external_dir.mkdir(parents=True, exist_ok=True)
    
    return {
        "internal": str(internal_dir),
        "external": str(external_dir)
    }

def get_logs_directory():
    """
    Returns the log directory located under the user-writable application folder.
    """
    logs_dir = get_writable_app_dir() / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)
    return str(logs_dir)
