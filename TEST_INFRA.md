# Test Infrastructure & Specification: TelemetryDashboard

## 1. Test Philosophy

The `TelemetryDashboard` test suite is constructed adhering strictly to **opaque-box, requirement-driven testing principles**:

1. **Requirement-Driven Test Derivation**: Test cases are derived strictly from formal project requirements (`ORIGINAL_REQUEST.md` R1..R4) and interface contracts (`PROJECT.md`). Tests verify specified input/output behaviors, failure states, boundary conditions, and cross-subsystem contracts rather than coupling to internal implementation details.
2. **Deterministic Expected Outputs**: Every test case uses explicit authoritative expected output sources (mathematical specifications, packet protocol rules, XOR checksum definitions, AST formula evaluations, and predefined state transitions).
3. **Self-Contained & Isolated Execution**: Each test sets up its own state independently without relying on side-effects from previous test executions. All mock hardware devices (`MockSerialDevice`), telemetry frame generators (`TestDataGenerator`), and STA thread execution helpers (`WpfTestHelper`) run in isolated memory spaces.
4. **Multi-Tiered Layered Verification**:
   - **Tier 1 — Feature Coverage**: Verifies primary functional paths for all 34 features (5 tests per feature = 170 tests).
   - **Tier 2 — Boundary & Corner Cases**: Exercises invalid inputs, edge cases, rapid state changes, corrupted data streams, missing resources, unicode boundaries, and numerical overflow conditions (5 tests per feature = 170 tests).
   - **Tier 3 — Cross-Feature Pairwise Combinations**: Tests complex interactions between coupled subsystems (e.g. Serial Stream -> Logger -> ScottPlot Scope; Virtual Simulator -> Kestrel Web Server -> Web JSON; Sensor Breach -> TTS Alert + Slack Webhook; Replay -> Helix3D Heatmap) (20 tests).
   - **Tier 4 — Real-World Application Workloads**: Simulates sustained end-to-end system workloads, high-rate packet ingestion (100k+ pts/sec), 10-second failure snapshot extractions, memory stability under load, and full application lifecycle transitions (10 tests).

---

## 2. Feature Inventory (F01 .. F34)

| Feature ID | Feature Name / Description | Target Module | Requirement Mapping | Tier 1 Tests | Tier 2 Tests | Pairwise / Workload | Total Tests |
|---|---|---|---|---|---|---|---|
| **F01** | Clean 4-Project Solution Structure | Solution Architecture | R1 | 5 | 5 | Covered | 10+ |
| **F02** | Multi-Threaded Serial Communication Manager | Infrastructure.Serial | R1 | 5 | 5 | Tier 3, Tier 4 | 10+ |
| **F03** | Win32 `WM_DEVICECHANGE` USB Hot-Plug Hook | Infrastructure.Serial | R1 | 5 | 5 | Covered | 10+ |
| **F04** | 1-Second Automatic Reconnect Engine & Resync | Infrastructure.Serial | R1 | 5 | 5 | Covered | 10+ |
| **F05** | Packet Routers (PREFIX, JSON, COLUMNS) & Checksum | Core.Parsers / Services | R1 | 5 | 5 | Tier 3 | 10+ |
| **F06** | Dynamic Algebraic Link Formula Engine (AST) | Core.Services | R1 | 5 | 5 | Covered | 10+ |
| **F07** | Profile-Driven Virtual Simulator | Core.Simulator | R1 | 5 | 5 | Tier 3, Tier 4 | 10+ |
| **F08** | C/C++ Firmware Code Generator | Core.Services | R1 | 5 | 5 | Covered | 10+ |
| **F09** | Zero-Config Auto-Baud Rate & Format Scanner | Infrastructure.Serial | R2 | 5 | 5 | Tier 4 | 10+ |
| **F10** | Windows 11 Fluent UI (Mica/Acrylic & Theme Switch) | UI.Styles | R2 | 5 | 5 | Tier 4 | 10+ |
| **F11** | AvalonDock Workspace (`DockingManager` Presets) | UI.Docking | R2 | 5 | 5 | Covered | 10+ |
| **F12** | `.workspace` Layout Profile Save/Load System | UI.Docking | R2 | 5 | 5 | Tier 4 | 10+ |
| **F13** | VS Code-Style Command Palette Overlay (`Ctrl+Shift+P`) | UI.Controls | R2 | 5 | 5 | Covered | 10+ |
| **F14** | Universal Drag-and-Drop Support | UI.Controls | R2 | 5 | 5 | Covered | 10+ |
| **F15** | i18n Internationalization (Korean / English) | UI.Services | R2 | 5 | 5 | Covered | 10+ |
| **F16** | Operator View vs Engineer Password Guard | UI.Services | R2 | 5 | 5 | Covered | 10+ |
| **F17** | ScottPlot 5 WPF 2D Real-Time Scope Charting | UI.Controls | R3 | 5 | 5 | Tier 3 | 10+ |
| **F18** | FFT Frequency Domain Analysis Scope | UI.Controls | R3 | 5 | 5 | Covered | 10+ |
| **F19** | Interactive Delta Cursor Measurement Tools | UI.Controls | R3 | 5 | 5 | Covered | 10+ |
| **F20** | HelixToolkit WPF 3D CAD Renderer (.obj/.stl) | UI.Controls | R3 | 5 | 5 | Tier 3 | 10+ |
| **F21** | Real-Time Thermal Heatmap Overlay (IDW) | UI.Controls | R3 | 5 | 5 | Tier 3 | 10+ |
| **F22** | Multi-Sensory Alert UX (Neon, Toast, SAPI TTS) | UI.Services | R3 | 5 | 5 | Tier 3 | 10+ |
| **F23** | Interactive Signal Generator & Closed-Loop Tester | UI.Controls | R3 | 5 | 5 | Covered | 10+ |
| **F24** | AI / Statistical Anomaly Detection Engine (EWMA/Z) | Core.Services | R3 | 5 | 5 | Covered | 10+ |
| **F25** | Extension Marketplace & `ExtensionStoreDock` UI | UI.Controls | R4 | 5 | 5 | Covered | 10+ |
| **F26** | `FileSystemWatcher` Hot-Reload Engine | Infrastructure.Plugins | R4 | 5 | 5 | Tier 4 | 10+ |
| **F27** | Hybrid High-Speed Data Logger (SQLite + MAT/CSV) | Infrastructure.Storage | R4 | 5 | 5 | Tier 3, Tier 4 | 10+ |
| **F28** | Kestrel Embedded Web Server (`:8080` Live Stream) | Infrastructure.WebServer| R4 | 5 | 5 | Tier 3 | 10+ |
| **F29** | Notion REST API Automated Report Generator | Infrastructure.Integrations| R4 | 5 | 5 | Covered | 10+ |
| **F30** | Slack Webhook Block Kit Alert Publisher | Infrastructure.Integrations| R4 | 5 | 5 | Tier 3 | 10+ |
| **F31** | MQTT Cloud Broker Publisher | Infrastructure.Integrations| R4 | 5 | 5 | Covered | 10+ |
| **F32** | Time-Machine Session Replay & Failure Snapshot | Infrastructure.Replay | R4 | 5 | 5 | Tier 3, Tier 4 | 10+ |
| **F33** | GitHub Releases Hot-Swap Auto-Updater | Infrastructure.Updater | R4 | 5 | 5 | Covered | 10+ |
| **F34** | Self-Contained Single-File Portable Packaging | Infrastructure / App | R4 | 5 | 5 | Covered | 10+ |

---

## 3. Test Architecture & Directory Layout

The test suite is contained entirely within the `TelemetryDashboard.Tests` project:

```text
TelemetryDashboard/
└── TelemetryDashboard.Tests/
    ├── TelemetryDashboard.Tests.csproj
    ├── GlobalUsings.cs
    ├── EmpiricalCoreVerificationTests.cs
    ├── InfrastructureEmpiricalTests.cs
    ├── TestUtilitiesTests.cs
    ├── TestUtilities/
    │   ├── WpfTestFactAttribute.cs        # [WpfFact] and STA thread execution helper
    │   ├── MockSerialDevice.cs            # Synthetic COM port emulator & byte stream generator
    │   └── TestDataGenerator.cs           # Telemetry frame synthesizer (PREFIX, JSON, COLUMNS)
    └── Tiers/
        ├── Tier1_FeatureCoverage/         # 34 files (F01_SolutionStructureTests.cs .. F34_SingleFilePortableTests.cs)
        ├── Tier2_BoundaryCornerCases/     # 5 files (Core, Simulator, UI, Visualization, ExtensionStorage)
        ├── Tier3_PairwiseCombinations/    # 4 files (Serial->Storage->Scope, Sim->Kestrel->Web, Alert->Slack, Replay->Helix3D)
        └── Tier4_RealWorldWorkloads/      # 2 files (SustainedTelemetryStressTest, FullApplicationLifecycleE2ETest)
```

---

## 4. Real-World Application Scenarios (Tier 4)

1. **High-Throughput Telemetry Stress & Pipeline Stability**:
   - Simulates 100,000+ packets/second incoming telemetry stream over synthetic memory channels.
   - Verifies lock-free packet processing via `System.Threading.Channels`, zero memory leaks (garbage collector delta < 20MB), and zero lost packets.
2. **10-Second Failure Snapshot & Time-Machine Session Replay**:
   - Executes continuous logging to SQLite and MAT binary files while generating an artificial critical sensor failure.
   - Verifies rolling buffer snapshot extraction around the exact failure timestamp and replays recorded session through `SessionReplayPlayer` with variable playback speed controls (0.5x .. 10x).
3. **Full End-to-End Application Lifecycle**:
   - Executes complete lifecycle flow: app startup -> virtual MCU connection -> auto-baud rate scanning -> live scope rendering -> threshold breach alert triggering -> Notion report generation -> extension plugin hot-reloading -> workspace profile serialization -> teardown.

---

## 5. Coverage Thresholds & Targets

- **Tier 1 (Feature Coverage)**: 170 Test Cases — **100% Pass Required**
- **Tier 2 (Boundary & Corner Cases)**: 170 Test Cases — **100% Pass Required**
- **Tier 3 (Cross-Feature Pairwise)**: 20 Test Cases — **100% Pass Required**
- **Tier 4 (Real-World Workloads)**: 10 Test Cases — **100% Pass Required**
- **Total Suite**: **370 Test Cases** — **100% Pass Required**
