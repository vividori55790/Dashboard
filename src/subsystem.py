# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.1.0 (2026-05-29)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.1.0 (2026-05-29) - Antigravity: Added bisect-based append_historical_data() to support seamless historical resync sorting.
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular Subsystem class.
# ======================================================================

class Subsystem:
    """
    Represents a logical embedded node/device being monitored.
    Encapsulates dynamic variable configurations, calibrations, data buffers,
    and real-time protection threshold alarm evaluations.
    """
    def __init__(self, name, display_name=""):
        self.name = name
        self.display_name = display_name if display_name else name
        
        # Dynamic variable mappings
        # Each var info: {name, display_name, column_index, gain, offset, unit, is_numerical}
        self.variables = []
        
        # Key-value maps for easy lookup
        self.latest_values = {}
        
        # Double buffers for time-series chart rendering
        self.max_buffer_points = 500
        self.time_buffer = []
        self.buffers = {} # var_name -> list of values
        
        # Safety Alarms configuration
        # Default safety triggers mapping: e.g. "ovp_var": "vin", "ovp_threshold": 110.0
        self.thresholds = {
            "ovp_var": "",
            "ovp_val": 110.0,
            "ocp_var": "",
            "ocp_val": 50.0,
            "otp_var": "",
            "otp_val": 95.0,
            "standby_var": "",
            "standby_val": 1.0 # If standby variable matches this state, converter is stopped
        }
        
        self.alarm_states = {
            "ovp": False,
            "ocp": False,
            "otp": False,
            "standby": True
        }
        
        # Dynamic thermal hotspot targets
        self.temp_mosfet_var = ""
        self.temp_transformer_var = ""

    def add_variable(self, name, display_name, column_index=0, gain=1.0, offset=0.0, unit="", is_numerical=True):
        """
        Registers a telemetry variable to this subsystem.
        """
        # Remove if duplicate exists
        self.variables = [v for v in self.variables if v["name"] != name]
        
        self.variables.append({
            "name": name,
            "display_name": display_name,
            "column_index": column_index, # column index for CSV or string key for JSON
            "gain": float(gain),
            "offset": float(offset),
            "unit": unit,
            "is_numerical": bool(is_numerical)
        })
        
        self.latest_values[name] = 0.0 if is_numerical else "OFFLINE"
        if is_numerical and name not in self.buffers:
            self.buffers[name] = []

    def clear_variables(self):
        self.variables = []
        self.buffers = {}
        self.latest_values = {}

    def get_variable_config(self, name):
        for v in self.variables:
            if v["name"] == name:
                return v
        return None

    def update_calibrated_value(self, name, raw_value):
        """
        Applies linear calibration: y = ax + b, and caches the result.
        """
        var_cfg = self.get_variable_config(name)
        if not var_cfg:
            return
        
        if var_cfg["is_numerical"]:
            try:
                calibrated = float(raw_value) * var_cfg["gain"] + var_cfg["offset"]
            except (ValueError, TypeError):
                calibrated = 0.0
        else:
            calibrated = str(raw_value)
            
        self.latest_values[name] = calibrated
        return calibrated

    def append_buffer(self, relative_time):
        """
        Appends the latest cached telemetry values into rolling plot buffers.
        """
        self.time_buffer.append(relative_time)
        if len(self.time_buffer) > self.max_buffer_points:
            self.time_buffer.pop(0)
            
        for var_name, buffer_list in self.buffers.items():
            val = self.latest_values.get(var_name, 0.0)
            try:
                buffer_list.append(float(val))
            except (ValueError, TypeError):
                buffer_list.append(0.0)
                
            if len(buffer_list) > self.max_buffer_points:
                buffer_list.pop(0)

    def append_historical_data(self, relative_time, data_dict):
        """
        Inserts a single historical telemetry point at the correct sorted chronological index.
        Uses binary search (bisect) to maintain O(log N) positioning and ensure graph order integrity.
        """
        # Discard if the buffer is full and the historical packet is older than the oldest point
        if len(self.time_buffer) >= self.max_buffer_points and self.time_buffer and relative_time < self.time_buffer[0]:
            return
            
        import bisect
        idx = bisect.bisect_left(self.time_buffer, relative_time)
        
        # Check if the exact time already exists (prevent duplicate overlap glitches)
        if idx < len(self.time_buffer) and abs(self.time_buffer[idx] - relative_time) < 1e-4:
            # Overwrite existing slot instead of inserting a duplicate
            for var_name, buffer_list in self.buffers.items():
                val = data_dict.get(var_name, 0.0)
                try:
                    buffer_list[idx] = float(val)
                except (ValueError, TypeError):
                    buffer_list[idx] = 0.0
            return

        # Insert at the sorted position
        self.time_buffer.insert(idx, relative_time)
        
        for var_name, buffer_list in self.buffers.items():
            val = data_dict.get(var_name, 0.0)
            try:
                buffer_list.insert(idx, float(val))
            except (ValueError, TypeError):
                buffer_list.insert(idx, 0.0)
                
        # Enforce buffer capacity threshold
        if len(self.time_buffer) > self.max_buffer_points:
            self.time_buffer.pop(0)
            for buffer_list in self.buffers.values():
                buffer_list.pop(0)

    def check_safety_alarms(self):
        """
        Inspects variables against alarm limits.
        """
        # 1. OVP Alarm
        ovp_var = self.thresholds.get("ovp_var")
        if ovp_var and ovp_var in self.latest_values:
            val = float(self.latest_values[ovp_var])
            self.alarm_states["ovp"] = val >= self.thresholds["ovp_val"]
        else:
            self.alarm_states["ovp"] = False

        # 2. OCP Alarm
        ocp_var = self.thresholds.get("ocp_var")
        if ocp_var and ocp_var in self.latest_values:
            val = float(self.latest_values[ocp_var])
            self.alarm_states["ocp"] = val >= self.thresholds["ocp_val"]
        else:
            self.alarm_states["ocp"] = False

        # 3. OTP Alarm
        otp_var = self.thresholds.get("otp_var")
        if otp_var and otp_var in self.latest_values:
            val = float(self.latest_values[otp_var])
            self.alarm_states["otp"] = val >= self.thresholds["otp_val"]
        else:
            self.alarm_states["otp"] = False

        # 4. Standby check
        stby_var = self.thresholds.get("standby_var")
        if stby_var and stby_var in self.latest_values:
            try:
                val = float(self.latest_values[stby_var])
                self.alarm_states["standby"] = abs(val - self.thresholds["standby_val"]) < 1e-4
            except (ValueError, TypeError):
                self.alarm_states["standby"] = True
        else:
            self.alarm_states["standby"] = True

    def clear_buffers(self):
        self.time_buffer = []
        for buf in self.buffers.values():
            buf.clear()
        self.alarm_states = {"ovp": False, "ocp": False, "otp": False, "standby": True}
