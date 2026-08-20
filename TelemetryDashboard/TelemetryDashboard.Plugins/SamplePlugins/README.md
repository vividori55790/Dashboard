# Sample Telemetry Plugin — a working extension

This is a complete, installable extension for the headless host. It is not a stub: it computes a
derived channel from the telemetry the host is actually routing and writes it to the host's data
logger.

## What it does

For every routed packet it keeps the previous sample of that node/variable and emits

```
<variable>.rate  =  (v2 - v1) / (t2 - t1)      in <unit>/second
```

as a derived packet (`PacketFlags.IsDerived`), buffered and written to the host `IDataLogger` in
batches of 200.

Two things it deliberately does **not** do:

- The first sample of a channel produces nothing. One point has no rate, and emitting `0` there
  would be a fabricated reading.
- A non-advancing or reordered timestamp is skipped rather than clamped, for the same reason.

On shutdown it logs how many samples it derived and how many the logger accepted — two different
numbers if a write failed, and it says so rather than reporting the flattering one.

## The package

An extension package is a directory holding an entry assembly and an `extension.json` beside it:

```
sample-extension/
  TelemetryDashboard.Plugins.dll
  extension.json
```

`extension.json` must carry an `id` and an `entryAssembly`; `sha256` is optional for a local
package and required in practice for a catalogue entry, since it is what the install verifies the
payload against.

The build stages this package into `sample-extension/` beside the host executable.

## Install it

From the host's output directory:

```
TelemetryDashboard.Host extensions install ./sample-extension
TelemetryDashboard.Host extensions list
```

Install verifies before it copies anything: the manifest must parse and name an entry assembly, the
assembly's SHA-256 must match the hash the catalogue published (when there is one), and the
assembly must load and export at least one `IPlugin`. A package failing any of those is refused
with the reason, and the store is left untouched.

From a catalogue index on disk or a network share:

```
TelemetryDashboard.Host extensions install --catalogue ./catalogue.json sample.plugin
```

The index is a JSON array of manifests. An `http(s)` index can be *listed* (`--extensions <url>`)
but not installed from: the hash vouching for a payload would come from the same server that served
the payload.

## Turn it off, on, and remove it

```
TelemetryDashboard.Host extensions disable sample.plugin
TelemetryDashboard.Host extensions enable  sample.plugin
TelemetryDashboard.Host extensions remove  sample.plugin
```

Disabling keeps the files and is recorded in `extensions/installed.json`, so it survives a restart.

Removing deletes the directory. A host that is already running does not block it — plugins are
loaded from a byte copy into a collectible `AssemblyLoadContext`, so the DLL on disk is never held
open — but that host keeps executing the copy it loaded until it exits. **Removal changes the next
start, not the current one.** If the delete does fail for an ordinary reason (an editor, a scanner,
a permission), the command says which file survived instead of reporting a success.

## See it run

```
TelemetryDashboard.Host --simulate
```

The start-up banner prints an `extensions` block naming every installed extension, its version and
its state — including any that failed to load and why. The plugin's own lines are tagged
`[plugin:sample.plugin]`.

## Note on the two load paths

`plugins/` is the unmanaged drop folder: every `*.dll` in it is loaded at start-up, with no
verification, no id and no way to switch one off. It still works and nothing was taken away from it.

The extension store is the managed path, and it is where this sample ships — as a package in
`sample-extension/` that you install, not as a DLL pre-placed in `plugins/` that runs on first
launch. Earlier builds staged it into `plugins/`, which meant a fresh host executed plugin code
nobody had asked it to run. Installing is now an act someone performs.

If you do put a copy in `plugins/` as well, both copies load and you will see the plugin's log lines
twice; the host does not de-duplicate across the two paths.
