# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v2.1.0 (2026-05-29)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: DO NOT delete any existing functions unless explicitly requested.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v2.1.0 (2026-05-29) - Antigravity: Added dual-track routing (custom plugin parser & high-speed fallback parser).
# * v2.0.0 (2026-05-22) - Antigravity: Initial creation of specialized modular DataRouter with formula engine.
# ======================================================================

import re
import json
import time
from PyQt6.QtCore import QObject, pyqtSignal, pyqtSlot
from subsystem import Subsystem

class DataRouter(QObject):
    """
    Parses and routes raw strings received from various USB COM ports into
    respective target Subsystem data models. Triggers dynamic calibrations,
    cross-system algebraic math data linking, and notifications.
    """
    # Signal emitted when a subsystem's data has been updated
    # Emits: subsystem_name, calibrated_values_dict
    telemetry_routed = pyqtSignal(str, dict)
    error_logged = pyqtSignal(str)

    def __init__(self):
        super().__init__()
        self.subsystems = {}        # name -> Subsystem instance
        self.routing_rules = []      # List of dynamic rules: {port, type, pattern, target}
        self.linking_formulas = []   # List of linking equations: {target_sub, target_var, formula}
        self.start_time = time.time()

    def add_subsystem(self, sub_name, display_name=""):
        if sub_name not in self.subsystems:
            self.subsystems[sub_name] = Subsystem(sub_name, display_name)
        return self.subsystems[sub_name]

    def remove_subsystem(self, sub_name):
        if sub_name in self.subsystems:
            del self.subsystems[sub_name]

    def clear_all(self):
        self.subsystems.clear()
        self.routing_rules.clear()
        self.linking_formulas.clear()

    def set_config(self, config_data):
        """
        Applies profile configurations from config_manager.
        """
        self.clear_all()
        
        # 1. Register Subsystems
        for sub_cfg in config_data.get("subsystems", []):
            sub = self.add_subsystem(sub_cfg["name"], sub_cfg.get("display_name", ""))
            
            # Variables
            for var_cfg in sub_cfg.get("variables", []):
                sub.add_variable(
                    name=var_cfg["name"],
                    display_name=var_cfg["display_name"],
                    column_index=var_cfg.get("column_index", 0),
                    gain=var_cfg.get("gain", 1.0),
                    offset=var_cfg.get("offset", 0.0),
                    unit=var_cfg.get("unit", ""),
                    is_numerical=var_cfg.get("is_numerical", True)
                )
                
            # Alarm thresholds
            for k, v in sub_cfg.get("thresholds", {}).items():
                if k in sub.thresholds:
                    sub.thresholds[k] = v
            
            # Thermal mappings
            sub.temp_mosfet_var = sub_cfg.get("temp_mosfet_var", "")
            sub.temp_transformer_var = sub_cfg.get("temp_transformer_var", "")

        # 2. Routing Rules
        self.routing_rules = config_data.get("routing_rules", [])

        # 3. Data Linking Formulas
        self.linking_formulas = config_data.get("linking_formulas", [])

    @pyqtSlot(str, str)
    def route_packet(self, port_name, raw_line):
        """
        Entry slot for MultiPortSerialManager. Parses the line and distributes
        calibrated fields to target subsystems, running data links right after.
        Supports performance-optimized dual-track parsing: custom plugin parser first,
        falling back to default high-speed parser on fail or bypass.
        Supports historical packet sync ($HIST,[time],[data]) for seamless resync.
        """
        if not raw_line:
            return
            
        t_elapsed = time.time() - self.start_time
        updated_subsystems = set()

        # ----------------------------------------------------
        # [역사적 데이터 패킷 ($HIST 접두사) 전처리]
        # ----------------------------------------------------
        is_historical = False
        historical_time = 0.0
        if raw_line.startswith("$HIST,"):
            try:
                parts = raw_line.split(',', 2)
                historical_time = float(parts[1])
                raw_line = parts[2]
                is_historical = True
            except:
                pass

        # ----------------------------------------------------
        # [플러그인 트랙 (이원화 라우팅 최적화)]
        # 활성화된 플러그인 중 custom parse_data()를 제공하는 플러그인에 우선 기회 부여
        # ----------------------------------------------------
        plugin_parsed = False
        if hasattr(self, "main_window") and self.main_window and hasattr(self.main_window, "plugin_manager"):
            active_plugins = getattr(self.main_window.plugin_manager, "active_plugins", {})
            for p_id, p_inst in active_plugins.items():
                if hasattr(p_inst, "parse_data") and callable(p_inst.parse_data):
                    try:
                        # parse_data()가 유효한 dict를 반환하면 파싱 성공으로 판단
                        parsed_dict = p_inst.parse_data(raw_line)
                        if parsed_dict and isinstance(parsed_dict, dict):
                            # 형식 A: {"subsystem": "BatteryNode", "data": {"volt": 12.3}}
                            if "subsystem" in parsed_dict and "data" in parsed_dict:
                                target_sub_name = parsed_dict["subsystem"]
                                data_map = parsed_dict["data"]
                                if target_sub_name in self.subsystems and isinstance(data_map, dict):
                                    sub = self.subsystems[target_sub_name]
                                    for k, v in data_map.items():
                                        sub.update_calibrated_value(k, v)
                                    updated_subsystems.add(target_sub_name)
                                    plugin_parsed = True
                            # 형식 B: flat dict {"volt": 12.3} ➔ 해당 포트 룰의 target 서브시스템 매핑
                            else:
                                # 이 포트와 연관된 룰의 target subsystem 탐색
                                for rule in self.routing_rules:
                                    if rule["port"] == port_name:
                                        target_sub_name = rule.get("target")
                                        if target_sub_name in self.subsystems:
                                            sub = self.subsystems[target_sub_name]
                                            for k, v in parsed_dict.items():
                                                sub.update_calibrated_value(k, v)
                                            updated_subsystems.add(target_sub_name)
                                            plugin_parsed = True
                            
                            # 하나의 플러그인이 성공적으로 파싱을 완료했다면 중단
                            if plugin_parsed:
                                break
                    except Exception as e:
                        self.error_logged.emit(f"Plugin Parser Exception in '{p_id}' on {port_name}: {str(e)}")

        # ----------------------------------------------------
        # [초고속 트랙 (기본 파서)]
        # 플러그인에 의해 파싱되지 않은 패킷에 한하여 기존 방식대로 고속 파싱 진행
        # ----------------------------------------------------
        if not plugin_parsed:
            # Find rules matching the source port
            matching_rules = [r for r in self.routing_rules if r["port"] == port_name]
            
            # If no rules match the port, treat as default raw columns for the first subsystem
            if not matching_rules:
                if self.subsystems:
                    first_sub_name = list(self.subsystems.keys())[0]
                    self._parse_raw_csv(raw_line, self.subsystems[first_sub_name])
                    updated_subsystems.add(first_sub_name)
            else:
                for rule in matching_rules:
                    rule_type = rule.get("type", "COLUMNS")
                    pattern = rule.get("pattern", "")
                    target_name = rule.get("target")
                    
                    if target_name not in self.subsystems:
                        continue
                        
                    sub = self.subsystems[target_name]
                    
                    # 1. PREFIX ROUTING (e.g., CSV prefixed by $DAB,)
                    if rule_type == "PREFIX":
                        if raw_line.startswith(pattern):
                            # Strip prefix, then parse
                            cleaned_line = raw_line[len(pattern):].lstrip(',')
                            if self._parse_raw_csv(cleaned_line, sub):
                                updated_subsystems.add(target_name)

                    # 2. JSON KEY ROUTING (e.g., JSON packet checks "device": "PSFB")
                    elif rule_type == "JSON":
                        if raw_line.startswith("{") or "{" in raw_line:
                            try:
                                # Strip any prefix before brackets if exists
                                json_str = raw_line[raw_line.find("{"):]
                                data = json.loads(json_str)
                                
                                # Validate signature pattern check (e.g., "device": "PSFB")
                                if ":" in pattern:
                                    chk_k, chk_v = pattern.split(":", 1)
                                    if str(data.get(chk_k.strip())) != chk_v.strip():
                                        continue
                                
                                # Parse variables from JSON
                                for var in sub.variables:
                                    key = var["column_index"] # For JSON, index holds the string key name
                                    if key in data:
                                        sub.update_calibrated_value(var["name"], data[key])
                                updated_subsystems.add(target_name)
                            except Exception as e:
                                self.error_logged.emit(f"JSON Parse Exception on {port_name}: {str(e)}")

                    # 3. DIRECT RAW COLUMNS ROUTING (no tags, matches index slices)
                    elif rule_type == "COLUMNS":
                        if self._parse_raw_csv(raw_line, sub):
                            updated_subsystems.add(target_name)

        if not updated_subsystems:
            return

        # 4. RUN ORGANIC DATA LINKING FORMULAS
        self.evaluate_data_links()

        # 5. COMMIT BUFFERS & EMIT SIGS
        for name in updated_subsystems:
            sub = self.subsystems[name]
            sub.check_safety_alarms()
            if is_historical:
                sub.append_historical_data(historical_time, sub.latest_values)
            else:
                sub.append_buffer(t_elapsed)
            self.telemetry_routed.emit(name, sub.latest_values)

        # Trigger update signals for subsystems targetted ONLY by formulas
        formula_subsystems = set(f["target_sub"] for f in self.linking_formulas)
        for name in formula_subsystems:
            if name in self.subsystems and name not in updated_subsystems:
                sub = self.subsystems[name]
                sub.check_safety_alarms()
                if is_historical:
                    sub.append_historical_data(historical_time, sub.latest_values)
                else:
                    sub.append_buffer(t_elapsed)
                self.telemetry_routed.emit(name, sub.latest_values)

    def _parse_raw_csv(self, line_str, subsystem):
        """
        Splits a standard CSV line by commas and maps elements to subsystem variables.
        """
        # Clean checksum if present (e.g. *5A)
        if '*' in line_str:
            line_str = line_str.split('*')[0]
            
        parts = line_str.strip().split(',')
        if len(parts) < 1:
            return False
            
        for var in subsystem.variables:
            try:
                col_idx = int(var["column_index"])
                if col_idx < len(parts):
                    subsystem.update_calibrated_value(var["name"], parts[col_idx])
            except ValueError:
                # Ignore mapping issues for non-int column definitions in CSV mode
                pass
        return True

    def evaluate_data_links(self):
        """
        Safely computes algebraic dynamic links across subsystems in real-time.
        Uses Python's ast module to build a secure arithmetic evaluator,
        completely replacing the dangerous eval() function.
        """
        for link in self.linking_formulas:
            target_sub = link.get("target_sub")
            target_var = link.get("target_var")
            formula = link.get("formula", "")
            
            if not formula or target_sub not in self.subsystems:
                continue
                
            sub = self.subsystems[target_sub]
            
            # Resolve variable references e.g. [DAB].pout -> real-time float value
            refs = re.findall(r'\[([^\]]+)\]\.([a-zA-Z0-9_]+)', formula)
            eval_str = formula
            
            for ref_sub, ref_var in refs:
                val = 0.0
                if ref_sub in self.subsystems:
                    val = self.subsystems[ref_sub].latest_values.get(ref_var, 0.0)
                try:
                    val = float(val)
                except (ValueError, TypeError):
                    val = 0.0
                eval_str = eval_str.replace(f"[{ref_sub}].{ref_var}", f"({val})")

            # Evaluate using a secure AST-based arithmetic parser
            sub.latest_values[target_var] = self._safe_ast_evaluate(eval_str)

    def _safe_ast_evaluate(self, expr_str):
        """
        Parses and evaluates a mathematical string safely using Abstract Syntax Trees.
        Supports: +, -, *, /, **, parenthesized expressions, abs, round, min, max.
        Prevents code execution and sandbox escapes completely.
        """
        import ast
        import operator as op

        # Whitelisted operators
        operators = {
            ast.Add: op.add,
            ast.Sub: op.sub,
            ast.Mult: op.mul,
            ast.Div: op.truediv,
            ast.Pow: op.pow,
            ast.USub: op.neg,
            ast.UAdd: op.pos
        }

        # Whitelisted built-in functions
        allowed_funcs = {
            "abs": abs,
            "round": round,
            "max": max,
            "min": min
        }

        # Sanitization filter: allow only typical math expressions
        clean_expr = re.sub(r'[^0-9\+\-\*\/\.\(\)\s,a-zA-Z_]', '', expr_str)

        try:
            tree = ast.parse(clean_expr, mode='eval')
            
            def _eval(node):
                if isinstance(node, ast.Expression):
                    return _eval(node.body)
                elif isinstance(node, ast.Constant): # Python 3.8+
                    return node.value
                elif isinstance(node, ast.Num): # Python < 3.8 fallback
                    return node.n
                elif isinstance(node, ast.BinOp):
                    left = _eval(node.left)
                    right = _eval(node.right)
                    return operators[type(node.op)](left, right)
                elif isinstance(node, ast.UnaryOp):
                    operand = _eval(node.operand)
                    return operators[type(node.op)](operand)
                elif isinstance(node, ast.Call):
                    if isinstance(node.func, ast.Name) and node.func.id in allowed_funcs:
                        args = [_eval(arg) for arg in node.args]
                        return allowed_funcs[node.func.id](*args)
                    raise TypeError(f"Execution blocked: Unauthorized function call '{node.func}'")
                raise TypeError(f"Execution blocked: Unauthorized node '{type(node).__name__}'")

            return float(_eval(tree))
        except Exception as e:
            # Emit logging or default gracefully to 0.0 without crashing
            return 0.0

