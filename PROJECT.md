# Project: TelemetryDashboard Enterprise Implementation

## Architecture
7-project .NET 8 solution (portable backbone + headless host + WPF desktop + browser console) at `TelemetryDashboard/TelemetryDashboard.sln`.

- **`TelemetryDashboard.Core`** — no infrastructure dependencies. Organised by domain, one namespace per folder (enforced by `ArchitectureRuleTests.NamespacesMatchFolderStructure`):
  `Analytics`, `Collections`, `Events`, `Firmware`, `Interfaces`, `Models`, `Parsers`, `Plugins`, `Protocols`, `Records`, `Recording`, `Resilience`, `Security`, `Services`, `Simulator`, `Streaming`.
- **`TelemetryDashboard.Infrastructure`** → Core. Adapters and transports:
  `Integrations`, `Network` (WebRTC, P2P mesh), `Plugins`, `Replay`, `Serial`, `Storage`, `Updater`, `WebServer` (Kestrel, MQTT, Notion, Slack).
- **`TelemetryDashboard.Plugins`** → Core. Plugin contracts and sample plugins. **Not referenced at compile time by the host** — staged into `plugins/` and discovered at runtime, so hot reload can actually unload it.
- **`TelemetryDashboard.Host`** → Core + Infrastructure + Plugins, **never UI**. Headless `net8.0` console executable: the portable way to run the backbone. `Configuration`, `Ingest`, `Startup`.
- **`TelemetryDashboard.UI`** → Core + Infrastructure. WPF (`net8.0-windows`), AvalonDock, ScottPlot, HelixToolkit. `Controls`, `Dialogs`, `Diagnostics`, `Docking`, `Services`, `ViewModels`.
- **Web assets** — `stream_client.html`, `telemetry-client.js`, `custom_widget.html`, exported `custom_dashboard.html`.
- **`TelemetryDashboard.Tests`** (`net8.0`, portable) and **`TelemetryDashboard.Tests.Desktop`** (`net8.0-windows`, WPF-dependent) — Tiers 1–4 plus architecture rules. No file is excluded from compilation in either.

## Platform reach

The WPF shell is Windows-only and always will be. "Runs on every computer" is delivered by the headless host plus the browser console — the desktop app is one client among several, not the product.

| Runs the hub | Reaches the hub |
|---|---|
| Windows, macOS, Linux (x64 / arm64) via `TelemetryDashboard.Host` | Any browser: desktop, tablet, Android, iOS |

Android and iOS are **clients, not hosts**. The backbone opens serial ports, writes files and holds a listening socket open indefinitely — none of which a mobile OS grants a background app. Running it there would mean a worse hub reachable from fewer places.

Verified: `dotnet publish -r linux-x64` and `-r osx-arm64` both succeed and emit zero Windows-only assemblies. Not yet verified: `HttpListener`'s WebSocket path executing on a real Linux or macOS machine. `telemetry-client.js` falls back to SSE when the WebSocket handshake fails, so the console degrades rather than breaks if it turns out to be limited there.

Two portability details that were silently broken and are now explicit:

- **Binding.** `TelemetryStreamingServer` binds loopback only by default; `acceptRemoteConnections` opens every interface. Loopback stays the default because the endpoint has no authentication and accepts commands over the WebSocket — exposing it is an operator's deliberate choice, not a default's.
- **Hot-plug.** `Win32HotPlugHook` only fires when a Win32 message pump forwards `WM_DEVICECHANGE`, so device arrival and removal went undetected off Windows and in any headless host. `PortPresencePoller` polls the port list instead, and `MultiPortSerialManager.HotPlugDetectionActive` reports whether detection is genuinely running.

## Feature Inventory
| # | Feature | Implementation | Milestone | State |
|---|---------|----------------|-----------|-------|
| 1 | Auto-Healing & RingBuffer | `Infrastructure.Serial.AutoReconnectEngine`, `Core.Collections.RingBuffer<T>`, `Serial.ZeroLossPacketBuffer` | M1 | Built |
| 2 | Time-Sync Jitter Buffer | `Core.Services.TimeSyncJitterBuffer` | M1 | Built |
| 3 | Circuit Breaker & Clamping | `Core.Resilience.TelemetryCircuitBreaker` | M1 | Built |
| 4 | AES-256-GCM & Ed25519 | `Core.Security.AesSecurityProvider`, `Ed25519`, `Ed25519Point`, `MeshPacketCodec` | M1 | Built |
| 5 | Gorilla Bit Compression | `Core.Services.GorillaCompressor` | M1 | Built |
| 6 | No-Code Web Builder | `Core.Services.DashboardExporter` → `custom_dashboard.html` | M2 | Built |
| 7 | 2D Visual Node Wires | `stream_client.html` | M2 | Built |
| 8 | DVR Time-Travel & Report | `Core.Recording.TimeTravelDvrPlayer`, `Infrastructure.Replay.IncidentReportGenerator` | M2 | Built |
| 9 | Right-Docked Event List UX | `UI.Controls.ControlPanelControl` | M2 | Built |
| 10 | Adaptive Dynamic Sampling | `Core.Services.AdaptiveSamplingController` | M3 | Built |
| 11 | Multi-Channel Alert Forwarder | `Infrastructure.Integrations.MultiChannelAlertForwarder` (Slack, Discord, Telegram, generic webhook) | M3 | Built |
| 12 | LLM Diagnosis & Emergency Control | `Core.Services.LlmDiagnosisAgent`, `EmergencyMcuController` | M3 | Built |
| 13 | Hot-Reload Plugin Sandbox | `Core.Plugins.ScriptPluginSandbox` + formula, managed, `JavaScriptEngine` (Jint), `PythonScriptEngine` (IronPython) | M4 | Built |
| 14 | Remote MCU OTA Flasher | `Infrastructure.Serial.EdgeMcuOtaFlasher`, `Core.Firmware.IntelHexParser` | M4 | Built |
| 15 | Industrial Protocol Adapters | `Core.Protocols.{CanBus,Modbus,Ros2}BridgeAdapter` via `ProtocolBridgeRegistry` | M4 | Built |
| 16 | Test Suite Alignment & E2E | `TelemetryDashboard.Tests`, Tiers 1–4 + `ArchitectureRuleTests` | M5 | Built |

**Feature 13 runs all four languages for real.** `FormulaScriptEngine` (expressions), `ManagedAssemblyScriptEngine` (C#/.NET in a collectible `AssemblyLoadContext`), `JavaScriptEngine` (Jint) and `PythonScriptEngine` (IronPython). Both interpreters are pure managed code — no Node, no CPython, no native binary — so a script behaves identically on every platform the host runs on. `HotReloadPluginSandbox` registers all four, which is what makes a `.js` or `.py` file in `plugins/` execute rather than be listed as an unsupported extension.

The one limit worth stating: IronPython is Python 3.4-level and cannot load C-extension packages, so `numpy` and similar are unavailable. A host needing them can override `PythonNetAdapter.Interpreter` with a CPython shim.

## Milestones
| # | Name | Dependencies | Status |
|---|------|--------------|--------|
| M1 | Resilient Data Ingestion & Safety | none | Built |
| M2 | High-UX Web Console & DVR | none | Built |
| M3 | ML Analytics, Sampling & Alerts | M1 | Built |
| M4 | Sandbox, OTA Flasher & Gateway | M1 | Built |
| M5 | E2E & Unit Test Verification | M1–M4 | Built |
| M6 | Universal `DataRecord` generalisation | M1–M5 | Built |
| M7 | Wiring the backbone: ingest accounting, outbound relays | M1–M6 | Built |

### What "Built" means here — and what it does not

**Built** = the implementation exists and the test suite covers it. It does **not** mean field-proven, and — this is the distinction the project got wrong for a long time — it does not mean *reachable*.

A separate audit measured how many public production types are referenced by nothing at all. The first pass counted `.cs`, `.xaml` and `.csproj` references and reported **44 of 244** — and that number was wrong, in the flattering direction. It tokenised XML doc comments, so a type stayed "wired" as long as some surviving file still mentioned it in a `<see cref="..."/>`. That is exactly the situation around abandoned code, where the prose outlives the call. Stripping comments surfaced eight more: `AnomalyEngine`, `ExtensionRegistry`, `GitHubUpdater`, `MqttPublisher`, `NotionClient`, `PythonNetAdapter`, `SqliteIndexRepository` and `Twin3DService`.

The rule now lives in `ArchitectureRuleTests.Wiring.cs` with a ratchet baseline that may only shrink, so this class of defect fails a build instead of waiting for an audit. Retired from the baseline so far, each by being genuinely wired rather than excused: `TelemetryCircuitBreaker`, `RecordPipeline`, `NumericPacketStage`, `StageActivity`, `AutoReconnectEngine`, `PluginHostContext`, `PluginManager`, `ManifestIndexMarketplace`, `MatFileWriter`, `SlackClient`, `MqttPublisher` and `GitHubUpdater`.

`ArchitectureRuleTests.NoNewProductionTypeIsUnreachable` now fails on any *new* unreachable type, and `EveryUnwiredTypeInTheBaselineIsStillUnwired` forces an entry off the baseline the moment it gains a reference, so the list can only shrink. Treat the baseline as the outstanding work it is.

Current suite: **948 passing, 0 build warnings** — 880 in the portable project, 68 in the desktop one.
This line records the size of the suite, not a claim that it is green: the count is checked against a
real run, and if two are failing while something is mid-change, that is what the run says and what
should be reported.

### Verification that does not come from the test suite

Two kinds of check exist alongside it, because there are defects unit tests structurally cannot
reach — they feed the code data the tests themselves invented, and reality is less cooperative.

**`verify_live.py`** starts the real binary against public infrastructure nobody here controls
(Wikimedia EventStreams, the USGS earthquake feed) and asserts on what comes back. Twenty-three
checks. It has already found four defects that a green suite did not: a missing `User-Agent` that
Wikimedia answers with 403, a start-up banner announcing "no source" while an SSE reader was
running, a forecast of *minus 228,000 bytes* for a page size, and a forecast field whose name
promised sixty seconds while carrying two.

**`PortabilityHazardTests`** hunts, on Windows, for the failures that only appear elsewhere. The
Linux and macOS binaries have never been run — there is no machine here — so instead of waiting,
this looks for the specific and well-known ways a Windows-developed .NET program breaks: literal
backslashes in paths, numbers parsed without a fixed culture, case-mismatched file references,
unguarded Windows-only APIs, and the Turkish dotted-I that stops `SIM:` and `HIST` matching. It
found a real one: `PrefixParser`, the primary telemetry frame parser, used the ambient culture in
one place and `InvariantCulture` in six others.

That is not the same as running it, and nothing here should be read as proof that it runs. It turns
"we have no idea" into "these particular things are ruled out", which is the difference between an
unknown risk and a bounded one.

### Standalone packages

`build_standalone.ps1` refuses to publish from a red tree, then produces self-contained
single-file builds that need no .NET installed:

| Package | Size | Verified |
|---|---|---|
| `host-win-x64` | 114 MB | Runs. Reports its own bundled runtime (.NET 8.0.28, not the machine's .NET 10), loads the sample plugin, serves `/stream` and `/api/status`. |
| `host-linux-x64` | 110 MB | Valid ELF64 x86-64. **Not executed** — see below. |
| `host-osx-arm64` | 120 MB | Valid Mach-O ARM64. **Not executed.** |
| `desktop-win-x64` | 220 MB | WPF console, Windows only by construction. |

Trimming is deliberately off. Jint, IronPython and the collectible `AssemblyLoadContext` that loads
plugins all resolve types by reflection, which the trimmer cannot see. A trimmed build would be
smaller, publish cleanly, and fail the first time an operator loaded a plugin. The published
Windows host printing `[plugin:sample.plugin] SampleTelemetryPlugin initialized.` is the check that
this path survives single-file packaging.

**Linux and macOS execution is unverified.** The binaries have correct executable headers and the
backbone projects pass `PortableBackboneProjectsTargetNoSpecificPlatform`, but both are static
checks. Nothing in this repository has been observed running on either platform.

### M7 — wiring, and what it exposed

M7 has no new headline feature. It exists because the reachability audit found that several of the
project's headline features were not reachable from any running program, and the fix for that is
not more code but connections — plus the defects that only surface once something is actually
connected to something else.

**The ingest path now accounts for everything that arrives.** A line no routing rule matched and
the positional parser rejected used to fall out of the loop with no counter and no message, so
"the device is not transmitting" and "the device is transmitting something this host cannot read"
produced the same symptom: an empty chart. Those lines now enter the M6 record path as
`DataValue.Text`, are tallied by shape, and one verbatim example of each is kept for the shutdown
report. This is also what gives M6 a producer: `RecordPipeline`, `NumericPacketStage` and
`StageActivity` carried nothing at all before it.

**`TelemetryCircuitBreaker` is in force.** `IngestRateGuard` gates every sample by channel. Its own
default limit of 50,000/s could not be reached over a serial link at all, so the host sets 5,000 —
still an order of magnitude above any real device. Isolation is announced on the console and the
dropped total is reported at shutdown, because dropping measured data silently is the failure this
codebase exists to prevent.

**Auto-reconnect needed a real bug fixed first.** `SerialPortWorker`'s read loop ended in a bare
`catch { }`. Pulling the cable threw, the loop exited, and nothing else changed: the port stayed
marked `Connected`, the worker stayed in the manager's table, and since the connect path returns
early for a port it already holds, reconnection was impossible for the life of the process. The
worker now reports its own death, the manager forgets it, and `AutoReconnectEngine` — previously
constructed by nothing — retries. **The end-to-end recovery is not covered by an automated test**:
it needs a physical port that can be unplugged mid-run, and that is not claimed as verified.

**The headless host can now report outward at all.** It had no outbound path whatsoever, which
meant the retry-on-429 work done on `SlackClient`, `NotionClient` and `GitHubUpdater` was
correctness applied to code no user could reach. Three flags, all opt-in:

| Flag | What it does |
|---|---|
| `--slack-webhook <url>` | Posts judged anomalies, at most one per channel per 5 minutes; what was held back is counted and reported with the next message. |
| `--mqtt <host[:port]>` | Republishes every scored sample to `<prefix>/<node>/<variable>`. |
| `--check-updates <owner/repo>` | Asks a release feed once at start-up and prints the answer. Downloads and applies nothing. |

Verified against a stub broker that decodes MQTT 3.1.1 off the socket independently of the
publisher under test: 350 `PUBLISH` packets from the real executable across 5 topics, every payload
carrying `simulated: true`, and **330 of 350 carrying a `zscore`** — the first four samples per
channel omit the field because the host had reached no verdict yet. The warm-up honesty rule holds
on the wire, not only in a unit test.

Relays hand off to a bounded queue rather than publishing inline, so a stalled broker cannot freeze
the console. Two defects in that queue were found by its own tests: `BoundedChannelFullMode.DropWrite`
discards an item while returning `true`, so the drop counter never fired — silent loss in the class
written to prevent silent loss; and because `ISlackClient.SendAlertAsync` takes no cancellation
token, a webhook that stopped answering would have held the process open at shutdown forever.

### M6 — the universal record layer (`Core/Records`)
The pipeline is typed on `double` end to end, which is what makes Gorilla compression and z-score analytics possible; widening `Value` to `object` would destroy both. M6 therefore adds a layer **above**, not a type change below:

```
DataRecord = identity (Stream, Key, Timestamp)
           + value    (Numeric | Text | Instant | Flag | Blob)
           + provenance (IsDerived, producer id)
```

A `Numeric` record projects losslessly onto `TelemetryPacket` (`TelemetryPacketProjection`) and inherits the whole existing pipeline via `NumericPacketStage`. Other kinds flow through routing, storage, streaming and plugins but **decline** numeric analytics instead of faking them, via `IRecordStage.CanHandle(DataValue)`; `RecordPipeline` counts every refusal per stage, so "this analyser saw 4,000 records and scored none" is visible rather than inferred from an empty chart.

Non-numeric domains gain anomaly detection through `DerivedNumericProjection`, which stamps everything it emits with the projection that produced it. `M6UniversalRecordTests` demonstrates the case end to end: a clinic appointment book carrying only `Instant` values is scored above 3σ by the same `TelemetryMlAnalyticsEngine` that watches a power converter — no change to the engine, no value invented.

### M7 — the profile decides, all the way to disk
The simulator, the recorder and the archive each used to carry one customer's rig in code. Selecting
a different profile changed the sliders and the captions while the data underneath stayed theirs, so
an operator watching a kiln read a battery converter with new labels on it. `ProfileSimulatorEngine`
replaces `DualMcuVirtualSimulatorEngine`, and recording moved from the display tick onto the ingest
path, so what reaches the CSV and the durable archive is whatever actually arrived.

What it produces is **shapes, not physics**. It has no model of any machine: each channel wanders
around its setpoint inside the range the profile declares, and channels stay independent, because
inventing a correlation would be fabricating a relationship — the same defect as a fabricated
reading, one level up, and harder to notice because a plausible correlation is what a reader expects.

Driving the finished application through its own UI, rather than asserting in a unit test, found
four defects a green suite had not:

| Defect | Why it mattered |
|---|---|
| The desktop shell registered **no routing rules at all** | Every frame missed the router and fell to a fallback that named the first number in the line `Temperature`, the second `Humidity`, and so on. A pressure reading was charted, alarmed on and archived as a temperature, under a heading an operator has every reason to trust. |
| `XorChecksum.Calculate(char)` truncated each char to its low byte | The generated firmware macro XORs *bytes*. `°C` is one char and two UTF-8 bytes, so the two sides disagreed and the frame was dropped as corrupt — silently, since a checksum failure is indistinguishable from line noise. The default profile ships a channel measured in °C. |
| Real hardware never reached the durable archive | The MATLAB export reads only the archive, so it worked in the demo and returned an empty file for every deployment with actual hardware attached. |
| Simulated readings were archived with no mark | `SIM:` prefix and the `Simulated` flag are now set at the router, so the store cannot be mistaken for a record of the real machine. |

The checksum defect had been invisible because the test helper reimplemented the same truncation it
was meant to check. Both sides wrong in the same way is the one failure a hand-rolled test copy
cannot detect; `CalculateXorChecksum` now delegates to the production routine.

Verified by automation against the running binary: selecting the generic profile records
`SIM:generic-machine.{ambient.temperature, ambient.humidity, machine.vibration, machine.speed}`;
selecting the bundled UPS example records `SIM:COM3.{grid.voltage, dab.bus_voltage,
psfb.output_voltage, server.load}` — four different channels, in the CSV and in SQLite, with every
forecast stating the horizon it can actually support (2 s where supportable, 0 where not).

Two measurements in that work were wrong, and both are worth recording because each cost more than
the defect it was chasing.

A UIA `FindAll(Descendants)` runs **inside** the target process, so probing the window during a run
made the app appear to burn 3.4 CPU cores. Measured without the probe it uses **0.15 cores while
streaming**. The instrument was the load — the same lesson `capture_ui.ps1` already carries about
DPI-unaware screenshots.

And `TieredStorageBaselineBenchmarkTests` takes around seven minutes for its million-row case,
longer than the other 899 tests together. Under a two-minute inactivity watchdog it was reported as
a hung test host, complete with a crash dump; the run before that simply said "test run aborted".
Both looked exactly like a deadlock. The benchmarks now carry `Category=Benchmark`, so a routine run
is `dotnet test --filter "Category!=Benchmark"` and the full number is a deliberate choice.

Chasing those intermittent failures found a third product defect, and this one would have reached an
operator. `JavaScriptEngine` gave a plugin **two seconds of wall clock** to load. Loading a one-line
filter took eleven seconds while a storage benchmark ran alongside it, so Jint raised a timeout,
`Load` returned null, and the sandbox moved to the next module — a valid plugin was simply absent,
and `LastError` read like a syntax error, pointing the operator at a file with nothing wrong in it.

Wall time was the wrong instrument: it measures how busy the machine is as much as what the script
does. `MaxStatements` is the guard that actually bounds a runaway plugin and does so identically
everywhere. `JavaScript_RunawayLoop_IsStoppedWithoutHelpFromTheClock` pins that — it hands the engine
a **one-hour** deadline and a `while(true)`, and the call still returns — so raising the wall-clock
backstop to ten seconds costs nothing that was protecting anybody. A timeout now says it is a limit
on time rather than a fault in the script.

**Suite: 970 passing, 0 failing** (902 portable + 68 desktop) — 6 m 16 s for everything, 1 m 14 s
with `--filter "Category!=Benchmark"`. The intermittent
failures were all one story: heavy benchmarks running in parallel with timing-sensitive tests. Two
tests were fixed at the cause rather than loosened — the JavaScript load timeout above, and
`Win32HotPlugHook_RapidMessages_DebouncesToSingleEvent`, which slept between messages so a stalled
machine pushed its own burst outside the 200 ms window it was testing. It now sends the burst with
no gap and checks that premise before asserting on it.

## Interface Contracts
### Data Ingestion & Safety (M1)
- `RingBuffer<T>` — thread-safe ring buffer; overflow is counted, never silent
- `TimeSyncJitterBuffer` — `AddSample(nodeId, timestamp, value)`, `GetAlignedSample(masterTimestamp)`
- `TelemetryCircuitBreaker` — amortised O(1) rate accounting, `UiClampRatePerSec`
- `AesSecurityProvider` — AES-256-GCM; short keys are hashed, never zero-padded
- `Ed25519` — `GenerateKeyPair()`, `DerivePublicKey(seed)`, `Sign(message, privateSeed)`, `Verify(message, signature, publicKey)`.
  `Verify` takes a **public key**, not the seed. The group order `L` is derived from its definition; a wrong literal here is what a signature-forgery fallback was once built to paper over.
- `GorillaCompressor` — `CompressDoubles`, `DecompressDoubles`

### Web Console & DVR (M2)
- `DashboardExporter` — widget layout → standalone `custom_dashboard.html`
- `TelemetryStreamingServer` — WebSocket `/ws`, SSE `/stream`, `/api/status`, `/api/dvr/replay`, `/api/dvr/report`
- `DvrFrame` — separates observation (`Value`) from verdict (`ZScore`, `IsAnomaly`, `AnalyzerId`). `HasVerdict` distinguishes "scored at 0.0" from "never examined"; consumers must check it before rendering either field.
- `ControlPanelControl` — event log, node controls, z-score breakdown

### ML Analytics, Sampling & Alerting (M3)
- `TelemetryMlAnalyticsEngine` — `AnalyzeChannel` returns an `AnomalyResult` whose `AnalyzerId` is **null during warm-up**, because no baseline exists yet
- `AdaptiveSamplingController` — base ↔ burst rate state machine
- `MultiChannelAlertForwarder` — Slack Block Kit, Discord embeds, Telegram, generic webhook; injectable clock
- `LlmDiagnosisAgent`, `EmergencyMcuController` — natural-language diagnosis and Z > 3.5 emergency dispatch

### Sandbox, Remote OTA & Gateway (M4)
- `IScriptEngine` — implemented by `FormulaScriptEngine`, `ManagedAssemblyScriptEngine`, `JavaScriptEngine`, `PythonScriptEngine`
- `ScriptPluginSandbox` / `HotReloadEngine` — `FileSystemWatcher` over `plugins/`, 250 ms debounce, collectible load contexts
- `IPluginContext` → `PluginHostContext` — hands a plugin the router, serial manager and data logger, and nothing else; plugin log lines are tagged `[plugin:<id>]`
- `EdgeMcuOtaFlasher` — `.bin` and Intel HEX, chunked OTA with CRC-32 and a retry limit
- `IProtocolBridge` — `CanBusBridgeAdapter`, `ModbusBridgeAdapter`, `Ros2BridgeAdapter`
- `IDataLogger` → `SqliteDataLogger` — durable store; `ChannelDataLoggerDrain` pumps the in-memory buffer into it and flushes on shutdown
- `IMarketplaceService` → `ManifestIndexMarketplace` — catalogue from a JSON index at a URL or local path; one malformed entry never discards the rest

## Executable Architecture Rules
`TelemetryDashboard.Tests/ArchitectureRuleTests.cs` enforces what prose could not:
- Core depends on neither Infrastructure nor UI, and never on WPF
- No test file is excluded from compilation (baseline empty; may only shrink)
- The host does not compile against the plugin project
- Every interface in `Core.Interfaces` has an implementation
- Namespaces match folder structure
- No production file hard-codes an anomaly score — as a literal, a pre-formatted sigma string, or a scenario-keyed ternary
- 150 lines per file, exemptions keyed by path and retired automatically once a file drops back under

## Code Layout
```
TelemetryDashboard/
├── TelemetryDashboard.sln
├── TelemetryDashboard.Core/          Analytics Collections Events Firmware Interfaces
│                                     Models Parsers Plugins Protocols Records
│                                     Recording Resilience Security Services
│                                     Simulator Streaming
├── TelemetryDashboard.Infrastructure/ Integrations Network Plugins Replay Serial
│                                      Storage Updater WebServer
├── TelemetryDashboard.Plugins/       SamplePlugins
├── TelemetryDashboard.Host/          Configuration Ingest Startup        (net8.0, headless)
├── TelemetryDashboard.UI/            Controls Dialogs Diagnostics Docking Services ViewModels
├── TelemetryDashboard.Tests/         Tiers/Tier1..Tier4, ArchitectureRuleTests   (portable)
└── TelemetryDashboard.Tests.Desktop/ WPF-dependent tests                 (net8.0-windows)
```
Web assets live at repository root: `stream_client.html`, `telemetry-client.js`, `custom_widget.html`, `custom_dashboard.html`.
