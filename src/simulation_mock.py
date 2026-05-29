# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-05-29)
# - Target Environment: Python 3.10+ & PyQt6
# - Description: Dynamically switchable Mock Serial Emulation backend
# ======================================================================

import os
import sys
import time
import math
import random

class MockSerialPort:
    def __init__(self, port, baudrate, timeout=None, write_timeout=None):
        self.port = port
        self.baudrate = baudrate
        self.is_open = True
        self.last_read_time = time.time()
        self.start_time = time.time()
        self.resync_queue = []
        self.history = [] # Backup up to 200 points for historical resync
        print(f"[MOCK SYSTEM]: Virtual serial connection opened on {self.port} at {self.baudrate} baud.")
        
    def close(self):
        self.is_open = False
        print(f"[MOCK SYSTEM]: Virtual serial connection closed on {self.port}.")
        
    def write(self, data):
        cmd = data.decode('utf-8', errors='ignore').strip()
        print(f"[MOCK MCU {self.port} RX Command]: {cmd}")
        
        if "$CMD,REQ_RESYNC" in cmd:
            try:
                last_time = float(cmd.split(',')[2])
                print(f"[MOCK MCU {self.port} RESYNC]: Host requested recovery from timestamp {last_time:.3f}s")
                for t_hist, line_hist in self.history:
                    if t_hist > last_time:
                        hist_packet = f"$HIST,{t_hist:.3f},{line_hist}"
                        self.resync_queue.append(hist_packet.encode('utf-8'))
                print(f"[MOCK MCU {self.port} RESYNC]: Loaded {len(self.resync_queue)} backup points into CDC Tx queue.")
            except Exception as e:
                print(f"[MOCK MCU {self.port} RESYNC ERROR]: {str(e)}")
        
    def flush(self):
        pass
        
    def readline(self):
        if self.resync_queue:
            time.sleep(0.005) # 5ms interval
            return self.resync_queue.pop(0)

        now = time.time()
        elapsed = now - self.last_read_time
        interval = 0.1
        if elapsed < interval:
            time.sleep(interval - elapsed)
        self.last_read_time = time.time()
        
        if not self.is_open:
            return b""
            
        t = time.time() - self.start_time
        self.tick_count = getattr(self, "tick_count", 0) + 1
        
        # Generate special $HEX protocol analyzer packet once every 10 ticks
        if self.tick_count % 10 == 0:
            val = int(40 + 20 * math.sin(t) + random.uniform(-1, 1))
            line = f"$HEX,7E,01,A2,{val:02X},0D\n"
            self.history.append((t, line))
            if len(self.history) > 200:
                self.history.pop(0)
            return line.encode('utf-8')
        
        # COM3 simulates the Engine Subsystem
        if self.port == "COM3":
            rpm = 3500.0 + 800.0 * math.sin(t * 0.5) + random.uniform(-15.0, 15.0)
            temp = 72.0 + 8.0 * math.sin(t * 0.08) + random.uniform(-0.3, 0.3)
            volt = 13.8 + 0.4 * math.sin(t * 0.15) + random.uniform(-0.08, 0.08)
            line = f"ENG,{rpm:.2f},{temp:.2f},{volt:.2f}\n"
            self.history.append((t, line))
            if len(self.history) > 200:
                self.history.pop(0)
            return line.encode('utf-8')
            
        # COM4 simulates the Battery Subsystem
        elif self.port == "COM4":
            vin = 398.5 + 2.5 * math.sin(t * 0.3) + random.uniform(-0.5, 0.5)
            curr = 8.5 + 2.0 * math.sin(t * 0.6) + random.uniform(-0.1, 0.1)
            temp_mos = 42.0 + 12.0 * (curr / 10.0)**2 + random.uniform(-0.15, 0.15)
            temp_trans = 48.0 + 8.0 * (curr / 10.0) + random.uniform(-0.1, 0.1)
            standby = 0.0 if curr > 0.1 else 1.0
            line = f"BAT,{vin:.2f},{curr:.2f},{temp_mos:.2f},{temp_trans:.2f},{standby:.1f}\n"
            self.history.append((t, line))
            if len(self.history) > 200:
                self.history.pop(0)
            return line.encode('utf-8')
            
        else:
            # Dynamically discover mapped prefix in config.json to make simulation mode actually work!
            prefix = ""
            try:
                import json
                config_path = "config.json"
                if os.path.exists(config_path):
                    with open(config_path, "r", encoding="utf-8") as f:
                        data = json.load(f)
                        for r in data.get("routing_rules", []):
                            if r["port"] == self.port and r.get("type") == "PREFIX":
                                prefix = r.get("pattern", "")
                                break
            except:
                pass

            if prefix:
                v1 = 45.0 + 15.0 * math.sin(t) + random.uniform(-0.5, 0.5)
                v2 = 80.0 + 20.0 * math.cos(t * 0.5) + random.uniform(-1.0, 1.0)
                v3 = 12.0 + 1.5 * math.sin(t * 2.0) + random.uniform(-0.1, 0.1)
                line = f"{prefix},{v1:.2f},{v2:.2f},{v3:.2f}\n"
                
                self.history.append((t, line))
                if len(self.history) > 200:
                    self.history.pop(0)
                return line.encode('utf-8')
            else:
                v1 = 100.0 + 20.0 * math.sin(t)
                v2 = 50.0 + 10.0 * math.cos(t)
                line = f"{v1:.3f},{v2:.3f}\n"
                return line.encode('utf-8')

class MockPortInfo:
    def __init__(self, device, description):
        self.device = device
        self.description = description
        self.manufacturer = "Mock Hardware Ltd."

def mock_comports():
    ports = [
        MockPortInfo("COM3", "Mock STM32 Engine controller"),
        MockPortInfo("COM4", "Mock ESP32 Battery sensor node")
    ]
    try:
        import json
        config_path = "config.json"
        if os.path.exists(config_path):
            with open(config_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                configured_ports = data.get("ports", [])
                existing = {p.device for p in ports}
                for cp in configured_ports:
                    p_name = cp["port"]
                    if p_name not in existing:
                        ports.append(MockPortInfo(p_name, f"Virtual System {p_name} Data stream"))
    except:
        pass
        
    return ports

class MockSerialLib:
    Serial = MockSerialPort
    SerialException = Exception
    
    class tools:
        class list_ports:
            @staticmethod
            def comports():
                return mock_comports()

mock_lib = MockSerialLib()

def setup_simulation_mock():
    # Save original serial if not saved
    if 'real_serial' not in sys.modules:
        sys.modules['real_serial'] = sys.modules.get('serial')
        
    sys.modules['serial'] = mock_lib
    sys.modules['serial.tools'] = mock_lib.tools
    sys.modules['serial.tools.list_ports'] = mock_lib.tools.list_ports

    # Dynamically update the global 'serial' attribute of all loaded application modules
    for name, mod in list(sys.modules.items()):
        if mod and (name.startswith('src.') or name in ('main', 'dashboard', 'serial_manager')):
            if hasattr(mod, 'serial'):
                try:
                    setattr(mod, 'serial', mock_lib)
                except Exception as e:
                    pass

def restore_physical_serial():
    real_lib = sys.modules.get('real_serial')
    if real_lib:
        sys.modules['serial'] = real_lib
        sys.modules['serial.tools'] = getattr(real_lib, 'tools', None)
        sys.modules['serial.tools.list_ports'] = getattr(getattr(real_lib, 'tools', None), 'list_ports', None)
        
        # Dynamically restore the global 'serial' attribute of all loaded application modules
        for name, mod in list(sys.modules.items()):
            if mod and (name.startswith('src.') or name in ('main', 'dashboard', 'serial_manager')):
                if hasattr(mod, 'serial'):
                    try:
                        setattr(mod, 'serial', real_lib)
                    except Exception as e:
                        pass
