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

This is what the existing `TimeSyncJitterBuffer` is for. It has been sitting in the codebase
unreferenced, which meant the problem was understood and then not addressed.

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

## What is built, and what is not

This document describes a target. Claiming it is implemented would be the same category of error it
argues against, so:

| Area | State |
|---|---|
| Provenance on every record (`Source`, `DerivedFrom`, `RawSource`) | Built |
| Unparsed input counted and reported rather than dropped | Built |
| Warm-up carried as "no verdict" rather than zero | Built |
| Synthetic data marked everywhere it travels | Built |
| Per-channel rate guard with counted, announced drops | Built |
| Coverage ledger — who was expected, who was heard | In progress |
| Stable per-installation node identity | In progress |
| Display-path reduction that preserves extremes | In progress |
| Tiered storage and rollups | In progress |
| Bounded per-channel state with reported cardinality | In progress |
| Clock offset and uncertainty across nodes | Not started |
| Peer exchange, sequencing, deduplication, backfill marking | Not started |
| Authentication between instances | Not started |

The right-hand column is maintained by hand and is expected to be embarrassing. That is preferable
to a roadmap that reads as an inventory.
