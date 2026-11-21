---
title: RM-03 bounded implementation plan
type: document
status: Proposed
cis:
  stable_id: cislib:change:cis-0018:implementation-plan
---

# Durable operations and idempotency

Deliver one short transactional operation journal under `CIS.Operations`.
A matching retry recovers its original committed business result without another
committed protected effect while the scoped duplicate-protection row remains.
The application owns the supplied RM-02 write transaction and commits it explicitly.
A handler can run again after rollback; Prepared never means committed.

This is the bounded plan requested by the user, not an implementation approval.
The attached handoff is preserved byte-for-byte. Current main and actual starting
commit are `8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc`; branch:
`codex/durable-operations`. No relevant baseline divergence was found.

## Reconciled implementation profile

The [reconciliation](reconciliation.md) gives concrete semantics and routine
defaults, including the portable fingerprint frame. These choices implement
the settled scope; API spelling and internal organization remain ordinary
implementation decisions.

| Profile | Decision |
| --- | --- |
| P01 | Core portable contracts and a production BCL ADO.NET PostgreSQL adapter; no new shipping package or runtime dependency. Keep Npgsql10.0.4 test-only. |
| P02 | Identity is captured server application/environment, app-resolved trusted scope, registered operation name and opaque caller key. Exact ordinal identity excludes instance/version/trace/credentials. |
| P03 | Owned normalized input and bounded operation-specific canonical fields, versioned deterministic framing and server SHA-256. Shared byte/digest vectors; no arbitrary JSON/reflection canonicalization. |
| P04 | Mockable Execute/GetResult ports. Separate business Succeeded/Rejected, execution disposition and existing RM-02 persistence facts. Public store/codec ports are adapter infrastructure, not application claim/complete APIs. |
| P05 | One non-generic module-wide weak context admission marker, shared by all service instances and generic operation types. One atomic admission is never reset; retries use fresh owners. |
| P06 | Resolve actual scope/target and run the existing capability authorizer before first execution, replay or lookup. Caller-context projection is not authentication; authorization/audit faults propagate. |
| P07 | Authorize/normalize/validate/admit, awaited native claim/read, handler outside that callback with the original context and owned normalized input, encode/validate, separate awaited native terminal write, then Prepared. Poison after-claim failures and preserve originals. |
| P08 | Fresh authoritative ReadCommitted lookup through the existing RM-02 factory. Settle/dispose its own read scope, never the caller's owner/source. Optional ReadScopeCompletion exposes existing safe cleanup facts without downgrading a known committed result. |
| P09 | Explicit versioned PostgreSQL installation SQL shipped with Core, unique full identity and schema-version checks. Registration performs no I/O/migrations. Unexpected incomplete/corrupt retained rows block reexecution. |
| P10 | PostgreSQL v1 execution is explicitly ReadCommitted-only; reject other selected isolations before claim/effects. ON CONFLICT DO NOTHING RETURNING, then a separate read on conflict. No statement/handler/commit retry or session-setting mutation. |
| P11 | Indefinite result/protection retention by default. Persist immutable per-row recording/expiry facts; logical expiry applies before maintenance. Unsupported fingerprint comparison precedes mismatch, mismatch precedes matching-result expiry; unsupported result decode never permits execution. |
| P12 | Explicit privileged maintenance for one registered operation/trusted scope, its own RM-02 transaction, at most256 rows and SKIP LOCKED only there. Payload expiry retains protection; actual full-row deletion ends it. No scheduler or selected polling interval. |
| P13 | Stable bounded result contracts/readers; copy encoded bytes and decode that representation for Prepared/replay fidelity. Defaults:64KiB canonical input,256KiB result; each configurable only up to1MiB. At most4 retained fingerprint comparators/result readers per definition. |
| P14 | Two conditional operation timers,16 aliases ×13 execute outcomes and16×9 lookup outcomes:352 normal combinations. Reuse central identity/binding/buckets; no duplicate commit metrics or global capacity increase. LogCalls defaults false, safe failure summaries only. |
| P15 | Real owned PostgreSQL atomicity/races/retention and a small C# seam that reports failure after a real native commit. Label simulated acknowledgement loss; no proxy/network-fault framework. |
| P16 | Bounded CommonTesting substitutes and the existing actual outside-friend package fixture. Existing Windows package route and Windows/Ubuntu runtime routes; missing Linux SDK-backed package execution remains unverified rather than triggering a tooling campaign. |
| P17 | Current portable contract, compiling consumer, provider/authorization/recovery guidance, focused repository skill and routes. CountryData remains the one maintained tutorial with its database-free default. |
| P18 | One shared finite allocation, original6GiB physical ceiling and64MiB reserve. Fresh labelled/private loopback Docker fixture only after approval. All agents/intake/tests/docs/review/restoration share totals; no existing DB, deployment, push, merge or release. |

## Coherent delivery tasks

Every task includes meaningful tests, scoped formatting, documentation, independent
review and completion evidence. These are medium delivery leaves within a larger
outcome. No coordination/documentation/test-sweep task is created.

| Draft task | Delivered behavior and included verification | Depends on | Proposed hours / attempts / restoration |
| --- | --- | --- | --- |
| WORK-001 | Portable identity/decision/result/store/codec contracts; finite immutable definitions, owned normalization, canonical writer and shared vectors; passive registration/metadata. Unit invalid-input/bounds/defaults/culture/Unicode/snapshot tests and contract docs. No registered service with public throwing stubs. | none | 5 / 6 / 300s |
| WORK-002 | Authorized durable Execute/GetResult protocol, shared weak admission, exact RM-02 sequencing, rollback poisoning, fresh-read ownership, version/expiry/conflict precedence and stored-result fidelity. Deterministic store/ADO doubles for admission, authorization/audit/handler/codec/cancellation faults and cleanup; usage docs. | WORK-001 | 7 / 8 / 600s |
| WORK-003 | Production PostgreSQL journal, packaged versioned installation SQL, schema verification, exact unique arbitration and conditional finalization. Real atomicity, matching/mismatched races, rollback/disposal, fresh lookup and RC/isolation/timeout cases; provider docs. | WORK-002 | 6 / 7 / 1200s |
| WORK-004 | Explicit privileged bounded retention maintenance, tombstones/deletion/compatibility recovery and concurrent maintenance correctness. Real expiry/decode/version races and native-commit acknowledgement-loss seam with recovery; persistence/cleanup invariants and maintenance docs. | WORK-003 | 5 / 6 / 900s |
| WORK-005 | Existing Logging/Metrics integration, finite conditional catalog and exact host binding. Core diagnostic-fault cases plus actual exporter/identity/disabled/order/host-isolation checks and affected catalog regressions; metric contract/docs. | WORK-002 | 4 / 4 / 300s |
| WORK-006 | Public bounded scripts/recordings in CommonTesting, actual outside-friend package consumption, compiling final consumer/guide, checked-in Operations skill/root routes/current indexes and integrated review. No fake SQL engine. | WORK-002; completion also requires003/004/005 | 5 / 5 / 300s |

Contracts/normalization are one coherent foundation, followed by one protocol
owner. PostgreSQL creation/arbitration and later privileged retention maintenance
have distinct executable outcomes and share a serialized SQL writer. Observation
integration and public substitution have different file ownership and can start
after WORK-002 freezes contracts/hooks. These boundaries keep broad state,
security, SQL and package concerns from becoming one high-complexity leaf.

Do not call a partial foundation full RM-03 delivery. WORK-003 may establish the
indefinite-retention journal; WORK-004 owns explicit maintenance and final retained
recovery qualification. Earlier tasks must reject unsupported/corrupt states safely.

## Execution organization and checkpoints

One coordinator owns shared project/package/lock/catalog files and serializes
builds plus real-provider lanes. One Core writer owns WORK-001/002. After its
contract/hooks freeze, PostgreSQL, observations and CommonTesting source may
proceed independently. WORK-003/004 share the journal/schema writer; no concurrent
edits to those files. Agents may review independent source/results.

Runtime Execute phases remain sequential. Native callbacks never wrap the whole
application handler. No lock spans the application handler and RM-02 does not
guard concurrent owner completion across it; callers must await the complete
Execute before completing/disposing that owner.

Report concise checkpoints after:
1. The protocol preserves authorization, one-context admission, Prepared,
   poisoning and independent lookup under actual focused tests.
2. Real PostgreSQL proves one committed effect across competing sessions,
   replay/conflict, retention and simulated acknowledgement-loss recovery.
3. Actual collector output and public package consumption work, with exact
   candidates, executed lanes and unresolved limits recorded.

## Acceptance ownership

Source section10 contains14 groups; it is the acceptance baseline, not a new
manual catalogue obligation.

| Source acceptance | Primary owner(s) |
| --- | --- |
| 1 atomic business/journal/outgoing visibility | 003 |
| 2 original result replay;3 conflicting request | 002,003 |
| 4 cross-instance/session races;5 rollback/disposal retry | 003 |
| 6 handler/codec/oversize/write/cancellation;7 rollback-only/rejection | 002,003 |
| 8 no uncommitted lookup and uniqueness arbitration | 002,003 |
| 9 Unknown/real-commit simulated acknowledgement loss/Committed cleanup | 004, retained RM-02 boundaries |
| 10 nested/concurrent/repeated context admission | 002 |
| 11 isolation, contention, deadlock/serialization and settlement | 003,004 |
| 12 scope/caller/authorization and instance replacement | 002,003 |
| 13 expiry/tombstones/compatibility/maintenance races | 002,004 |
| 14 passive DI/diagnostics/bounds/public mocking/actual package | 001,002,005,006 |

Reuse ordinary native MSTest and existing fixtures. No broad coverage, mutation,
performance or release campaign is part of this module. A missing infrastructure
lane or real-provider observation is unverified, never silently replaced by scripts.
A caller-selected unsupported isolation is tested as explicit rejection, not
advertised as supported PostgreSQL operation execution.

## Shared proposed authority and limits

Request one implementation decision covering **32 active hours /36 logical work
attempts**, including **3600 restoration seconds**, across all agents and already
performed intake. The table distributes the same totals; it does not multiply
them. Codex may organize tasks and redistribute within the approved overall
allocation. Logical work attempts are not individual CLI invocations.

Retain **6,442,450,944 counted bytes (6GiB)** and **67,108,864 protected restoration
bytes (64MiB)** under `CISLIB-CIS-0001-STORAGE-2026-11-10`. The conservative1GiB
Docker allowance stays inside this same physical group. Current intake counts
2,960,996,728 filesystem/native bytes; after that allowance/reserve it leaves
2,340,603,528 bytes. Recount before execution; no additional historical cleanup,
new physical pool or ceiling increase is proposed. See [infrastructure](dependency-profile.md).

All existing CIS owners are complete/closed. Draft task JSON is not a runnable
canonical owner. After actual approval, record that specific decision with
`change authorize`, organize these contracts through the existing task commands,
validate the canonical plan and apply finite work allocation before implementation
or product-native execution. Do not infer approval from the attached imperative.

The applicable [CIS bounded-planning skill](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-bounded-planning/SKILL.md)
says preparation grants no execution authority and requires the complete scope
and overall limits before one new outcome decision. [AGENTS.md](../../../AGENTS.md)
requires an eligible approved finite native work owner. This is one fresh
implementation/allocation decision, not reconfirmation of settled requirements.


For WORK-003, retain one representative complete-operation workload receipt
(workload-evidence.json through the existing evidence route) with actual bytes,
row shape, concurrency, elapsed observation and resource/candidate facts,
separately from installation/component checks. This owns positive end-to-end
acceptance and introduces no benchmark campaign or runner. CommonTesting scripts
and recordings use16/16 defaults with explicit finite configured capacities;
their observations are scripted consumer facts, not SQL durability proof.

[Independent draft review](review-intake.md) found no material required correction.
Draft JSON checks verify six medium contracts, declared scope references, an
acyclic dependency graph, exact shared resource sums and valid local links.
Canonical CIS plan validation occurs after the actual outcome decision establishes
its task contracts; no placeholder runnable plan was generated to simulate it.
