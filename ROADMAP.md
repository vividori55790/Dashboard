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

The second constraint is as important as the first: **use the conventions the rest of the industry
already has.** A telemetry hub that invents its own metric naming, its own unit vocabulary and its
own dashboard format is a hub nobody can connect anything to.

## What has shipped, and where it went

The first three workstreams are done and their rows are in ARCHITECTURE.md. What they took from
prior art is recorded beside the code it governs, and the three findings worth remembering are:

- **Sparkplug B does not define `engUnit`.** The keys everyone associates with it are an Ignition
  convention inside a free-text `PropertySet`; the spec names exactly one well-known property,
  `Quality`. So Sparkplug answers *where a unit travels* and says nothing about quantity kind. That
  gap is what the taxonomy fills, and it is why a declared unit here is a string.
- **The metrics endpoint had to be structurally unable to export a zero for an unmeasured value.**
  A family writes its `HELP`/`TYPE` from its first sample or never, so a collaborator holding
  nothing contributes zero bytes — one place instead of twenty call sites.
- **Two correct halves made a broken whole.** The exporter asked for
  `telemetry_channel_value{node=…, channel=…}` and the endpoint emitted one glued `channel` label.
  Both sides' tests passed. Every panel imported cleanly and drew nothing.
  `GrafanaScrapeContractTests` now asserts them against each other, which is the only place the
  disagreement exists.
- **Verifying against a parser is not verifying.** Both endpoints shipped reading *Half built* for
  the same reason, and a CI job now stands up `prom/prometheus` and `grafana/grafana` against a
  published host. The first run scraped it, answered every generated query from stored series, and
  stored the dashboard under uid `telemetry-hub-auto` with 5 panels, read back to prove the import
  was not a 200 over nothing.

## W5 — Let an operator accept a proposal

**Problem.** The taxonomy proposes a classification for a channel it cannot derive one for, and
there is nowhere for the operator's answer to go. The engineering half is built and the
`fieldN` case the goal names produces no proposals at all by design — there is nothing to accept
for a channel with no unit and no recognisable name.

**Decide first, then build.** Where an accepted proposal is written is a product decision and is
recorded rather than guessed at: the operator's **routing rules file** makes the decision
reviewable and diffable alongside the rest of the rig config, but edits a file they may
hand-maintain; a **separate overrides store** leaves their file alone but creates a second place a
channel's unit can come from.

## Not in these, and not forgotten

- **Peer exchange and local buffering.** The last *Not started* row. The exchange is pull, so a
  returning receiver must ask for the interval it lost — a query the sender can already answer from
  its archive, verified: an archiving host returns 448 samples for a 12-second window and a
  non-archiving one answers `Error` with a reason rather than an empty result.
- **Confidentiality.** `--listen network` puts a password on the wire in the clear. `HttpListener`
  has no HTTPS off Windows, so the honest options are a different HTTP stack or leaving TLS to the
  proxy it already works with.
- **The peer's verdicts, kept attributed to the peer.** §7 asks for it; they are dropped instead.
- **Desktop parity.** The WPF shell has neither the input inventory nor the fleet view.
