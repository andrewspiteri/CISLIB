---
title: "RM-04 hosting, bounded observations and Part A integration review"
type: document
status: Draft
---

The actual console and web facade cases pass on Windows and Ubuntu against the
owned PostgreSQL/RabbitMQ fixtures. They establish sequential startup, admitted
work drain before dependent modules/data sources, and recovery of unfinished
durable work. The named native authentication scheme required by the existing
Security contract is explicitly registered in the fixture and documented for
applications. Actual broker authority still comes from private verified native
delivery tickets; the HTTP handler returns NoResult.

All twelve Messaging collection/exporter cases pass on each platform, including
actual HTTP export. The seven conditional streams have 435 bounded combinations
at sixteen aliases, central application/environment identity, exact module owner
binding and stale-aware in-memory backlog. Existing global Metrics caps and public
constructor signatures are preserved. Scrapes perform no SQL or broker calls.

Independent review checked immediate admission cutoff, joining actual in-flight
cleanup despite cancelled host stop, concurrent disposal, synchronous drain
factory faults, and preserving all dependent module cleanup when Messaging stop
faults. Runtime ownership remains separate from CISApplication and collector SDKs.
The source/units preserve the original fault while joining every actual drain.

The 425 affected Messaging/Operations units pass on both platforms. An additional
existing bridge regression cohort passes 120/120 on Windows; Ubuntu initially
passes 119/120 with the pre-existing Heartbeat monotonic/UTC-overflow schedule
case timing out. That result is retained and investigated separately rather than
reported as passing or erased. It does not alter the passing native host/exporter
facts. The public package and representative footprint checks remain the final
Part A qualification before WORK-013 completion.

The unchanged Heartbeat case then passes in one isolated Ubuntu run. Independent
inspection found that a sampled relative timer can be rearmed after the test's
last manual-clock advance, leaving no further clock progress. That interleaving
was not instrumented, so it remains a source-supported diagnosis rather than a
proven trace. This direct Heartbeat test and its runtime/clock are unchanged;
the 119/120 cohort is retained as intermittent existing-test evidence.

The public package check and footprint checks now pass: two outside-friend
Core-only/optional consumer applications establish packaged wire/schema/passive
composition and exact dependency/assembly boundaries. They do not claim a
package-loaded broker runtime. The fixed workload on each native lane confirms
16 publications and 16 committed effects for 4 KiB payloads at concurrency four.
Encoded admission samples are 0 / 69,872 / 0 bytes within the selected 1 MiB
budget; the final ready queue is empty. Measured elapsed is 0.787s Windows and
0.725s Ubuntu. Managed retained deltas include fixtures/pools/test state and are
385,792 / 724,072 bytes; these observations establish no throughput threshold.

WORK-012 and WORK-013 retain WORK-007's accounting owner and finite allocation.
These focused single-node developer observations do not establish deployment,
multi-node failover, a Linux SDK package build or release readiness.
