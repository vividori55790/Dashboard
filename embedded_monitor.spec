# -*- mode: python ; coding: utf-8 -*-
# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-22)
# - Target Environment: PyInstaller / Windows EXE
# - Integrity Check: Packages dynamic plugins and ensures local AppData fallbacks
# ======================================================================

import os
from PyInstaller.utils.hooks import collect_data_files

block_cipher = None

# Collect all dynamic plugins and stream_client.html
added_files = [
    ('stream_client.html', '.'),
    ('plugins', 'plugins')
]

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[],
    datas=added_files,
    hiddenimports=[
        'serial',
        'serial.tools',
        'serial.tools.list_ports',
        'path_resolver',
        'subsystem',
        'config_manager',
        'data_router',
        'serial_manager',
        'plugins',
        'plugins.base_plugin',
        'plugins.telemetry_cards',
        'plugins.trend_charts',
        'plugins.mcu_terminal',
        'plugins.parameter_manager',
        'plugins.service_console'
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(
    a.pure, 
    a.zipped_data, 
    cipher=block_cipher
)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='EmbeddedTelemetryMonitor',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,  # Set to True if terminal logging/debugging is desired, False for window-only EXE
    disable_windowed_traceback=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=None # Place a custom .ico path here if available
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='EmbeddedTelemetryMonitor',
)
