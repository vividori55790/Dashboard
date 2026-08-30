# What this is aimed at next

**Nothing in this file is built.** It is a plan, and it is written separately from
[ARCHITECTURE.md](ARCHITECTURE.md) for that reason — that document's table records what exists and
is maintained by hand precisely so it cannot drift into a wish list. When something here ships, it
moves there and is deleted from here.

`AGENT_BLUEPRINT.md` is the counter-example and is left in place as one: five unchecked tasks dated
2026-08-10, every one of them long since built (`ScriptPluginSandbox`, `GorillaCompressor`,
`DashboardExporter`, the protocol bridge, the security provider). A checklist nobody reads back is
worse than none, because it is read as a statement of what is missing.

## The goal

**An operator should be able to see exactly what is arriving, know what each channel actually is,
and wire it into the dashboards they already run — in minutes, without hand-writing a config.**

Three things are missing for that, and they are the three workstreams below.

The second constraint is as important as the first: **use the conventions the rest of the industry
already has.** A telemetry hub that invents its own metric naming, its own unit vocabulary and its
own dashboard format is a hub nobody can connect anything to. Each workstream begins by reading the
relevant specification and prior art, and records what it took from where.

## Where the product is today

Ingest is in good shape and honest about itself. A sample carries where it came from, whether it
was measured or generated, whether it was computed, how old it was when it arrived and whether that
was knowable; a peer's stream keeps its channel identities and units; a replayed link cannot inflate
a total; the clock offset between two nodes is estimated with an error bar that is published as a
lower bound. `/api/inputs` lists what every port is delivering.

What an operator gets from all of that is a list of names. `dab.bus_voltage` arrives with a unit
because a rule file said so; `field1` arrives because nobody has written one yet. Nothing says what
kind of quantity a channel is, nothing groups a rig's channels into the subsystems they belong to,
and nothing exports any of it anywhere. The browser console is the only view, and it is the only
view because there is no way to feed anything else.

## W1 — Say what each channel actually is

**Problem.** Classification today is binary: either a rule file declared a channel, or it did not
and the parser named it positionally. There is no notion of *what kind of thing* a reading is, so
nothing downstream can pick a unit, an axis, a scale or a colour without being told each time.

**Build.** A channel taxonomy: quantity kind (voltage, current, temperature, rate, ratio,
dimensionless…), unit, and the subsystem it belongs to, derived from the channel name, the declared
unit and the values themselves.

**The rule that governs it.** It must answer **unclassified** rather than guess. A channel
confidently labelled a temperature because its name contains `t` and its values sit near 20 is the
same defect as a confident zero — worse, because it will pick the axis and the alarm band. Every
classification carries how it was reached, and a low-confidence one is presented as a *proposal for
the operator to accept*, never as a fact.

**Read first.** Prometheus metric and unit naming conventions; OpenMetrics units; UCUM; Sparkplug B
metric metadata (the industrial-telemetry answer to exactly this question); OPC-UA
`EngineeringUnits`. Take the vocabulary from whichever of these the industry actually uses rather
than inventing one.

**Done when.** `/api/inputs` carries a classification for every channel; a rig of unnamed `fieldN`
channels produces proposals an operator can accept in one action; and a planted mislabel is caught
by a rule.

## W2 — Let anything scrape this hub

**Problem.** There is no way to get data out of this product into a system that already exists. MQTT
relay and Slack alerts exist; a metrics endpoint does not. Every monitoring stack in use — Grafana,
Prometheus, VictoriaMetrics, Datadog agents, Telegraf — can read the Prometheus exposition format,
and none of them can read anything this hub currently serves.

**Build.** `/metrics`, in Prometheus text exposition format, from the live channel set.

**The rule that governs it.** A channel with no verdict, no baseline or no sample must be **absent
from the output**, not exported as zero. This is the single most important detail of the whole
workstream: Prometheus fills gaps by interpolation and alerting rules fire on values, so a zero
exported for "not measured" becomes a confident reading inside somebody else's alert. The same rule
this codebase already applies to its own analytics applies at the boundary, and it is easier to get
wrong here because the format has no null.

**Read first.** The Prometheus exposition format and OpenMetrics specifications; the naming and base
units conventions; how established exporters (`node_exporter`) handle absent series and staleness.

**Done when.** A real Prometheus scrapes this host and the series that appear are exactly the
channels that have data; an unmeasured channel is absent rather than zero, verified by planting one.

## W3 — Generate the dashboard rather than asking for one

**Problem.** Even with `/metrics`, connecting this to Grafana means a human building panels by hand
for channels the hub already knows about. "쉽게 설정할 수 있게" is the requirement, and a scrape
endpoint alone does not meet it.

**Build.** A generated Grafana dashboard: `/api/export/grafana` returns dashboard JSON built from
the channels this host actually has, grouped by the W1 taxonomy, with units and axes already
correct. Plus a console panel that hands the operator the exact scrape config and the dashboard
file, with nothing to type.

**The rule that governs it.** The generated dashboard must not draw panels for channels that have
never reported. An auto-generated dashboard full of empty graphs is how an operator learns to
distrust the generator, and an empty graph is again indistinguishable from a quiet one.

**Read first.** The Grafana dashboard JSON schema and provisioning format; how projects that already
generate dashboards structure theirs; what a Prometheus datasource panel needs at minimum.

**Done when.** A generated dashboard imports into a real Grafana without editing, its panels carry
the right units, and it contains no panel for a channel that has never reported.

## Not in these three, and not forgotten

- **Peer exchange and local buffering.** The last "Not started" row in ARCHITECTURE.md. The exchange
  is pull, so a returning receiver must ask for the interval it lost — a query the sender can
  already answer from its archive.
- **Confidentiality.** `--listen network` puts a password on the wire in the clear. `HttpListener`
  has no HTTPS off Windows, so the honest options are a different HTTP stack or leaving TLS to the
  proxy it already works with. A product decision, recorded rather than guessed at.
- **The peer's verdicts, kept attributed to the peer.** §7 asks for it; they are dropped instead.
- **Desktop parity.** The WPF shell has neither the input inventory nor the fleet view.
