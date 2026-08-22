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
| 2 | Time-Sync Jitter Buffer | `Core.Services.TimeSyncJitterBuffer` via `/api/aligned` | M1 | Built |
| 2b | Computed Channels | `Core.Analytics.ComputedChannel` via `/api/computed`; published live by `Host.Ingest.ComputedChannelPump` | M2 | Built |
| 2c | Engineering Limits | `Core.Analytics.ChannelLimit` + `LimitMonitor` via `/api/limits`, declared by profile or `--limit` | M3 | Built |
| 12b | Loopback Serial Port | `Host.Ingest.LoopbackSerialManager` over `Core.Simulator.MockSerialPort`, via `--serial loopback` | M3 | Built |
| 12c | Operator Control | `Core.Streaming.ControlEndpoint` via `/api/control`, generated sources only | M3 | Built |
| 3 | Circuit Breaker & Clamping | `Core.Resilience.TelemetryCircuitBreaker` | M1 | Built |
| 4 | AES-256-GCM & Ed25519 | `Core.Security.AesSecurityProvider`, `Ed25519`, `Ed25519Point`, `MeshPacketCodec` | M1 | Built |
| 5 | Gorilla Bit Compression | `Core.Services.GorillaCompressor` | M1 | Built |
| 6 | No-Code Web Builder | `Core.Services.DashboardExporter` + `ProfileDashboardWidgets`, via `telemetry-host --export-dashboard` | M2 | Built |
| 7 | 2D Visual Node Wires | `stream_client.html` | M2 | Built |
| 8 | DVR Time-Travel & Report | `Core.Recording.TimeTravelDvrPlayer`, `Infrastructure.Replay.IncidentReportGenerator` | M2 | Built |
| 9 | Right-Docked Event List UX | `UI.Controls.ControlPanelControl` | M2 | Built |
| 10 | Adaptive Dynamic Sampling | `Core.Services.AdaptiveSamplingController` | M3 | Built |
| 11 | Multi-Channel Alert Forwarder | `Infrastructure.Integrations.MultiChannelAlertForwarder` (Slack, Discord, Telegram, generic webhook) | M3 | Built |
| 12 | LLM Diagnosis & Emergency Control | `Core.Services.LlmDiagnosisAgent`, `EmergencyMcuController` via `telemetry-host --emergency-stop` | M3 | Built |
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
| The scope was fed by **two** paths at once | The display tick pushed each profile channel's *label* from the built-in physics model while the ingest consumer pushed the same channel's *id* from the profile simulator, so every quantity appeared twice under two names carrying different numbers — the same sensor disagreeing with itself. Verified after the fix by driving the app: four toggles, `ambient.temperature`, `ambient.humidity`, `machine.vibration`, `machine.speed`. |

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

### Feature 6 reached a user for the first time
`DashboardExporter` was marked Built in M2 and constructed by nothing, so no running program could
produce a dashboard and the page it emitted had never been opened. `telemetry-host
--export-dashboard <file>` writes one now, built from the profile in force: one reading card and
one trend per declared channel, in that channel's own unit and range.

Making it reachable exposed three faults that had survived precisely because it was not:

- The connection chip was the literal text **WS CONNECTED**, written into the markup and updated by
  nothing. `TelemetryClient` had been reporting `CONNECTED`/`DISCONNECTED`/`ERROR` all along through
  `onStatusChange`; the page simply never asked.
- A widget whose field was absent from a packet fell back to `data.temp`, and then to `0` — so a
  card headed with one quantity could show another's reading, or a confident zero, with nothing on
  screen to say which. Worse, the fields it asked for (`temp`, `vin`) are not the ones this wire
  format carries at all, so every card would have sat at its placeholder forever.
- Port 8080 was hardcoded in the script tag, the socket URL and the chip, so a host on any other
  port exported a page pointing at nothing while claiming to be connected.

Verified by running the page's **own script** — extracted from the exported file, not a copy —
against live packets from a running host (`verify_dashboard.js`): eight checks covering both
profiles, including that a card whose channel never reported still shows no value, and that a packet
for an unknown channel changes nothing. The browser pane in this environment would not composite, so
what the page *looks* like is still unverified and marked as such.

### Feature 12: the one path that acts on the machine
`EmergencyMcuController` was marked Built in M3 and constructed by nothing, so the only feature that
*writes to* hardware rather than reading it could not be reached from any running program.
`telemetry-host --emergency-stop` arms it: above `--emergency-sigma`, transmit `--emergency-command`
to the serial port, at most once per channel per `--emergency-cooldown`.

Two refusals are the design, and both are verified against the real binary:

```
--simulate --emergency-stop   → refused: needs --serial. The interlock transmits to the port
                                you opened; this host will not choose one to write to.
--emergency-sigma 4.0         → refused: tuning an interlock that is switched off does nothing.
```

That matters because the controller ships a default rule that **auto-executes against a port
literally named `COM3`**. A host that armed itself would have been writing to whatever happened to
be on that port. The relay discards that rule rather than adding to it, and registers only what the
operator configured.

Wiring it also found a defect in the controller: a trigger held back by the cooldown did a bare
`return false` — no history entry, no event — while a trigger held back by the disarm interlock was
recorded. So a channel that stayed over threshold for a minute produced one history entry and then
silence, and a reader could not tell a throttled storm from a condition that cleared. Suppressions
are now recorded and counted, the way every other relay here reports what it held back.

**Not verified, and marked so:** the byte leaving a serial port. That needs hardware this repository
does not have. Everything in front of it is — when it arms, when it refuses, what it sends, where it
sends it, and that a warm-up sample with no verdict can never trip it.

### A real console for the DAB/PSFB project
`power_ups_psfb_dashboard.html` was a mockup. `380.0 V`, `60.0 Hz`, `12.8 kW`, a grid-frequency
readout and a received-power readout were all typed into the markup with no code to update them —
and the host sends neither frequency nor power, so those numbers could never have moved. Of the
whole wire frame it read only `source`, `type`, `port` and `nodeId`.

Where it did read measurements it matched variable names by substring, so
`vName.includes('voltage')` caught `grid.voltage`, `dab.bus_voltage` **and** `psfb.output_voltage`
and wrote all three into the DAB bus field — three measurements from three points in the power chain
overwriting each other in one box, several times a second.

`dab_psfb_console.html` replaces it: the four channels the `dab-psfb-ups` profile actually declares,
laid out as the chain they form (계통 → DAB → PSFB → 서버). Exact channel-id matching, a channel that
has never reported stays at `—`, anomaly colour comes from the detector's verdict rather than a
threshold re-derived in the page, warm-up reads `기준선 학습 중` instead of `0.00σ`, a channel that
goes quiet dims and shows its age, and a synthetic stream raises a banner that cannot be missed.

Verified by `verify_dab_console.js`, which runs the page's own script against live packets — twelve
checks, including the one the old page could not have passed:

| channel | value | its declared range |
|---|---|---|
| `grid.voltage` | 394 V | 0–440 |
| `dab.bus_voltage` | 403 V | 350–450 |
| `psfb.output_voltage` | 48.44 V | 38–54 |
| `server.load` | 83.2 % | 10–100 |

Three voltages, three cards, each inside its own range. What the page *looks* like is still
unverified — that needs a browser.

### A spectrum endpoint, and the two defects it found
`FftAnalyzerService` — a real radix-2 Cooley-Tukey transform, written in M2 — lived in
`UI/ViewModels`. The headless host must never reference the WPF project, so the one place a
spectrum reaches every client, an endpoint any browser can call, could not have it. Its address was
the mistake, not its contents; it now sits in `Core/Analytics` behind `/api/spectrum`.

The endpoint measures the sample rate from the timestamps rather than taking it from configuration,
because a telemetry stream never arrives on a metronome and a spectrum labelled with an assumed rate
puts every peak in the wrong place while looking entirely plausible. It removes the mean before
transforming — a 400 V bus with a 2 V ripple is the ordinary case — and never reports bin zero as
the peak, since the mean is the largest bin for nearly every channel and calling it a peak would
make every spectrum look like it had found something.

**Its first run against live data found a defect in the series store.** `TelemetryFrameRecorder`
keyed channels on the node id and the JSON field name, and skipped `variable` because it is a
string — so every channel of a profile was written into one series called `<node>.value`. A
four-channel run served temperature, humidity, vibration and speed to `/api/series` as one
interleaved mixture, which is what any browser drawing a chart was reading. The arithmetic said so
plainly: the merged series reported **36 Hz for a 10 Hz channel**, and its spectrum peaked at
**exactly half Nyquist** — the signature of alternating unrelated quantities. Keyed on node *and*
variable, the same run reports 12 series and four distinct peaks.

**And a defect in the simulator.** The drift period came from `channel.Id.GetHashCode()`, beside a
comment claiming it was "stable across runs". .NET randomises string hashing per process: a probe
measured 58, then 40, then 40 steps for `dab.bus_voltage` in three consecutive runs. That silently
broke the promise the fixed `Seed` exists to make — seeding the noise but not the drift leaves a
demonstration that looks different every time. FNV-1a replaces it.

Verified against a running host by predicting each peak from the engine's formula, computed
independently in Python, and comparing with what the endpoint measured over the wire:

| channel | period | predicted | measured | error |
|---|---|---|---|---|
| `ambient.temperature` | 71 steps | 0.1268 Hz | 0.1230 Hz | 0.43 bins |
| `ambient.humidity` | 58 | 0.1552 | 0.1582 | 0.34 |
| `machine.vibration` | 76 | 0.1184 | 0.1142 | 0.48 |
| `machine.speed` | 93 | 0.0968 | 0.0966 | 0.02 |

All four within half a bin.

### The primary console could not display the product's own telemetry
`stream_client.html` is what the host serves by default and what "runs on every computer" rests on.
It read `data.temp`, `data.vin`, `data.vout`, `data.iin`, `data.iout`, `data.pin`, `data.pout`,
`data.efficiency`, `data.phase` and `data.status_flags` — **the hub sends none of them.** Its frame
carries `nodeId`, `variable`, `value`, `unit` and a verdict.

It had a general mechanism for exactly this: `fetch('/api/config')`, build the panels from the
answer. **No server ever implemented `/api/config`**, so the fetch failed on every load and the page
fell into a legacy path that fabricated:

```js
latestPSFB.vout = 48.0;     // a device that had reported nothing
latestDAB.vout  = 96.0;
latestPSFB.vin  = data.vin  // one converter's reading shown under the other's name
```

Numbers nobody measured, displayed as readings, on the console most users would ever see.

The replacement **discovers channels from the stream**, which removes the need for the config
endpoint entirely: what is on screen is what arrived, a list that is true by construction. One card
per channel with its own unit and verdict, a spectrum panel over `/api/spectrum`, and a DVR scrubber
over `/api/dvr/replay`. Warm-up reads `기준선 학습 중`; a DVR frame stored without a verdict reads
`판정 없음` rather than a calm 0.00σ; a channel that goes quiet dims and says how long it has been
silent.

Verified by `verify_console.js` against a running host — ten checks, including that four channels
discovered themselves and each card held its own reading:

```
4 cards for 4 channels: ambient.temperature, ambient.humidity, machine.vibration, machine.speed
29.8°C | 52.0% | 0.0600g | 1401rpm
spectrum: 294 samples, measured 9.59 Hz, peak 0.1311 Hz (period 7.63 s)
DVR: 30.6 s buffered, 172 frames in a 4 s window
```

### Every bundled example page had the same fault
The starters are teaching material: someone copies one and builds on it. All of them read
`data.temp`, `data.humidity`, `data.rpm`, `data.vibration` and friends — fields the hub does not
send — so a reader who copied one got a page that displayed nothing and no way to tell whether the
fault was theirs.

`starter_minimal.html` did worse than show nothing. It matched channel names by substring and ended
with an `else` that wrote **every unrecognised channel into the temperature box**.

All four now read the canonical frame, match on the exact channel, and show `—` until that channel
reports. `custom_dashboard.html` was a 976-line artefact of the old exporter checked into the
repository; it is regenerated from the fixed one. Verified together by `verify_starters.js`, which
captures real packets once and replays them into every page.

### Feature 2: what were all these channels at the same moment?
Channels do not arrive together. Each sample lands when its device sent it, so the question behind
every efficiency, every ratio and every phase relationship — *what were the input and the output at
the same instant* — has no answer in the raw stream. Reading the latest of each is the obvious thing
to do and is wrong by exactly the interval between them.

`TimeSyncJitterBuffer` could answer it from M1, was marked Built, and was constructed by nothing.
Wiring it meant fixing what it said first:

- It returned **`0.0` for a node that had sent nothing** — which is also a perfectly ordinary
  reading, so an unwired node and a node reading zero volts gave the same answer. A test asserted
  this as the required behaviour: `GetAlignedSample(node, 10.0).Should().Be(0.0)`.
- It **clamped silently** to the nearest sample for any instant outside its buffer, so a request an
  hour past the last sample returned that sample as though it were the value at that instant.
- Interpolated and measured values were indistinguishable, so a caller plotting both drew invented
  points that looked exactly like recorded ones.

`GetAligned` now returns the value **and how it was obtained** — Exact, Interpolated, HeldBefore,
HeldAfter or None — with the size of the gap for a held value. `/api/aligned` exposes it, and
reports how many channels answered *the instant* rather than near it, so a caller can reject a whole
reading at once.

Verified against a running host on the UPS profile — four channels at one instant, each inside its
own declared range, each labelled:

```
grid.voltage        413.61 V   Interpolated
dab.bus_voltage     392.64 V   Interpolated
psfb.output_voltage  47.78 V   Interpolated
server.load          75.59 %   Interpolated      answered the instant: 4 of 4

ago=-3   HeldAfter  gap=3.07s  answers=false      ago=0.5  Interpolated  answers=true
```

### Playing a recording back through the pipeline that wrote it
`SessionReplayPlayer` could load a recorded CSV from M2 and was constructed by nothing, so a
recording could be written and never read by the program that wrote it. `telemetry-host --replay
<file>` attaches it as a source, which makes the whole stack work on recorded data — routing, the
analytics engine, the console, the spectrum, the alignment endpoint and the DVR all behave exactly
as they do live, because from their side nothing is different.

Frames are re-encoded as `$TELE` lines rather than pushed in as packets, so the parser and the
routing rules are exercised by the replay too. The recorded z-score is deliberately dropped and
recomputed: a score stored beside the value it came from is a second copy that disagrees with the
detector after any change to it. `Origin` is `REPLAY`, because a recording played back is not a
live reading.

Wiring it found that `SessionCsvRowParser` used the `Channel` column alone as the channel name and
**dropped `NodeId`**, so two devices reporting the same channel name collapsed into one series on
replay and overwrote each other — the same defect as in the series store, in a second place.

Verified by a full round trip against the running host: record 20 s of the UPS profile, replay it at
4×, and compare what came out of `/api/series` with the CSV.

```
recorded : 173 samples, 120 distinct
replayed : 173 samples, 120 distinct
recorded values missing from replay : 0
replayed values not in recording    : 0     VALUES MATCH EXACTLY
```

A finite source ending is now reported. A stream that simply goes quiet is indistinguishable from a
source that died, and the alignment endpoint agrees — after the replay finished, every channel came
back `HeldAfter` rather than as a current reading.

### The cross-platform half of the product could not remember anything
The headless host is what "runs on every computer" rests on — the shell is Windows-only. It had no
durable store at all: a CSV transcript and a few minutes of in-memory ring. *What did this channel
do last Tuesday* had no answer anywhere on Linux or macOS, and every restart began from nothing.

`telemetry-host --archive <file>` keeps a SQLite archive of every ingested sample, served by
`/api/history` by node, channel and time window. A bounded ring in front of a drain, the same shape
the shell uses — writing to SQLite on the ingest thread would put a disk flush between two samples,
so a slow disk would appear as a gap in the telemetry rather than as a slow disk. What the ring
cannot hold is counted, because a silent gap in an archive is discovered months later by someone who
assumes the machine was quiet.

Verified by restarting: one process archives a UPS run, is killed, and a **second process with no
source at all** answers from the same file.

```
new process:  seriesChannels=0  dvrFrames=0        (nothing live, as it must be)
/api/history: SIM:COM3.psfb.output_voltage  48.090 V  48.360 V  48.460 V
```

**And it found a defect in the server that had nothing to do with the archive.** `DispatchAsync`
caught exactly three exception types; anything else escaped into a fire-and-forget `Task.Run`, was
never observed, and left the response unclosed — so the caller waited forever. A hung request is the
worst answer available: it is indistinguishable from a slow query, a wedged server and a dropped
network, and none of those lead anyone to the fault. Every route before this one happened not to
throw. A failing route now returns 500 with the reason, and that change named my own bug in the very
next request — `RoundtripKind | AdjustToUniversal` is a combination .NET rejects *before* it looks
at the input, so the endpoint threw even when given no timestamp at all.

### An alert names a time; this turns it into the run-up
`FailureSnapshotExtractor` lived in `Infrastructure/Storage` and depends on nothing but
`TelemetryPacket`. That address is why nothing could reach it — Core must not reference
Infrastructure, so the endpoint layer where an incident window is actually asked for could not have
it. `/api/incident?at=<iso>` now answers over the durable archive, so the question can be asked days
later from another machine.

The window is asymmetric on purpose: ten seconds before the failure and two after. What happened
*before* a fault is what explains it, and the tail exists only to show how the system responded.
Measured against a live host rather than assumed:

```
dab.bus_voltage   94 samples spanning 9.9s before the instant
                  19 samples spanning 1.9s after      ratio 4.9 : 1
4 channels, 452 samples, each channel's min/max and its last value before the instant
```

The instant is supplied, never discovered. The archive stores measurements and not verdicts, so this
endpoint cannot claim to have found an incident — it answers about a moment an alert or an operator
identified. A channel silent before the instant reports **no** value before it rather than offering
the first reading after, which describes a different state entirely.

**And the tests for this feature were testing a stub.** `F32_SessionReplayTests` exercised two
classes declared at the bottom of its own file: a `SessionReplayPlayerState` whose
`LoadSession(file)` returned true and set `TotalPackets = 100` **without opening the file** — so a
path that did not exist loaded successfully — and a `FailureSnapshotExtractorHelper` that
reimplemented the extractor with a *symmetric* window. A double that stands in for the thing under
test can only confirm itself. Both are gone; the assertions run against the shipping classes, and
one of them now pins the asymmetry the helper had quietly discarded.

### The flaky suite was one story
Failures moved around across full runs — a storage benchmark, a debounce test, a JavaScript load, a
downsample allocation, a streaming throughput, a circuit breaker — never the same one twice, each
passing alone. Chasing them found two genuine defects worth having (above, and the JavaScript load
timeout). The residue was xUnit running measurement-sensitive tests concurrently: tests whose
assertion *is* a measurement, measuring each other.

Eleven classes now share one `DisableParallelization` collection, so they take turns. Nothing is
excluded and no bound was widened. The routine run went from 1 m 14 s to about 2 m 35 s, and stopped
lying.

### The number a converter is judged by, which nothing reports
Every quantity this product showed was something a device had said. For a DC-DC converter that is a
real gap: the figure of merit is efficiency, and no converter has an efficiency pin. It is
`Pout / Pin` — four measurements multiplied and divided, arriving from two MCUs at two rates and
never at the same moment. The bundled example profile could not even express the halves, because it
declared four voltages and no current at all.

`/api/computed` serves expressions declared by the host, evaluated over `/api/aligned` rather than
over the latest value of each input. That distinction is the whole feature: multiplying a voltage
from now by a current from 300 ms ago gives a power that was never drawn, and it prints like any
other number. Measured against a live host, at an instant just past the last sample:

```
dab.p_in  Unavailable  'dab.bus_voltage' has no reading at this instant; the nearest is
                        10.58s away (HeldAfter), and holding it would describe a different moment
          inputs: dab.bus_voltage = 403 V (HeldAfter), dab.input_current = 24.49 A (HeldAfter)
```

Both values were available. The naive implementation multiplies them and reports 9,869 W.

**Wiring it meant giving the expression tree a way to say "no value".** `AstNode.Evaluate` took a
resolver returning `double`, so a channel that had never reported and a channel reading zero volts
were the same answer — the defect `AlignedSample` exists to remove from the alignment path, present
again one layer up and worse here, because arithmetic hides it: a missing denominator makes a ratio
infinite and a missing numerator makes it exactly zero. Three more inventions came out with it: an
unknown function name evaluated to `0.0`, so `power(v, i)` read as zero watts forever; `min(x)` with
one argument answered `x`; and a division by zero returned infinity. All four are now refusals, and
the two that are decidable from the text — an unknown name, the wrong number of arguments — are
refused when the host starts rather than once per sample.

**Efficiency is deliberately not declared on the bundled profile.** Declared there it measured
116.1% on a live run, correctly computed from inputs that do not constrain each other: the simulator
wanders every channel independently, on purpose, so it never invents a correlation nobody put there
— and efficiency is a claim about the relationship between the two sides of a converter, which is
exactly the relationship the simulator refuses to model. Narrowing the current ranges until the
quotient looked plausible would have fixed the appearance and not the meaning. What the profile does
declare is safe on any inputs, because each name states an operation rather than a physical
relationship: `dab.p_in`, `psfb.p_out`, `psfb.conversion_ratio`. On hardware the inputs are
correlated by physics, and efficiency belongs on the command line where the operator states it about
their own rig.

**An unqualified channel name is resolved against what has actually arrived.** A profile calls a
channel `dab.bus_voltage`; the store keys it `SIM:COM3.dab.bus_voltage`, because the same quantity
from two converters has to stay two series. Both names are right and neither can be changed to match
the other. When two nodes report the same channel the request is refused and both keys are named,
because choosing one would compute a converter's efficiency from another converter's current and the
answer would look correct.

Two more defects surfaced while wiring this. `--simulate --profile does-not-exist` printed its error
and then ran with an empty timeline under exit code 0, which is indistinguishable from a rig nobody
has plugged in yet; a requested source that cannot be opened now ends the run. And `verify_dab_console.js`
was itself unreliable in two ways: it waited on the HTTP response but not on the page's own
`fetch().then(json).then(render)` chain, so it passed against a warm host and reported an empty panel
against a fresh one; and it asserted that three live voltages differ, which is a coincidence rather
than a property — the grid and DC bus ranges overlap, so two unrelated channels rounding to the same
integer failed a page that was working. Both are fixed at the cause, the second by injecting three
distinct values and checking each lands in its own card.

### A derived channel that is actually a channel
`/api/computed` answered a question; the value was query-only, so it could not be charted beside
the voltages it came from, could not be scored, and disappeared when the process ended. It now goes
through the same publisher as a measured sample — scored, broadcast, recorded, archived, and
available to the spectrum and the DVR. Verified against a live host: the derived channels appear on
the stream carrying `derived: true` and their units, `/api/spectrum` reports them at exactly
5.00 Hz, and `/api/history` returns them by name from the SQLite archive.

**The instant is chosen, not configured, and the choice sets the rate.** Evaluating at "now"
refuses everything: an input other than the one that just arrived has nothing after now, so it
could only be held. The instant used is the *oldest of the inputs' newest samples* — the latest
moment every input can be interpolated at rather than extrapolated to. That also means the instant
only advances when the slowest input advances, so a 10 Hz voltage and a 1 Hz current give a 1 Hz
power instead of ten interpolations a second of the same two current samples. An input that falls
silent stops the channel, which is the truthful outcome. Measured lag behind the live stream: about
170 ms.

**A general console showed a computed efficiency exactly like a measurement.** `stream_client.html`
discovers whatever arrives and drew every channel identically, so the confusion the whole design
exists to prevent appeared the moment derived channels started flowing. The wire frame gained a
`derived` field — absent unless true, and independent of `simulated` — and the console labels those
cards.

Three defects came out of this, all mine and all found by running it:

- **The pump published for a while and went quiet, and nothing anywhere could say why.** A single
  try around the whole tick loop, with the task awaited only at shutdown, turned any exception into
  a channel that had simply stopped arriving. It is now caught per channel, said the first time,
  and the channel abandoned rather than retried; `/api/status` carries published, withheld, faulted
  and the first fault message, because "no pump", "publishing nothing" and "threw an hour ago" have
  the same symptom and different causes.
- **The whole test suite hung.** The computed loop is a timer that never ends; the read loop over a
  finite source does. Starting the timer on the caller's token and awaiting it when the source ran
  out deadlocked every such run. It now has its own linked token, cancelled when the read loop
  ends, and a test asserts an ingest run over a finished source returns.
- **A comment that contradicted a measurement.** It claimed the tick rate was only a latency bound
  and did not affect the output rate; inputs at 9 Hz produced exactly 5.00 Hz, the tick rate. The
  published rate is the lower of the two, and the comment says so now.

### The alarm a rolling detector cannot raise
`SensorNode.Thresholds` existed, `UpdateVariable` compared against it, and `PacketFlags.AlarmExceeded`
was defined — and nothing in production ever filled the dictionary. Only tests did. So the flag was
never set in a running program, and the engineering-limit path was a complete, tested, unreachable
feature. `AlertUXService` had the same evaluation again, in the WPF project, where the headless host
cannot reach it.

A z-score asks how unusual a reading is against the channel's own recent history, so a bus that
settles above its ceiling and stays there becomes normal to it within a minute — the rolling
baseline follows the fault in. No tuning removes that; "unusual" and "unsafe" are different
questions. Measured on a live host, a channel running steadily 42–119 V above a hard limit:

```
grid.voltage, 107 consecutive frames, 342–419 V
  scored by the detector : 107        |z| never above 1.94
  flagged as anomalous   : 0
  outside the 300 V limit: 107
```

`--limit "channel[unit] in lo..hi"` and the profile's own `Limits` list now declare bands that do
not move, evaluated on the ingest path before the analytics and independent of them. The wire frame
carries `limitBreach` separately from `isAnomaly`, and the console labels a breach as a breach —
the limit wins the label when both apply, because "outside the safe band" is actionable and
"unusual lately" is a hint.

**A profile's slider range is not its alarm band, and conflating them would have been the easy
mistake.** The bundled bus slider reaches 450 V and its ceiling is 420: that gap is how an
over-voltage gets injected on purpose. One pair of numbers for both would alarm on every deliberate
test and on nothing else.

**A limit that cannot fire is the one alarm failure with no symptom at all.** So the unit is stated
in the rule and checked against what the channel reports; a rule in kV against a bus in volts is
disarmed and says so, once, loudly. `/api/limits` reports every rule with four states — `Watching`,
`Breached`, `Unarmed` (unit disagrees) and `Never` (nothing ever matched it) — and counts the last
two at the top, because that is the number an operator needs before trusting a quiet alarm list.
Verified live: eight rules, one breached, two unarmed.

Two harness defects surfaced, both of which reported a working page as broken. `verify_console.js`
stopped capturing after four distinct channels, which used to mean four raw ones and now means a
single tick of derived channels arriving in one burst — it captures a fixed window instead. And its
breach check read the card's `innerHTML`, where the label is assigned as `textContent` on a child;
that is an assertion about the shim, which is the fourth time that particular trap has been walked
into and the first time it was walked into in the direction of a false failure.

### The interlock was armed on the one signal that cannot see a steady fault
The emergency interlock is the only thing in this product that acts on the machine rather than
watching it, and `OnSampleScored` began `if (sample.ZScore is not double z) return;`. So it could
only ever fire on a rolling z-score — the signal measured, last cycle, to be blind to a channel
held 42–119 V above a hard limit for 107 consecutive samples.

`--emergency-limit "<declaration>"` declares a limit that also trips. It is a separate flag from
`--limit` because they are separate authorisations: every limit says "somebody should look", and
this one says "act on the machine". Making every band excursion a trip would be its own kind of
unsafe — a converter shut down for a two-sample overshoot is a converter whose interlock gets
disabled by the end of the week. The flag is refused without `--emergency-stop`, because a limit
that says it will act on a host that cannot is worse than no limit: it reads as protection.

Unlike the sigma path, it fires during warm-up. A reading outside a hard limit is outside it before
any baseline exists, and the machine does not wait for statistics before being damaged.

**`--serial loopback` makes this observable without hardware.** `MockSerialPort` had been written
for exactly this and was constructed by nothing. `LoopbackSerialManager` builds ports out of it and
`LoopbackTelemetrySource` sends profile frames in one side and reads them back out the other, so
the parser, the checksum and the routing rules all run on their real inputs — and anything the host
writes back is announced. This is the first time in this project that a command has been watched
leaving the host's write path. What it does **not** check is the driver, the cable and the device:
it proves the host wrote the command to the port it was told to, and nothing after that.

**Its first run found a defect immediately.** 91 identical commands in twenty seconds, from a
five-second cooldown — because the limit path handed the command straight to the queue, bypassing
the controller where the cooldown lives. The fix distinguishes a crossing from a hold: the crossing
always acts, and a breach that is merely still there re-asserts only after the cooldown, because
both halves are true at once — a machine that ignored the first command should be told again, and a
command per sample is a flood aimed at the one port that matters. Re-measured: 242 breaching
samples, 7 commands.

Two more things were wrong and are fixed. The command line refused `--profile` alongside
`--serial loopback` — a check written before a serial port could generate from a profile, which
rejected the only configuration that can exercise the interlock. And the armed banner said only
`above 3.5 sigma`, leaving an operator to read a sigma threshold and assume that was all of it; it
now names every trip limit, and says plainly when there are none that a steady excursion will not
trip this host.

### The most actionable signal was the one that never left the machine
`SlackAlertRelay.OnSampleScored` began `if (sample.IsAnomaly is not true) return;`. So the only
thing that reached a webhook was what the rolling detector flagged — and that detector does not
find a steady value unusual. A converter sitting outside its safe band told nobody, and an
unattended host that notices a fault and stays quiet has failed at the one job it was left alone
to do.

Limit events now go out too, on their own throttle key: sharing the channel's would let an ordinary
anomaly's quiet period swallow the one message that says a machine is outside what it may safely
do. The MQTT payload carries `outsideLimit` and the rules by name, absent unless true, separate
from `isAnomaly` — which answers a different question and cannot answer this one.

Verified by pointing the host at a local webhook sink and reading what it actually posted:

```
*Outside limit* SIM:COM3.grid.voltage: 384 is above the 300 ceiling (`grid.voltage[V] < 300`)
_No anomaly verdict: the detector has no baseline for this channel yet. A limit does not need one._
```

**And that run found the defect that mattered.** With a limit set inside the wander band, the host
logged four crossings and four recoveries; the webhook received one message. Crossings and
recoveries shared a throttle key, so the crossing consumed the quiet period and the recovery was
suppressed — an alert channel that says "it broke" and never "it is fine", which leaves an operator
believing a machine is still out of band hours after it recovered. That is worse than not alerting.

The rule now is that a recovery is sent only for a breach this relay actually announced, and is not
throttled: it can never be more frequent than the crossings that were sent, because it is only sent
when one is outstanding. Re-measured on the release binary: 18 host-side transitions, one crossing
and one recovery at the webhook, nothing orphaned.

Two smaller things. `ScoredSample.BreachedLimits` now carries the transition rather than a
`JustEntered` flag, because three states are acted on differently — the interlock wants the
crossing, the relay wants the crossing and the recovery, and nothing wants a message per sample —
and a bare "is outside" cannot express a recovery at all. And the limit clause moved out of
`Describe()`, where it had landed between the reading and the timestamp: *"2.62 sigma) OUTSIDE
LIMIT: grid.voltage[V] < 300 at 2026-08-21"* reads as though the limit had a time on it.

### Deleting the one that could not do what it said
`SqliteIndexRepository` had sat on the unwired baseline since the rule was written, described as a
fast lookup of "which file and offset holds a channel at a given moment, without scanning the
archives themselves". Read closely, it could not answer that. `byte_offset` was declared in its
schema and **never written**; the `archive` column it did write had **no method that reads it**. It
had no member returning a file name or an offset at all. What it actually did was keep a second,
narrower copy of every sample — `SqliteDataLogger` with the query removed.

So the choice was not "wire it or leave it". Wiring it would have added a duplicate of every row in
a table nothing can read, which is worse than leaving it alone. **Deletion is the other way an
unreachable type stops being one**, and it is only honest if the need it named is met.

The need is real: a CSV transcript cannot be queried, so "what did this channel do last Tuesday"
has no answer if all you kept was `--record`. Existing wiring already answers it — `--replay` plays
a recording through the same pipeline a live source feeds, and `--archive` is on the far end of
that pipeline. Measured end to end on the release binary:

```
CSV rows:  990
archived:  990        queryable by channel, node and time window
```

No new code, and a better answer than the index would have given: the archive holds the values
rather than pointers into files that may since have moved. That path existed and appeared in no
help text, so nobody would have found it; `--archive` now says what pairing it with `--replay` is
for. The corrupt-file property the deleted class was tested for — refuse rather than silently
recreate a database an operator believes holds history — moved onto the class that ships.

Writing the test for it found that the replay source is better than the test assumed: it does not
hand back the CSV rows it read, it **rebuilds each one as the device frame it came from**, checksum
included, so a replay runs the parser and the checksum check exactly as a live port does. The first
version of the test parsed CSV columns and failed on a checksum suffix.

### The cross-platform product could not be told anything
The streaming server has raised a `CommandReceived` event for text arriving on the WebSocket since
M2. **Nothing anywhere subscribed to it.** A command sent from a console was raised and dropped —
a control that appears to work and changes nothing, which is worse than one that is visibly absent.
`ProfileSimulatorEngine` had accepted setpoints and scenarios since M1 and only the WPF shell could
reach it, so on Linux, macOS or a browser the product was read-only: watch, query, be alerted,
change nothing.

What that costs is **commissioning**. An engineer installing this has to prove the alarm fires and
the interlock trips before trusting either, and with no way to put a channel at a chosen value the
only proof available is over-volting real hardware.

`/api/control` closes it. `GET` lists what may be moved and where each channel currently sits;
`POST` moves one. The same three commands arrive over the WebSocket in the shape a console already
sends. Verified end to end on the release binary, one HTTP command driving the whole chain:

```
POST cmd=setpoint&channel=dab.bus_voltage&value=440   applied 440 V
  /api/limits          Breached, 111 breaching samples
  interlock            3 dispatches
  loopback port        SAFE_MODE written
  Slack                "Outside limit ... 434 is above the 420 ceiling"
POST cmd=reset
  Slack                "Limit cleared ... back inside at 394 V"
```

**Offered only for a generated source, and the enforcement is that there is no object to command.**
A host reading a real device gets null, not a check that refuses. Moving that machine is a command
to the machine, which is the emergency interlock's job and is armed separately, deliberately, and
never from a browser.

**A clamped setpoint is reported as clamped.** Ask for 999 on a channel the profile bounds at 450
and the reply says `requested 999, applied 450, clamped true` with the reason. A caller told only
"Success" would believe the bus went to 999 — and on a commissioning run that belief is the
difference between "the alarm did not fire" and "the alarm was never given the chance".

One platform detail worth writing down: `HttpListener` sits on Windows' HTTP.SYS, which answers
**411 Length Required** to a POST with no `Content-Length` before this host sees the request.
Browsers send `Content-Length: 0`; `curl` needs `-d ""`. The help text says so.

The harnesses found two of their own defects. `verify_console.js` still asserted that every
displayed value is distinct — a coincidence rather than a property, already found and fixed in the
DAB harness in an earlier cycle and still present here; it failed a working page on two channels
reading 396.0 V. Replacing it with injected distinct values then broke the limit-breach check,
because those probe packets carry no `limitBreach` and repainted every card as healthy: a harness
destroying the state it was about to assert on. The probe now runs last, and the ordering is
written down where it matters.

### A picture of where the power is going, and where it is not
The console showed six numbers and no relationship between them. `power_flow.js` draws the chain —
계통 → DAB → PSFB → 서버 — with the power on each link, animated so direction and magnitude read at
a glance, and efficiency on the conversion.

The hard part is not the drawing. **A link with no data and a link carrying zero watts look
identical if you draw both as a still line**, so the picture would invent a fact for every channel
that had gone quiet. Unknown is drawn as a dashed grey outline with an open arrowhead and the words
`값 없음`, and the legend on the diagram says outright that this is not 0 W. A sample older than ten
seconds is dimmed and carries its age, because a frozen value must not look live. Derived
quantities are tagged `계산값`, and the module **does not compute a missing power from V and I
itself** — those arrive at different instants and only the host aligns them.

Efficiency above 100% is displayed as given. On the simulator that happens, because its channels
wander independently on purpose; the diagram labels it `100 % 초과 · 보정 없음` rather than clamping
it, since clamping would hide a real property of the data.

Verified in a real browser against a live host, and by `verify_power_flow.js` against a DOM stub for
the states a live host will not produce on demand: mounted with nothing, one input missing, a
sample a minute old, a breach, and an efficiency of 116.1%.

```
9.10 kW  →  효율 94.5 %  →  8.60 kW        계통→DAB 구간: 값 없음 (그 전력을 재는 채널이 없음)
```

The console feeds it the same packets the cards get rather than letting it poll: a diagram on its
own clock shows a different instant from the cards beside it, and nothing on screen would say which
one was right.

Three harness defects, all of them reporting a working page as broken. `verify_power_flow.js` first
matched the diagram's own legend — the prose explaining that 값 없음 is not 0 W — as though it were
a reading, so the module failed for documenting the property the harness existed to enforce. The
console harness stubbed `setInterval` to a no-op and then checked what the timer feeds, reporting a
console that feeds nothing when it had simply never been ticked. And it stopped capturing once the
six chain stages had arrived, which is before any computed sample does — the same early-stop
mistake found and fixed in the other harness a cycle earlier.

### A plugin that never finishes used to take the host with it
`PythonScriptEngine` ran a `.py` file with no token and no deadline, and `PythonScriptModule`
invoked its hooks the same way. So a plugin whose body is `while True: pass` hung the host at
start-up before a packet had been read, and a hook that spins hung the **ingest path**, once per
packet, stopping the console, the recording and every other channel's scoring behind it. The
JavaScript engine beside it has carried a budget since it was written.

The only cancellation machinery in the codebase sat in `PythonNetAdapter`, which nothing
constructed. It is now on the path a plugin actually takes — 10 s to import, 2 s per call, both
adjustable — and the adapter is deleted.

**Running it found that the first version of the fix did not work.** The interruption goes through
IronPython's tracing hook, and tracing in Python is per-thread — it is `sys.settrace`. The hook was
installed on the calling thread while the script ran on a worker, so it was never installed where
the script was: the caller returned on time, reported the script "did not respond to interruption",
and the loop kept burning a core for the life of the process. **A test asserting only that the call
returns within six seconds would have passed.** It was caught because the test asserts the wording —
`was interrupted` rather than `did not respond` — which is the difference between stopping a runaway
script and abandoning it while claiming otherwise.

### Deleting the last ASP.NET Core dependency
`KestrelWebServer` started a `WebApplication` serving exactly one route, `/health`, and no telemetry,
no assets and no client; `TelemetryStreamingServer` has served `/ws`, `/stream` and eleven `/api`
routes the whole time. It was also the solution's **only** ASP.NET Core consumer, and the sole
reason `Infrastructure` carried a `FrameworkReference` that flowed on to Host and UI.

Measured before and after on a published, framework-dependent build:

```
before   Microsoft.AspNetCore.App declared in Infrastructure.csproj (git show HEAD:…)
after    runtimeconfig.json requires Microsoft.NETCore.App 8.0.0 only
         published host still serves: 9 channels, 13 endpoints
```

A deployment no longer needs the ASP.NET Core runtime installed for one route nothing called.

**A third file was found testing a stub it declared itself.** `F28_KestrelWebServerTests` exercised
a `KestrelServerState` class declared at the bottom of that same test file, and
`SimulatorToKestrelToWebTests` drove a `MockKestrelWebServer` whose `Start()` sets a boolean. Neither
touched the class they were named for. That is the same pattern as `SessionReplayPlayerState` and
`FailureSnapshotExtractorHelper` before them: a substitute standing in for the thing under test can
only confirm itself.

### A known signal, so the spectrum can be checked instead of trusted
`SignalGeneratorService` had been written, tested and constructed by nothing. What its absence cost
was not a feature but a **reference**. The simulator emits exactly one shape per channel — a slow
sine around the setpoint plus noise, at a period derived from a hash — so `/api/spectrum` had never
had a ground truth to be measured against. An operator reading a peak at 0.14 Hz had no way to know
whether that was the converter oscillating or the analyser being wrong; the evidence was that the
number looked plausible.

`--signal dab.bus_voltage=sine@2:20` drives a channel with a declared waveform. Then the question
has an answer:

```
declared   2 Hz sine, ±20 V about the setpoint
measured   2.0018 Hz          bin width 0.0185 Hz  ->  0.10 of a bin
```

**Its first run found the reference itself was wrong.** The same request came back as 1.8883 Hz —
six bins out, on an endpoint whose bin width is 0.0185 Hz, so not a rounding artefact. The analyser
was right: the generator advanced its phase by the interval it *asked* for while the simulator
actually ticks at 9.479 Hz against a nominal 10, which puts a declared 2 Hz at
`2 × 9.479/10 = 1.896` in wall-clock terms. A waveform is defined against the clock the samples are
stamped with, so the phase now advances by real elapsed time. A reference that is wrong is worse
than no reference.

**A square wave gave a second, independent confirmation.** Sampled at 9.5 Hz, its third harmonic
landed at 3.011 Hz with 0.328 of the fundamental's magnitude, against a theoretical 1/3. The two
peaks that were not harmonics turned out to be predicted exactly: the fifth (5 Hz) and seventh
(7 Hz) are both past the 4.75 Hz Nyquist limit and came back folded to 4.498 Hz and 2.491 Hz, at
0.185 and 0.128 against theoretical 1/5 and 1/7.

That last part is a limitation worth stating rather than hiding: the Nyquist refusal at start-up
checks **the fundamental only**. Every shape but a sine carries harmonics and those fold too, so a
square is a useful reference for edge detection and not a clean one for a spectrum. Nothing here
can make it so, and the help text says as much.

### Injecting a waveform while you watch
`--signal` could only be set at start-up, and commissioning is something someone does while
watching: put an oscillation on a channel, see what the alarm does, take it away again.
`/api/control` gained `signal` and `signal-off`, the panel gained a shape picker and a rate box per
row, and the same commands arrive over the WebSocket.

Driven end to end from the browser: pressing 주입 on the bus row with sine/2 Hz put the channel on a
reference waveform, and `/api/spectrum` over a clean window read **1.9825 Hz — 0.24 of a bin**. The
row is marked 파형 주입 중 and the button becomes 해제, because a driven channel that looks like every
other one invites reading the chart as though the converter were doing that.

The rate box takes its `max` from the source's own sample rate, which `/api/control` now reports —
Nyquist belongs on the control that sets the rate, not in a message afterwards. Above it the request
is refused outright.

**A measurement that looked wrong turned out to be right, and worth explaining.** Straight after
injecting, the spectrum reported 0.11 Hz rather than 2 Hz. The channel was verifiably correct — raw
samples swinging 380–420 about 400 — so the window was the suspect, and measuring three widths
settled it:

```
window 10 s (entirely post-injection)   1.9985 Hz    0.02 bins
window 20 s                             1.9951 Hz    0.13 bins
window 45 s (reaches back before it)    1.9760 Hz    1.31 bins
```

The step between the drift and the injected wave dominates the low end of any window containing it.
The endpoint is right in all three; a caller who does not know that reads the third as a broken
analyser. The `signal` reply now says so, with those numbers.

### The cross-platform claim, finally measured
Every document in this repository has called the headless host "the cross-platform product". It had
never been run anywhere but Windows. That was the largest unverified claim here, and it has now been
tested rather than asserted.

Published `linux-musl-x64`, self-contained, and run in a musl x86_64 environment:

```
uname                Linux x86_64
seriesChannels       27          seriesSamplesAccepted   2693
computed             3 declared, 291 published, 0 faulted
limits               6 declared, 1 breached
spectrum             computing, 10.12 Hz measured sample rate
endpoints            all 13 advertised and answering
```

**What that does and does not establish.** It is a real Linux kernel running a real build of this
product serving real telemetry over HTTP, so the headless path contains nothing Windows-only that
matters. It is *musl* (Alpine), inside Docker Desktop's own utility VM — not mainstream glibc Linux,
and not macOS. Neither of those has been run, and this does not stand in for them.

Two things had to be true before it would start, and both were worth learning:

- `libstdc++` and `libgcc` are prerequisites .NET does not carry on musl. The first run failed with
  a page of unresolved C++ symbols.
- **It aborted on ICU** — `Couldn't find a valid ICU package installed on the system` — before
  reaching `Main`. A minimal container is exactly that environment, so a plant host that needs a
  locale package installed before it starts is a deployment step nobody remembers.

The second is now fixed rather than worked around. The headless host publishes with
`InvariantGlobalization`, which is honest for this project rather than a shortcut: every number,
timestamp and comparison on that path is already invariant or ordinal by construction — the wire
format, the CSV, the SQLite schema and the JSON all depend on it — so locale data changes nothing
the host does and only decides whether it starts. Re-verified with ICU **uninstalled**: 27 channels,
2,693 samples, every endpoint answering. The desktop shell is deliberately untouched; it shows text
to a person and should sort the way their machine does.

Two executable rules were added alongside, because what can be checked from Windows should be:

- **The portable backbone calls no Windows-only API.** The existing rule checked target frameworks,
  which is necessary and not sufficient — a project can target plain `net8.0`, call a registry read
  or a P/Invoke, and get a CA1416 *warning* while the build and the suite stay green. The new rule
  scans for P/Invokes, WMI, the registry, WPF and `[SupportedOSPlatform("windows")]`. Verified to
  actually catch something: a probe `[DllImport("kernel32.dll")]` dropped into Core failed it by
  name, and was removed again. `Win32Native` is deliberately not flagged — it is constants and
  struct layouts with no imported entry point, so it loads anywhere and simply never receives a
  message off Windows.
- **The host asks for no locale data**, so the ICU finding cannot be quietly reverted.

**Suite: 1,167 passing, 0 failing** (1,099 portable + 68 desktop) — about 2 m 35 s with
`--filter "Category!=Benchmark"`, longer for everything (the million-row storage benchmark alone is
seven minutes). The intermittent
failures were all one story: heavy benchmarks running in parallel with timing-sensitive tests. Two
tests were fixed at the cause rather than loosened — the JavaScript load timeout above, and
`Win32HotPlugHook_RapidMessages_DebouncesToSingleEvent`, which slept between messages so a stalled
machine pushed its own burst outside the 200 ms window it was testing. It now sends the burst with
no gap and checks that premise before asserting on it.

### The UPS branch, and a diagram shaped like the machine

The profile was called `dab-psfb-ups`, offered a button labelled 정전 (UPS 방전), and had **no
battery in it**. Pressing that button took the mains to 0 V and left every other channel unchanged,
so the one question an outage raises — how long have we got — had no answer on any screen. The
codebase did contain a battery model, in `PowerPlantSimulator`: `DabStateOfCharge = 94.5 - t*0.0005`,
a ramp in wall-clock time that ran at the same rate whether the bank was charging at +12 A or
discharging at −32 A, and `DabBatteryVoltage` computed on the next line from neither. That class is
constructed only by the WPF shell; the headless host has never been able to produce any of it.

Four channels now exist and are measured end to end: `ups.battery_voltage`, `ups.battery_current`
(signed, positive into the bank), `ups.bus_current` (the converter's other winding, positive into
the bus) and `ups.state_of_charge`. Two derived: `ups.p_batt` and `ups.p_bus`, one per side of the
converter — deliberately not one figure drawn twice, because the two differ by the loss.

**State of charge is a coulomb count, not a channel that wanders.** `ProfileChannel.Integrates`
declares it as the running total of another channel, which is the single exception to the engine's
rule that channels are independent — and it is an identity rather than a correlation: charge *is*
the integral of current, which is how a real coulomb counter measures it. Declared as an ordinary
channel it would have drifted at 8 % of its range, rising while the bank drained, and looked exactly
like every other reading on the screen.

Measured on a live host over a 120 s outage: mean current −179.92 A against the −180 setpoint, and
the charge the host reported fell **3.000 %** against the **2.997 %** obtained by integrating the
current samples the same host reported. The 0.003 % difference is well inside the 0.1 % the frame
itself rounds to. The clamp holds — commanded to 0.5 % and left discharging, it reached 0 and
stopped — and the current keeps being reported at −201 A afterwards, because nothing here models a
bank that disconnects itself. That limitation is written down rather than hidden.

The low-charge rule `ups.state_of_charge[%] > 20` fired on the way past: 745 breaching samples,
`0 is below the 20 floor`.

#### Two scenarios that reported success and did nothing

`ProfileScenario.Fault` is read in exactly one place in the repository — the WPF shell — and the
headless host never looks at it. `dab-overcurrent` and `psfb-undervoltage` carried a `Fault` and no
setpoints, so `POST /api/control?cmd=scenario` resolved them, looped over zero setpoints and
answered **Success**. Correct about every step it took, and wrong about what happened; the operator's
evidence for either outcome is a chart that did not change.

Both now declare setpoints chosen to cross the profile's own limits, and measured on a live host
they do: `dab.input_current` 22.02 → 36.51 A against a 36 A ceiling (37 breaching samples),
`psfb.output_voltage` 48.89 → 40.68 V against a 45 V floor (55 breaching samples). `ControlEndpoint`
now answers **Error** for any scenario that declares nothing to apply, and says whether it named a
fault only the desktop shell applies.

#### The diagram is a T

`power_flow.js` is two rows now: the top bar is the normal feed, and a stem drops from the DC bus to
the battery branch wired in parallel with it — the standard one-line drawing of an online
double-conversion UPS. **The stem's arrow points up into the chain whenever the UPS is holding the
bus up**, which is the whole reason for drawing it this way: during an outage the picture shows
power entering the top bar from below. Verified in a browser against a live host mid-outage —
`ups.p_bus` 7.78 kW, arrow up, labelled 버스 지원; `ups.p_batt` −8.50 kW, arrow left toward the
converter, labelled 방전.

The segment between the bus and the converter carries its own channel rather than borrowing
`ups.p_batt`, for the same reason the DAB→PSFB segment is still blank: drawing one power on both
sides of a converter asserts a loss of zero that nobody measured.

Three defects were found by measuring the rendered page rather than looking at it, and none of them
could have been caught by the DOM stub: the direction label overlapped the power figure by 4 px
(font metrics the stub does not have), the last legend line was positioned outside the viewBox
entirely (SVG clips it and reports nothing), and the 계산값 pill had moved on top of the flow line.
The height is now derived from the legend's own line count, and the harness asserts that nothing is
positioned outside the declared box.

A fourth was found by the harness failing three of its own new checks: they matched the legend prose
and a row tooltip as though those were readings — the module documenting itself being graded instead
of the drawing. The checks now read one link group at a time.

#### `power_flow.js` was never shipped

The host's csproj globbed `..\..\*.html` and named `telemetry-client.js` — one file, by hand, next
to a wildcard. So `dab_psfb_console.html` reached the output directory and `power_flow.js` did not,
and a host started from its own `bin` served the console and answered 404 for the diagram, drawing
the page's "could not load" fallback where the picture belongs. Invisible to both harnesses: one
reads the file straight off disk, the other stubs `PowerFlow` and never loads it. The include is now
a wildcard too, excluding `verify_*.js`.

Also fixed while here: `PythonExecutionBudgetTests` was marked `[Collection("HeavyTests")]`, a string
matching no `CollectionDefinition`. xUnit answers an unknown collection name by inventing an ad-hoc
collection, so that class serialised against nothing and kept running beside the tests it was meant
to take turns with — while its own remarks describe the cost of exactly that.

**Suite: 1,182 passing, 0 failing** (1,114 portable + 68 desktop).

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
- **No browser asset reads a telemetry field the hub does not send.** The allowed set is parsed out
  of `TelemetryFrame.cs`, so adding a field to the wire lets the pages use it and removing one fails
  the test rather than silently blanking a display. Written after the same defect was found four
  times in one sweep, in four files written at different times — it never fails loudly, because a
  page reading an absent field renders a placeholder, which is indistinguishable from a hub that has
  sent nothing yet. Its first run found a fifth: `telemetry-client.js` still fell back to
  `packet.device`, a field retired long ago and kept alive by an `||` chain that never complains
  about a branch it does not take.

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
