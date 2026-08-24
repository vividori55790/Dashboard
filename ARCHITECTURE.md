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

This is what the existing `TimeSyncJitterBuffer` is for. It sat in the codebase unreferenced for a
long time, which meant the problem was understood and then not addressed; `AlignedEndpoint` now
constructs it and `/api/aligned` answers from it, so the half that estimates a per-node offset runs.
The half this section is actually about does not: the buffer tracks an offset and reports no
uncertainty for it, so a merged view can be shifted onto a common timeline but still cannot say how
precisely two nodes' events can be ordered. An offset without an error bar is a point estimate being
read as a guarantee.

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
| Synthetic data marked everywhere it travels | Built | `simulated=true`, `SIM:` node prefix |
| Per-channel rate guard with counted, announced drops | Built | `Core/Resilience` |
| Coverage ledger — who was expected, who was heard | Built | `Core/Cluster/CoverageLedger*`, `Host/Startup/CoverageSetup.cs`, `--expect`, `coverage` on `/api/status` |
| Stable per-installation node identity | Built | `Core/Cluster/NodeIdentity.cs`, `Host/Startup/HostNode.cs`; persisted, and the banner says when it could not be |
| Display-path reduction that preserves extremes | Built | `/api/series?reduction=minmax`, `reducedFramesSent` / `reducedPointsSent` on `/api/status` |
| Tiered storage and rollups | Built | `Core/Storage/TieredTelemetryStore`, swept on a clock by `Host/Startup/RetentionSweep.cs`, opt-in via `--retain` |
| Bounded per-channel state with reported cardinality | Built | `Core/Resilience/BoundedChannelRegistry` — `Capacity`, `Count`, `Evictions`, and an eviction record |
| Clock offset across nodes | Built | `Core/Services/TimeSyncJitterBuffer.GetClockOffset`, behind `/api/aligned` |
| **Uncertainty on that offset** | Not started | nothing reports an error bar, so §3's actual requirement is unmet |
| Peer exchange, sequencing, deduplication, backfill marking | Not started | no `LateArriving` / `Backfill` anywhere in the tree |
| Authentication between instances | Not started | `TelemetryStreamingServer` has none. Loopback-only is not a default here but the only behaviour: nothing in the product can bind wider, and `ArchitectureRuleTests.TheConsoleBindsLoopbackOnlyInEveryProductionConstruction` fails if that changes without the question being answered |

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
