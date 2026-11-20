---
title: RM-02 local transactions bounded implementation plan
type: implementation-plan
status: Approved
---

# RM-02 local transaction integration

This is the approved implementation plan against main `539a05d1`, using the
[preserved specification](../../../planning/transactions/sources/CISLIB_RM02_Local_Transaction_Integration_Specification_v1.0.md).
The user requested the next module and started Docker after the prerequisite
question. Andrew Spiteri subsequently approved this scope, dependency profile and
allocation, and CIS registered the five canonical tasks. He then approved retaining
the original 6 GiB ceiling with exact generated-fixture cleanup, resolving the
installed allocator's inability to amend a shared ceiling. The
[storage resolution](storage-resolution-proposal.md) records that decision and
its bounded cleanup. No previous
completed owner or unused RM-01 grant authorizes RM-02.

## Reconciled contract profile

| Profile | Selected behavior |
| --- | --- |
| P01 — Placement | Portable interfaces, immutable completion/errors, generic owner and native ADO.NET capability stay in Core under `CIS.Transactions`, `.AdoNet` and `.Hosting`. No new Core dependency, transaction framework or adapter package. |
| P02 — Registration | Passive, immutable, ordinal case-sensitive aliases, at most 16, exact specified syntax; explicit supported isolation subset/default. `AddCisTransactions` repeats only with equivalent captured options. Duplicate/unknown/unsupported configuration and ambient `Transaction.Current` fail before source resolution/opening. The application supplies host-lived data sources through the existing provider. |
| P03 — Begin/ownership | One borrowed application data source, one owned connection lease and native transaction per owner. No forced `BEGIN` round trip. Failed/canceled begin releases acquired resources and rethrows the original problem; secondary cleanup uses safe diagnostics. No partially initialized owner escapes. |
| P04 — Participation | Public mockable `IAdoNetTransactionContext` and `UseAdoNetAsync` extensions; no reflection/internal casts. Exact handles are lent only for an awaited callback. Participants dispose their commands/readers and neither retain handles nor complete the transaction. A portable-only fake rejects native participation before invoking work. |
| P05 — Admission/lifetime | One callback admitted at a time, overlap/reentry rejected without a queue. Owner completion and rollback-only marks use the same gate. External disposal closes admission and joins work; a small internal execution marker rejects same-callback disposal. It never selects an ambient current transaction. Preserve the admitted callback's final rollback-only mark while disposal waits. |
| P06 — Completion | Use the specified Committed/NotCommitted/Unknown split, flags, end reasons, stable errors and at most three ordered cleanup errors. Any ordinary failure after native commit invocation is Unknown; later rollback/disposal cannot downgrade it. Confirmed commit survives later caller cancellation/cleanup errors. Snapshots publish only after cleanup, remain immutable and expose no business Success or native exception. |
| P07 — Cancellation/cleanup | Tokens belong to begin/work/commit, not an inferred owner lifetime. Admitted canceled work poisons before invoking its delegate; post-work cancellation poisons after real settlement. Unrelated OCE remains the original exception and uses work_failed unless the supplied token was observed canceled. Rollback-only precedes the final commit-token checkpoint. Fresh independent cooperative rollback budget defaults to 30 seconds; actual rollback, transaction disposal and connection disposal each settle once, in order, despite earlier failures. No replay, hard-timeout claim or pool clearing. |
| P08 — Composition | No new host service, registry, scheduler, Configuration module or DI provider. Applications drain explicit owners before disposing data sources. Pipeline errors keep their existing meaning; enclose only the short DB portion and explicitly inspect/mark/complete it. No production RM-03/RM-04 stores or EF adapter. |
| P09 — Logging | Category `CIS.Transactions`, existing correlation/identity and guarded logger. Default LogCalls=false; enabled successful terminal summary is Debug. Safe native failure/unknown diagnostics remain eligible, expected cancellation alone is not Error. Never log SQL, values, results, DSNs or raw exceptions; telemetry faults do not change work, cleanup or outcome. |
| P10 — Metrics | Three conditional streams, 128 normal combinations at 16 aliases, existing seconds buckets/capacities and authoritative identity. Capture the complete host binding at begin admission before I/O; start active/duration only at successful owner publication and stop after actual cleanup. Reuse the report's one elapsed value. No replay on late attachment, mismatched host decrement, duplicate completion counter or inferred native failure from a business callback. Preserve disabled/no-collector/CollectCoreModules=false and either registration order. |
| P11 — Dependency | Pin test/reference-only Npgsql 10.0.4 and add exactly its source mapping. Its net10 dependency is Logging.Abstractions >=10.0.0, satisfied by existing 10.0.12; keep the existing Microsoft, SDK and runtime pins. Core/CommonTesting shipping closures acquire neither Npgsql nor collector/EF dependencies. See the exact [dependency profile](dependency-profile.md). |
| P12 — Real database | One isolated local Docker PostgreSQL 18 fixture, exact official Linux/amd64 image digest, fresh owned data/credentials and bounded resources. Three ordinary logged tables remain test-only. Prove commit, later-failure rollback, caught-error marking, declined-result-only persistence, rollback/disposal, server isolation and genuinely active-query cancellation through independent connections. Run Windows/Ubuntu lanes where actually executable; an unexecuted group remains a gap. |
| P13 — Public testing | Small portable `ScriptedLocalTransactionFactory` in CommonTesting, defaults of 16 queued scripts and 16 retained owner recordings; explicit bounded exhaustion, begin failure, unknown/cleanup scripts, first rollback reason, rollback-only cannot script a commit. No ADO.NET capability, SQL engine, mocking framework or reuse of production native owners/provider doubles. |
| P14 — Consumer/evidence | Reuse the existing outside-friend Core/CommonTesting package consumer. Keep the compiling DB acceptance consumer in existing integration tests; CountryData stays the sole maintained tutorial. One canonical contract/guide, current indexes, checked-in transaction skill and root routing. Ordinary affected MSTest/build/style, actual scrape and independent review belong to their implementation tasks; no broad campaign or new harness/catalogue. |

These routine profile choices reconcile the specification with current APIs.
They do not change Workflow, REST, Startup or static environment contracts.
There is no UI or unauthenticated HTTP endpoint in this delivery, so UI design,
public-endpoint caching and manual test-catalogue workflows are inapplicable.

## Five coherent outcomes

WORK labels below are canonical task identities, registered through CIS under the
outcome decision. Each includes its tests, docs, formatting, independent
review and evidence. Public delivery remains incomplete until all applicable
outcomes pass; a contract-only checkpoint is not full runtime delivery.

| Task | Owned implementation outcome and acceptance | Dependencies | Shared hours / attempts / restoration sec |
| --- | --- | --- | --- |
| WORK-001 | Contracts, immutable options/registration, native begin and acquired-resource cleanup; portable contract and zero-I/O/ambient/alias/isolation tests. AC-01–04 and registration/ownership part of AC-23. Includes all already-performed intake. | none | 5 / 5 / 300 |
| WORK-002 | Same owner writer completes public native participation, admission/reentry, rollback-only/terminal race, commit outcomes and actual cancellation/disposal. Recording native-provider doubles cover lost acknowledgement, ordered cleanup faults and exact invocation counts. AC-05 identity, AC-08,13–21 and relevant AC-11/12/26. | WORK-001 | 6 / 7 / 900 |
| WORK-003 | Isolated PostgreSQL fixture, three-table real consumer, explicit provider/config pins, server isolation/query cancellation, host-owned pool survival, dependency inspection and concise DB/EF-recipe documentation. AC-05–07,09–12,22–23 and DB portion of AC-28. | WORK-002 | 5 / 6 / 900 |
| WORK-004 | Transaction instrumentation plus existing collector/catalog composition, one duration, balanced active lifecycle, safe logging and native-failure phases. Native recorder/actual scrape, late/disabled/host isolation and throwing telemetry. AC-24–26 and diagnostic portions of AC-16/17. | Stable WORK-001/002 hooks | 4 / 5 / 300 |
| WORK-005 | Bounded public CommonTesting helper, its behavioral tests and actual outside-friend packages; final consumer guide, module indexes, skill/routing and compiling usage integration. AC-27–28 and section 13.1. | Stable WORK-001/002 public contracts; final integration with WORK-003/004 | 4 / 5 / 300 |

One writer owns the transaction implementation across WORK-001/002. Freeze its
instrumentation hooks before delegating observations. Then database qualification,
telemetry and public-helper source may proceed independently with separate file
ownership. One coordinator owns shared project/package/lock/catalog files and
serializes builds/tests sharing outputs. Native callbacks, commit and cleanup
remain sequential; source parallelism never changes that runtime contract.

## One approved shared allocation

**24 active hours (86,400 seconds), 28 logical attempts, including 2,700 protected
restoration seconds.** All agents, intake already performed, research, source,
native commands, tests, documentation, formatting, review and restoration share
these totals. Organization and redistribution are delegated within them; no cost
reset or multiplication by agent/task count. Conservative source estimates and
measured native costs remain distinct from automatic CIS counters.

Retain the existing physical counted ceiling of **6 GiB (6,442,450,944 bytes)**
and its **64 MiB (67,108,864 bytes) reserve**, under the subsequent explicit
storage/cleanup decision. The initial approved 8 GiB amendment remains in the
canonical authorization history; it was never applied to the ledger.
The two exactly approved generated fixture folders have been removed after host
exit checks and a retained hash manifest, reclaiming 4,206,421,532 bytes.
The resulting observation counts 1,957,008,108 bytes before Docker, including the
existing native charge. The **1 GiB counted Docker fixture allowance** and reserve
leave 3,344,592,148 bytes beyond them within the same 6 GiB ceiling.
Continue counting `.artifacts`, `.cis/local`, `implementations/csharp` and the
existing native charge. Docker image/cache, owned data, logs and metadata outside
those filesystem roots remain charged through that same allowance and physical
group observation. Recount before native expansion; never silently borrow an
uncounted cache/volume or remove other historical evidence.

The [infrastructure profile](dependency-profile.md#isolated-postgresql-operational-boundary)
gives exact image identity, resource bounds and owned cleanup. Beyond the two
exact fixture roots already removed under the specific decision, only newly owned
RM-02 regenerable scratch/container/volume material may be cleaned. Never prune
the engine or touch another project's database/resources.

Preserve untracked `CountryData/Properties/launchSettings.json` SHA-256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
No external deployment, existing database access, push, merge or release is
included. Missing native/provider/platform proof remains unverified; it cannot
be substituted by the public fake or a scripted lost-acknowledgement test.

## Checkpoints

Report when guarded lifecycle/outcomes and actual cleanup work, when the real
three-table transaction proves atomicity/cancellation, and when native telemetry
and public package consumption work. Complete only the actually verified scope.
Record server/provider/candidate versions and actual Windows/Ubuntu execution.
The EF recipe is documentation, not an executed EF qualification. Broader release,
mutation, coverage and performance obligations stay separate.
