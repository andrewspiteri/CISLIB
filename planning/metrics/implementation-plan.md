---
title: Metrics bounded implementation proposal
type: planning-reference
status: Draft
---

# Bounded Metrics implementation proposal

Read the [shared scope and source reconciliation](README.md) and
[acceptance map](acceptance.md). Andrew Spiteri explicitly approved this profile
and shared allocation. Supported CIS operations assigned A/B/C/D/E to
WORK-001/002/003/004/005, with the concrete contracts in the canonical plan.

This document preserves the initial approved proposal and ordering assumptions.
The five implementation leaves now have local source; supporting package-source
corrections did not replace their identities or create another resource pool.
[CIS-0009 evidence](../../docs/changes/CIS-0009/verification.md) records the actual
native checkpoints, later corrections and remaining work. Canonical task records
remain authoritative for current owner status, allocation and execution eligibility.

## Three outcomes with bounded implementation leaves

Retain the guide's three coherent outcomes. The provider/HTTP integration and
six-module changes are broad enough to split into five executable leaves rather
than assert that all integration/state/verification fits one medium task. These
are product boundaries, not separate planning, documentation or QA campaigns.
Every leaf owns its meaningful tests, documentation and independent review.

| Leaf / outcome | Owned delivery and validation | Ordering |
| --- | --- | --- |
| A — Portable primitives | Core/Metrics public types, BCL adapters, timer, finite definition/name/unit/dimension policy, host-owned attachment/callback seam; contracts and portable fixtures; primitive races/negative behavior/architecture. | First; freeze public types and shared catalog/ownership before parallel edits. |
| B — Collection policy | Optional package provider/reader, exact host Meter selection, one effective view, authoritative resource projection, catalog/late admission, native reserved capacity and overflow; real scrape identity and callback-settlement proof. | After A. Early real identity/drain proof gates broad integration. |
| C — Runtime service observations | Named Logging, Heartbeat and Healthcheck operation/accounting/state files; finite catalog entries and one normal/failure path per module; preserve actual outcomes/defaults and metric-failure isolation. | After A; can author alongside B in nonoverlapping files; integrate only against the frozen seam and early collection proof. |
| D — Bootstrap/startup observations | Named StartupCoordinator, Configuration operation/report and Type Manager result files; bootstrap facts without replay; sequential operation boundaries, state and one normal/failure path per module. | After A; can author alongside B/C; no edits to shared hosting/project files. |
| E — Hosting and usage | Reserved branch, standard concurrency limiter, native HTTP policy/enrichment, lifecycle integration, combined example and actual optional-package consumption; A-09–A-14, two-host isolation, held collection/owner stop, one footprint observation, skill/routing and integrated docs. | Starts when A/B seams are stable. Completion requires C and D integrated plus real host/package checks; independent service authoring is not blocked on E. |

Each initial leaf was assessed at medium only within these named boundaries.
Provider state/SDK uncertainty belongs to B's early proof; HTTP/route and lifecycle
integration belongs to E; service observations do not redesign their runtimes.
An actual remaining high/unknown item must be bounded further through supported
task organization under the same outcome rather than implemented as a framework.

## Ownership and parallel safety

The controller owns solution/project/package/lock/catalog changes and serializes
all shared native builds. A owns shared Core/Metrics public contracts. B owns the
optional provider/policy files. E owns optional hosting/HTTP files, shared existing
Startup/Heartbeat/Configuration hosting edits and the example. C/D edit only named
module operation/state files and their respective focused tests/guides. One owner
must merge a requested shared-file change before others consume it.

Runtime startup remains sequential. Runtime recording may be concurrent using the
BCL/SDK's existing aggregation. Snapshot callbacks perform no I/O, discovery,
health evaluation or startup work. Scrape timeout is not callback preemption.
Host-owned adapters, precreated services and existing constructor behavior remain
intact; real callback drain protects their lifetime.

## Authorized single allocation

The approved total is **10 active hours (36,000 seconds), 12 logical attempts**, shared across
every agent, implementation, restore/build/test, documentation, review and recovery.
It includes **900 seconds and 64 MiB reserved for restoration**, not extra capacity.
Reuse the existing physical six-GiB counted-storage ceiling/group
`CISLIB-CIS-0001-STORAGE-2026-11-10`; existing files/evidence stay counted.
No cleanup controller, historical budget reuse or second storage pool is proposed.

Indicative leaf seconds are A 5,400, B 7,200, C 7,200, D 7,200 and E 9,000;
included restoration seconds are 120/240/180/120/240. Attempts are 2/3/2/2/3.
The controller may organize supporting work and redistribute these same totals
under the recorded authority. Scratch is `.artifacts/metrics/CIS-0009`, counted under
the existing physical group. Stop at actual limits; preserve restoration capacity.

The optional `CIS.Metrics.OpenTelemetry` closure is now locked at SDK 1.21.0,
exporter 1.21.0-beta.1 and required DiagnosticSource 11.0.0. The approved source
corrections and actual restore/consumer evidence are recorded separately.
Core's established closure and .NET 10.0.12 / SDK 10.0.401 pins remain unchanged.
The supplied finite v1 defaults, mandatory identity, direct endpoint, host isolation
and restart-only provider configuration form the approved contract profile.

## Focused verification and stop conditions

Reuse existing MSTest projects, ordinary native commands, existing host/clock
fixtures and actual package machinery. Use independent expected values, literal
exported names and a real parser for the negotiated format. Do not reimplement
the SDK or build another runner. Reserve native streams that appear lazily.

Run affected Unit/Integration/Architecture/Compatibility/Business and applicable
bounded Mutation checks at meaningful boundaries. Exact Core/package/optional
closure and each actually executed platform are recorded separately. Preserve
PerformanceObservation/ExtendedStress exclusions; authorize only the one specified
footprint observation, not a broad benchmark or full release campaign. Keep the
existing 95/90 coverage and 90 mutation release obligations and prior gaps intact.

The footprint case records the actual default catalog/capacities, 14 duration
buckets, label shape, warm-up, two concurrent scrapes, response bytes, incremental
retained managed memory and plateau behavior. Include zero/overflow points and
per-format cache state in interpretation. The four-MiB cap may reject a fully
populated theoretical catalog; do not call that truncated success or claim a
four-MiB heap guarantee. Report whether the 64-MiB retained target was met.

Stop dependent expansion for a concrete incompatible pinned dependency, an
unprovable identity/host-isolation or callback-owner lifetime contract, a missed
default memory target without a supported tuning decision, or a hard resource
limit. Present the exact remaining material decision; do not silently weaken
requirements. No publication, default-branch merge or future-module implementation
is included in this proposal.
