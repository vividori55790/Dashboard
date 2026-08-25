# How this system behaves when it runs everywhere

This document is about what changes when the same program runs on a thousand machines and their
data is exchanged, rather than on one machine with a cable in it. The number is an illustration; the
design questions are the point, and they are not primarily about throughput.

## The rule this project is built on, restated for many machines

One machine: **never assert what was not measured.** A verdict the analytics engine has not reached
is carried as null rather than as a confident zero, because "0.00 sigma, normal" is a claim of
normality nobody established.

Many machines: **never present a partial view as a complete one.** A dashboard drawing data from 998
of 1000 nodes, without saying that two are missing, is the same lie in a more dangerous form. The
chart looks healthy. Nothing is flagged. The two silent nodes are precisely the ones worth looking
at, and the interface has hidden them by omission.

Everything below follows from taking that seriously.

## 1. Silence must be distinguishable from health

A node that stops reporting produces no data. A node whose sensors read nominal also produces
little that draws attention. On one machine these look different because the operator can see the
connection state. Across a thousand they look identical, and the failure mode is that an outage
renders as calm.

So the system tracks, separately from the data itself, **who was expected and who was heard from**.
Every aggregate answer carries a coverage statement: which nodes contributed, which were expected
and silent, and how stale the newest sample from each is. A view that cannot say this is not
allowed to claim completeness.

This is implemented as a coverage ledger consulted on every query, not as a status page an operator
has to remember to check. A number that arrives without knowing what it is missing is not a
measurement, it is an impression.

## 2. Identity must be globally unambiguous

`MCU_NODE_1.TEMP` is a perfectly good channel name on one machine and a collision waiting to happen
on a thousand. Two hosts each reading a device called `MCU_NODE_1` on a port called `COM3` publish
the same name for two different physical sensors. Merge those and you get a channel whose values
alternate between two machines — a series that looks like noisy data and is actually two datasets
interleaved. Nothing in the numbers reveals it.

Identity is therefore three parts, not one: **which host observed it**, **which device it came
from**, and **which quantity it is**. The host part is a stable identifier generated once per
installation and persisted, not the machine's hostname — hostnames are reassigned, duplicated in
images, and changed by administrators, and an identifier that quietly changes is worse than none.

## 3. Time is not shared, and pretending otherwise corrupts ordering

A thousand machines have a thousand clocks. Merging their samples onto one timeline implies a
precision nobody has: two events a millisecond apart on different nodes cannot be ordered at all
unless the clock offset between those nodes is known and smaller than that.

So a sample carries the observing node's clock, and each node separately reports its offset and
uncertainty against a reference. A merged view either shows data at a resolution coarser than the
worst uncertainty, or it shows the uncertainty. It never silently interleaves.

This is what the existing `TimeSyncJitterBuffer` is for, and the state of it is worth stating
carefully, because this document got it wrong once.

**What runs.** `AlignedEndpoint` constructs the buffer and `/api/aligned` answers from it, so several
channels can be read as they stood at one instant, each answer labelled exact, interpolated or
held. That is real and it is useful — it is the question behind every efficiency and every phase
relationship. It is also *intra-host*: those channels come from one series store and share one
clock, so no offset is involved in it at all.

**What now runs, and what it measured.** The estimator has a caller. A sample that crossed a
network carries the sending node's own clock in `ObservedAt`, and `IngestPublisher` pairs it with
this host's reading at arrival — the pair *is* the observation. Before this, `SyncNodeClock` had no
caller outside a test, so every offset it could have reported was zero. This document said
`/api/aligned` answering from the buffer meant "the half that estimates a per-node offset runs",
which conflated two things: the buffer ran, that half of it did not.

It could not have run any earlier. A remote sample used to be stamped `DateTime.UtcNow` at receipt
— `RawPayloadParser` set `Timestamp = raw.Timestamp` — while the sending node's timestamp travelled
on the wire and was discarded. Nothing downstream can estimate an offset between two clocks when
only one of them survives ingest, and that was also §4's unmarked backfill: a sample four hours
late and one that arrived instantly were stamped identically, for the same reason.

Measured on a pair of hosts on one machine — one on `--simulate`, the second reading its `/stream` —
where the true offset is known to be exactly zero because both read the same clock. The estimate
came back **+0.57 ms with a spread of 6.8 ms over 64 observations**. Both halves are what the
physics requires: the offset is *above* the truth, because every observation is `offset + transit`
and loopback transit cannot be negative; and the error bar covers the truth with room to spare.
A host with nothing crossing a network into it reports `nodes: 0` rather than an offset of zero.
**What is now built.** The uncertainty. `GetClockOffset` returns a `ClockOffsetEstimate` rather than a
bare double, and it separates three things that used to be one answer: never measured, measured
once with no error bar, and measured with a spread. The point estimate is the *minimum* of the
observations rather than an average, because each is `offset + transit` for a transit that cannot
be negative, so the smallest is the least overstated and an average is worse by the mean transit.
`CanOrder(separation)` is the question this section exists to make askable, and it answers false
for an unmeasured or single-sample offset rather than defaulting to true.

The spread is honest about being a floor rather than the whole answer: one-way messages never
separate the offset from the transit, so even the fastest message overstates by an amount nothing
here can observe without a round trip. Presenting that floor as a ceiling would be this section's
own error, one level in.

## 4. A node must survive alone, and say so when it was

A network partition is normal, not exceptional. A node that stops working when it cannot reach a
peer has moved the fragility of the link into the plant floor.

So each node records, detects and buffers locally, and backfills when the link returns. The
important part is the second half: **backfilled data is marked as late-arriving.** A sample that
took four hours to arrive and one that arrived instantly are different facts, and an alert
threshold crossed four hours ago that only surfaces now must not be presented as current.

Exchange must also be idempotent. A reconnect that replays a buffer must not double-count, so
samples carry a per-node sequence and the receiver deduplicates. Otherwise a flaky link inflates
every total, and the totals are what the operator trusts.

## 5. Aggregation happens where the data is

The display cannot show a million series and should not try. A chart a thousand pixels wide can
carry about two thousand points; sending more is waste on the way to being discarded, and the
client does the reduction worse than the server would.

So the query names what the screen can draw, and reduction happens before transmission. The
reduction must preserve extremes — plain decimation drops the spike the operator is looking for,
and a chart that omits an excursion is lying by omission. Min/max per bucket cannot lose one.

And every reduced series states that it is reduced, with its bucket width and the true sample count
behind it, so nobody mistakes an hourly mean for a reading.

## 6. Storage is tiered because retention and resolution are different questions

Full-resolution data is worth keeping for a while and not forever. Rollups — count, min, max, sum,
and enough to derive deviation — are worth keeping far longer and cost a fraction.

Two rules make this honest. A rollup over a window with no measurements must be **absent, not
zero**, or gaps become readings. And discarding raw data is destructive, so it is explicit,
configurable, logged with what was removed, and never what happens by default on first run.

## 7. What is exchanged is data, and it is not trusted

If instances exchange data, then one instance's output is another's input. Input from the network
is not more trustworthy than input from a serial cable, which this codebase already refuses to
trust: a frame that fails its checksum is dropped rather than scraped for numbers.

The same applies upward. A peer's samples carry that peer's identity, are attributable, and are
never merged in a way that erases where they came from. Provenance is already a first-class part of
the record type — `Source`, `DerivedFrom`, `RawSource` — and this is the reason it exists. A number
whose origin cannot be recovered cannot be disputed, and on a plant floor the disputed number is
the one that matters.

**This section was written before anything was measured against it, and what it describes was not
happening.** Two hosts were run as a pair — one on `--simulate`, the second started with
`--sse http://127.0.0.1:PORT/stream` — and the receiving host was asked what it had learned. It had:

- one channel called `value`, holding every channel the sender had, its points alternating between
  vibration in g and a figure near 1000 rpm. That is §2's interleaving, and §2 is right that nothing
  in the numbers reveals it;
- `anomalyScore` with 1,292 samples and `predicted` with 783 — the sender's *verdicts*, ingested as
  measurements and then scored again, so the receiver published an anomaly score of an anomaly score;
- every unit dropped: `°C`, `%` and `g` all arrived empty;
- a channel named `port` holding 8074, read out of the stream's opening connection event;
- and `simulated: false` on everything it republished, while the sender had marked every frame
  `true`. Synthetic data laundered into measured data in one hop.

The cause was that nothing recognised this product's own frames, so they reached the last-resort
parser, whose contract is one channel per numeric property of an object nobody has a rule for —
exactly wrong for a frame that names its channel in one field and its reading in another.
`PeerFrameParser` now reads them by deserialising into the same `TelemetryFrame` the outbound path
builds, so the reader and the writer cannot drift; the origin mark and the sending node's clock
travel on `DataRecord` rather than dying at the projection; and a source may now say that its
samples decide their own origin, which is the honest answer for a transport that does not know what
it is carrying.

What is still owed here is the part this section asks for by name. The peer's verdicts are dropped
rather than kept attributed to it — dropped because a score is a claim about a baseline that did not
travel and limits this host was never configured with, and adopting either would let a peer's
configuration decide what this host considers alarming. Keeping them *as the peer's* is the thing
§7 actually wants, and it is not built.

## A worked example of the rule catching the system out

The engine published a 60-second forecast for every channel it had scored. A live feed made the
result absurd — a Wikipedia page-size channel predicted at minus 228,000 bytes — and the first fix
was to require that the fitted line actually explain the data. That withheld 92% of the forecasts
the dashboard had been stating as fact, which was already a large correction.

The second fix came from asking where a forecast may land: within one width of the range the channel
has occupied, a bound the channel's own history sets rather than a constant anybody chose. A
volatile channel earns a wide allowance and a steady one a narrow allowance, which is the right way
round — predicting a large move for a quantity that has never moved is exactly the claim needing
evidence.

Then the arithmetic said something worse. The default window holds 50 samples at 20 Hz: **two and a
half seconds**. The published field is called `Predicted60s`. Continuing a slope twenty-four times
further than it was observed is not a prediction, and a perfectly clean ramp is now refused for the
same reason noise is. The number was never supportable; the noisy channels merely made it visible by
coming out negative.

That is recorded as a finding rather than patched over, because the fix is a product decision: a
window long enough to justify the horizon, or a horizon scaled to the window. Until one is chosen,
the engine says nothing rather than saying something unfounded.

One thing it does still say. Time-to-threshold is gated on the trend existing, not on the range
bound, because "the threshold is 27 seconds away" is a bounded question the data can answer while
"the value will be 63 in sixty seconds" is not. Withholding both would have removed the more useful
of the two in precisely the situation where a channel is running away.

## What is built, and what is not

This document describes a target. Claiming it is implemented would be the same category of error it
argues against, so:

The "Where" column exists so a row can be checked rather than believed. A state nobody can look up
is the same kind of claim this document argues against.

| Area | State | Where |
|---|---|---|
| Provenance on every record (`Source`, `DerivedFrom`, `RawSource`) | Built | `Core/Records` |
| Unparsed input counted and reported rather than dropped | Built | `Host/Startup/IngestReport.cs` |
| Warm-up carried as "no verdict" rather than zero | Built | `Core/Analytics` |
| Synthetic data marked everywhere it travels | Built | `simulated=true`, `SIM:` node prefix, `DataRecord.Synthetic`. This read Built while being false across a network hop: a host relaying a peer's simulator output republished it as measured, because the mark had nowhere to sit on the record and the receiving source answered for its peer. Measured on a live pair, then fixed |
| Per-channel rate guard with counted, announced drops | Built | `Core/Resilience` |
| Coverage ledger — who was expected, who was heard | Built | `Core/Cluster/CoverageLedger*`, `Host/Startup/CoverageSetup.cs`, `--expect`, `coverage` on `/api/status` |
| Stable per-installation node identity | Built | `Core/Cluster/NodeIdentity.cs`, `Host/Startup/HostNode.cs`; persisted, and the banner says when it could not be |
| Display-path reduction that preserves extremes | Built | `/api/series?reduction=minmax`, `reducedFramesSent` / `reducedPointsSent` on `/api/status` |
| Tiered storage and rollups | Built | `Core/Storage/TieredTelemetryStore`, swept on a clock by `Host/Startup/RetentionSweep.cs`, opt-in via `--retain` |
| Bounded per-channel state with reported cardinality | Built | `Core/Resilience/BoundedChannelRegistry` — `Capacity`, `Count`, `Evictions`, and an eviction record |
| Clock offset across nodes | Built | `TimeSyncJitterBuffer.SyncNodeClock`, fed by `IngestPublisher` from the sending node's own clock, reported per node on `/api/status` under `clocks`. Empty rather than zero on a host nothing reaches over a network |
| **Uncertainty on that offset** | Built | `Core/Models/ClockOffsetEstimate` — never-measured, measured-once-with-no-error-bar and measured-with-a-spread are three different answers, and `CanOrder` refuses on the first two. The spread is published as a floor: one-way messages never separate transit from the offset, so `uncertaintyIsALowerBound` travels beside it |
| Peer exchange, sequencing, deduplication, backfill marking | Not started | no `LateArriving` / `Backfill` anywhere in the tree |
| Authentication between instances | Half built | `--credential` gates every path -- page, endpoints, SSE and the WebSocket upgrade -- against a salted PBKDF2 credential, and `telemetry-host credential` enrols one without a desktop. `--listen network` binds every interface and cannot be asked for without it: `CommandLineParser` refuses the pair before anything binds and `TelemetryStreamingServer.Start` refuses it again at the socket, so the open-and-unlocked state has no construction path. What is missing is confidentiality -- Basic over cleartext puts the password on the wire, which makes this a bench-LAN answer and not a plant-network one. That is said at every launch by the banner and reported as `reachability.encrypted: false` on `/api/status`, rather than left to be inferred from `authenticated: true` |

The right-hand columns are maintained by hand and are expected to be embarrassing. That is
preferable to a roadmap that reads as an inventory.

Five rows moved from "In progress" to "Built" in one pass, and none of them moved because anything
was written that day — they had been finished for a while and the table had not been read since.
That is the same defect as a stale claim, pointing the other way: a document that undersells is
still a document nobody can plan from. Worth noting because the reflex is to police only the
optimistic direction.

The row that split is the interesting one. "Clock offset and uncertainty across nodes" was one line
covering two things, and the buffer having shipped the offset made it tempting to tick the whole
row. §3's argument is entirely about the second half — an offset places a sample, an uncertainty is
what says whether two samples can be ordered at all — so the row is now two, and the half that
matters reads Not started.
