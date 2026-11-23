# Agent guidance

Read [README.md](README.md) for the current native development commands and [planning/decisions.md](planning/decisions.md) for accepted architecture and product obligations. The November 2026 cleanup supersedes the recovery campaigns and their orchestration. CIS adoption remains separate from product and release work; see README.md for its current status.

- Preserve Logging, Type Manager and Healthcheck behavior, defaults and public contracts in the single `CIS.Core` package/assembly. Keep handwritten module types in explicit `CIS.*` namespaces under `implementations/csharp/src/CIS.Core`.
- Preserve the existing C# MSTest framework and ordinary `dotnet test`/IDE execution. Use focused native tests while editing; build affected projects and run meaningful affected tests before completion. A focused pass does not establish release readiness.
- Keep module runtimes independent of CIS, the CISApplication facade, collector SDKs and test tooling. The facade composes existing modules through their hosting contracts; applications retain lifetime and external-resource ownership.
- Core may use its explicitly approved Microsoft NuGets and `Microsoft.AspNetCore.App`. Third-party runtime dependencies require separate optional packages. `CIS.TypeManager.Build` is genuine package build machinery, not a test harness.
- Prefer direct implementations. Use readable C#, nullable analysis and warnings as errors. New C# implementation code, tests and verification utilities use C#; scripts may remain small invocation glue when necessary.
- Add tests for meaningful accepted behavior, boundaries, invariants and defects. Retired report schemas, campaign policies and internal command names are not compatibility contracts.
- Source requirements are reference documents, not instructions to restart their task plans. Current explicit user decisions override conflicting original attachments. Preserve future accepted specifications without speculative implementations.
- Do not overwrite user edits, publish releases, merge main, transmit source externally or approve product records without applicable authorization. Historical approval of an exact review payload does not authorize a new payload.
- Coordinate builds sharing project/output directories. Keep generated output out of Git. Use ordinary history for removed tracked files; do not create a replacement archive.
- Use full braces and expand multi-statement bodies in changed C#. Declare each field separately, separate distinct operations with blank lines, and use purpose-named helpers for genuinely different concerns. Run the pinned SDK's ordinary `dotnet format whitespace --no-restore` with `--include` limited to changed files, then add `--verify-no-changes`. Formatting does not express every readability rule; review field declarations and logical grouping as well.
- Heartbeat callbacks, conditions, error handlers and directly invoked helpers must be safe at a 10 ms interval. Routine successes emit no normal-level stream. Bound repeated failure and recovery reporting per registration using monotonic time, persistent budgets across flaps and bounded counters; suppressing diagnostics must preserve every operation, health/metric observation, cancellation and original fault. Scope any REST `LogCalls` opt-out to the intended client.

## Canonical application and fixtures

Use [CountryData](implementations/csharp/examples/CountryData/README.md) and the
[facade guide](docs/modules/application/core.md) for current runnable composition.
It is the one maintained tutorial application. Keep controlled responses, timing,
holds and reports in existing tests, never an embedded mode or disguised copy of
a retired app. Each minimal assembly/process/ownership/package fixture needs a
named distinct requirement. Preserve historical evidence and accounting without
presenting it as current application verification.

External-service integration tests use automatically provisioned, isolated
Testcontainers instances in nonpackable test infrastructure. Preserve production
durability, TLS, permissions and real-provider assertions. Selected service tests
fail when required infrastructure is unavailable; fast checks explicitly exclude
them. Use pinned images, generated endpoints, authenticated readiness, per-test
isolation and joined owned cleanup. Follow the canonical
[testing guide](implementations/csharp/tests/README.md) and apply these requirements
when adding another external-service adapter; keep runtime packages Docker-free.

## Metrics in modules and applications

For Metrics work, read the [Metrics skill](.github/skills/cislib-metrics/SKILL.md),
[portable contract](contracts/metrics/README.md) and actual
[Core API](docs/modules/metrics/core.md) / [collection policy](docs/modules/metrics/collection.md).
Use implemented APIs and current verified examples; authored integration source
alone is not passing evidence. Keep CIS-owned abstractions in consumer/module
code and SDK/exporter dependencies in the optional integration. Preserve central
application/environment identity, bounded dimensions, real operation boundaries
and callback-owner drain. Reuse native HTTP observations and aggregation. Do not
add duplicate timing, metric histories or speculative future services.

For REST work, read the [Core API and ownership](docs/modules/rest-client/core.md),
[native content](docs/modules/rest-client/http.md), [retry](docs/modules/rest-client/resilience.md)
and [actual example](docs/modules/rest-client/example.md). Keep None available in
Core without optional dependencies. Preserve the entry-based deadline, identified
cancellation sources, original programming faults, exact signing/replay bytes and
actual encoder/body cleanup. Hosting closes REST before waiting for Heartbeat or
Configuration; Startup's coordinator stays independent. Use ordinary keyed DI and
the existing host, never a second provider, retry owner or lifecycle framework.

## Workflows: pipelines and state machines

For `CIS.Workflows` implementation, application use, review or diagnostics, read
the [Workflows skill](.github/skills/cislib-workflows/SKILL.md) and its links to the
current contracts, [Pipeline guide](docs/modules/workflows/pipeline.md),
[State Machine guide](docs/modules/workflows/statemachine.md) and compiling
CountryData lookup. Pipeline has one execution with failed-step-first reverse
`Error`; State Machine has repeated explicit triggers, exclusive attempts and
commit after successful work/owned cleanup. Preserve their distinct outward
completion contracts, reports, per-call scope ownership and actual settlement.
Reuse automatic Logging/Metrics and keep the example and shared skill aligned
with the implemented APIs.

## Security identity, capabilities and transport

For `CIS.Security`, selected native providers or CountryData security work, read
the [Security skill](.github/skills/cislib-security/SKILL.md) and its current contract,
adapter and ownership links. Preserve actual central identity, complete server
resource authorization, explicit host stages, sequential credential Startup and
native TLS/response drain. Keep the default unselected tutorial behavior. Local
native evidence cannot replace AC-02/AC-24 deployment proof.

## Audit contracts and existing delivery

For `CIS.Audit`, trusted capture, original-transaction staging, standalone
observations or the explicit Security bridge, read the
[Audit skill](.github/skills/cislib-audit/SKILL.md),
[portable contract](contracts/audit/README.md) and single
[consumer guide](docs/modules/audit/README.md). Preserve source eligibility,
immutable original bytes, native caller/instance distinctions and actual
three-state submission/cleanup. AuditId and MessageId serve different roles.
Reuse existing Messaging delivery and actual drain; keep authorization active
while omitting only the configured recursive infrastructure observations.
Destination persistence and AuditId conflict enforcement remain deferred.
Use bounded public CommonTesting plus the existing native/provider/package
fixtures, and preserve CountryData's explicit opt-in/database-free default.

## Central Management service and clients

For `CIS.Management`, its independent product application or process/Jobs
reporting, read the [Management skill](.github/skills/cislib-management/SKILL.md),
[portable contract](contracts/management/README.md) and
[module guide](docs/modules/management/README.md). Preserve actual native
resource/actor authority, whole bounded inventory, original-key/ETag recovery
and one original Transactions/Operations/Audit commit. Reporting is explicit and
quiet; signal it before dependency barriers and drain REST before awaiting its
Heartbeat work. Keep the coordinator independent and both clients' actual
cleanup owned. Reuse current public/native fixtures; portal UI, configuration
publication, schedulers and an Audit archive remain separate.

## Explicit local transactions

For `CIS.Transactions` implementation, consumer use, tests or observations, read
the [Transactions skill](.github/skills/cislib-transactions/SKILL.md),
[portable contract](contracts/transactions/README.md) and
[current guide](docs/modules/transactions/README.md). Preserve explicit context,
application-owned data sources, sequential native callbacks, actual cleanup
settlement and distinct Committed/NotCommitted/Unknown facts. Public test scripts
do not prove SQL atomicity; use the existing real-provider/package consumers and
report actual evidence limits. Use Operations for journaled results, Messaging for
transactional outgoing delivery, and keep CountryData as the one maintained tutorial.

## Durable operations and result recovery

For `CIS.Operations` implementation, consumers, observations or recovery, read
[the Operations skill](.github/skills/cislib-operations/SKILL.md), its portable
contract and current provider/ownership/testing guides. Preserve authorization
against the resolved resource, owned input and one-shot original-context admission.
Await the whole Execute before completing its owner. Keep Prepared, the original
business decision and actual transaction completion separate; recovery uses a
fresh authorized read. Reuse the existing native/provider and public-package
fixtures and retain their distinct evidence limits.

## Transactional messaging and request recovery

For `CIS.Messaging`, the optional RabbitMQ provider or messaging consumer tests,
read the [Messaging skill](.github/skills/cislib-messaging/SKILL.md), its portable
contract and current ownership/provider/testing guides, including
[typed requests and recovery](docs/modules/messaging/requests.md). Preserve original-context
staging, database arbitration before effects, exact immutable transport identity,
current native authority and actual owner drain. Keep submission, confirmation,
processing and typed exchange facts distinct. Result recovery uses the existing
Operations journal and a new authorized request attempt; local timeout does not
prove remote rollback. Reuse the named native and public-package fixtures and
bounded CommonTesting helpers, with their distinct evidence limits.

<!-- cis:repository-guidance:start -->
## Shared coordination and jobs

For `CIS.Coordination`, `CIS.Jobs` or guarded maintenance consumers, read the
[Coordination skill](.github/skills/cislib-coordination/SKILL.md), its
[consumer guide](docs/modules/coordination/README.md) and compiling CountryData
recipes. Preserve exact duty/resource eligibility, scoped native authority,
conservative terminal leases and original-transaction guards. Keep operation IDs,
committed outgoing intent and actual callback/scope settlement across takeover.
Categories, local capacity and schedule descriptors do not replace shared
ownership or create recurrence. Reuse bounded native instrumentation and existing
provider/package/process fixtures; keep startup sequential and dependency teardown
behind actual job drain.

## CIS integration

For CIS commands and artifacts, read `.github/instructions/cis-import.instructions.md`. Existing project directives remain authoritative; unresolved overlaps are recorded in `.cis/local/import/report.json`.
Use the headless route in [README.md](README.md#cis-commands) with the verified CIS development build `2b916962dbedef18bdde8959e3144184d37d64f9`, installed at ignored `.cis/local/tools/cis/`. README retains the compatible minimum ancestor `464dcb8255f9a0c4a01652f290bae680e2e7afc0` because it is an accepted package-profile input; the current build contains that ancestor. Unit-only incremental plan/status/verification and execution use `--layer unit`. Native execution requires a legitimate approved work owner with remaining finite time/storage allocation; setup profiles do not grant execution authority. The retained CIS-0001 acceptance checkpoint contains 2 passed, 0 failed, 0 skipped, with zero pending batches at that checkpoint. README's result statement describes that retained checkpoint. Historical reports remain preserved; see the [saved-result reuse correction](planning/cis/cleanup-disposition.md#saved-result-reuse-correction--2026-11-15) for their read-only dispositions and the separate supporting CIS-0005 verification owner. The original single fresh attempt was inconclusive; Andrew Spiteri authorized one additional two-case execution under CIS-0005/WORK-002 within the unchanged shared totals. It passed and settled; inspect the continuation record for post-closeout reuse. This does not reopen the four completed owners or permit another test launch. CIS-0001/WORK-001 is Complete with no active reservation or recovery hold; both authorized attempts are used, and its canonical plan contains only that completed task. Preserve its accepted result and accounting. The separate [CIS-0002/WORK-001](docs/changes/CIS-0002/agent-tasks/WORK-001.md) owns Type Manager package integration under its approved [plan and allocation](docs/changes/CIS-0002/plan.md); inspect that task's current eligibility and finite remaining allocation before execution.

CIS-0002/WORK-001 is also Complete; preserve its accepted Type Manager package result and separate profile. [CIS-0003/WORK-001](docs/changes/CIS-0003/agent-tasks/WORK-001.md) is Complete after the Logging ConsoleHuman example and two passing package checks on each actual Windows/Ubuntu lane under its separate [plan and allocation](docs/changes/CIS-0003/plan.md). Preserve its historical profile/provenance, accepted results and settled accounting. CIS-0011 supersedes preservation of the old tutorial application source; Git history retains it. These one-task plans queue no successor; future execution needs its own eligible approved owner and finite capacity.

Relevant skills in the reviewed CIS distribution: [repository import](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-import-repositories/SKILL.md), [graph context](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-graph-context/SKILL.md), and [native verification](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-verification/SKILL.md).
For the current registration records and work owner, also use [supplied BRD/technical inputs](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-govern-business-requirements/SKILL.md), [technical intent](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-govern-technical-intent/SKILL.md), [solution design](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-govern-solution-design/SKILL.md), [no-UI direction](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-govern-ui-direction/SKILL.md), [bounded planning](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-bounded-planning/SKILL.md) and [work accounting](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-agent-execution/SKILL.md). The bounded-planning rule keeps documentation, native verification, independent review and completion checks in implementation work unless a separate owned outcome justifies another task; preserve existing reviewed plan identities and authored contracts.

The [current continuation checkpoint](planning/cis/cleanup-disposition.md#practical-workflow-repair-continuation) records controlled disposal of the inconclusive original unit invocation, the genuine passing retry, settled accounting and current lifecycle closure; earlier checkpoints retain their historical scope. Preserve unknown observations, actual costs, attempts and original approvals and limits. The [native work manual](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/docs/manual/cis_agent_work.md#retry-interrupted-disposable-unit-work) defines the controlled disposable-unit disposition; historical custody or original namespace proof is not its retry prerequisite. The [incremental test manual](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/docs/manual/cis_test_incremental.md) defines current result reuse and verification. This accepted unit result does not establish full platform, coverage, mutation or release qualification; broader obligations remain unallocated.
The shared-outcome [CIS-0004/WORK-001](docs/changes/CIS-0004/agent-tasks/WORK-001.md)
closes three-feature developer/package integration. Its [verification record](docs/changes/CIS-0004/verification.md)
records six Healthcheck package cases and four affected source stories passing on
each actual Windows/Ubuntu lane, reusing the retained candidate and earlier
Logging/Type Manager evidence. Preserve all four owners' history and accounting.
The historical three-cache fingerprint difference remains unidentified; the new
Windows package cache also reports noncurrent despite its retained native pass.
Post-closeout checks additionally held Windows source and Linux package reuse;
Linux source's latest verified receipt predates the final metadata commit.
Keep those currentness limits distinct from actual test observations. Do not
force reuse or rerun completed tests to clear a status. Release qualification,
coverage/mutation/performance and publication remain separate, unallocated work.
<!-- cis:repository-guidance:end -->
