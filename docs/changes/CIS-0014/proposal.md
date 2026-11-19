---
title: "Enum-based Workflows State Machine with result-based transitions"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0014
outcome: "Deliver the supplied State Machine requirements in existing CIS.Core: immutable enum maps, exclusive reusable Trigger attempts, commit after actual cleanup, per-call results, scoped DI handlers, existing-host draining and bounded automatic telemetry; extend CountryData and the one Workflows skill with focused native/package evidence under the user-delegated finite time authority."
baseline_kind: git
baseline: "1e0595f28dd284b365aadae9a7549a0fdb75848a"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:7567ad164a237cd53b739fa957a5194817110b7f95b43f1bdbc8400db900bd0d"
authority: derived-outcome-authorization
---

# CIS-0014: Enum-based Workflows State Machine with result-based transitions

## Outcome

Deliver the supplied State Machine requirements in existing CIS.Core: immutable enum maps, exclusive reusable Trigger attempts, commit after actual cleanup, per-call results, scoped DI handlers, existing-host draining and bounded automatic telemetry; extend CountryData and the one Workflows skill with focused native/package evidence under the user-delegated finite time authority.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Preserve the exact supplied specification and reconcile SM01-SM10/A01-A15 through the bounded P01-P12 profile in implementation-plan.md.
- Deliver immutable enum maps, exclusive reusable result-based attempts, commit-after-cleanup and fresh selected-handler ownership without changing Pipeline contracts.
- Compose one existing host lifetime and bounded automatic telemetry through explicit State Machine opt-in.
- Prove the actual CountryData/public-package consumer and shared repository skill with meaningful focused native tests and documentation.
- Count all work under the finite user-delegated allocation; preserve storage/restoration and user files, with no push, merge or release.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-STATE-MACHINE-2026-11-18:USER-DELEGATED-REQUIRED-TIME`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver the supplied enum-based State Machine requirements in existing CIS.Core, integrated with existing host/telemetry and CountryData, preserving Pipeline contracts, under user-delegated bounded required-time authority.

- `consumer`: P11-P12: request-local CountryData State Machine using fresh existing lookup Pipeline; public/package mockable result, Busy/child scope story, one shared Workflows skill/metadata/routing; include HTTP/cache regressions, docs, formatting and review. Paths: `.cis/local/statemachine-intake`, `.cis/local/testing`, `.github/skills/cislib-workflows`, `AGENTS.md`, `README.md`, `contracts/workflows`, `docs/catalog.yml`, `docs/changes/CIS-0014`, `docs/modules/workflows`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/examples/CountryData`, `implementations/csharp/tests`, `planning/workflows/sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md`, `planning/workflows/statemachine`.
- `execution`: P01-P07: immutable enum definitions, reusable exclusive direct runner, normal guarded results, selected handler DI, commit/cancel/fault/Error/disposal and child Pipeline contract; include tests, portable contract, formatting and review. Paths: `.cis/local/statemachine-intake`, `.cis/local/testing`, `contracts/workflows`, `docs/catalog.yml`, `docs/changes/CIS-0014`, `docs/modules/workflows`, `implementations/csharp/src/CIS.Core/Workflows`, `implementations/csharp/tests`, `planning/workflows/sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md`, `planning/workflows/statemachine`.
- `hosting`: P08: explicit State Machine factory, per-call borrowed-scope adapter, existing shared workflow owner/bridge and real host/facade/failed-Build drain; include held-work tests, documentation and review. Paths: `.cis/local/statemachine-intake`, `.cis/local/testing`, `contracts/workflows`, `docs/catalog.yml`, `docs/changes/CIS-0014`, `docs/modules/workflows`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Startup/Hosting`, `implementations/csharp/src/CIS.Core/Workflows`, `implementations/csharp/tests`, `planning/workflows/sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md`, `planning/workflows/statemachine`.
- `instrumentation`: P09-P10: automatic safe logs, three reusable metric handles, transactional finite enum/alias union, opt-in collector reservation/attachment and actual scrape; include tests, metric documentation, formatting and review. Paths: `.cis/local/statemachine-intake`, `.cis/local/testing`, `.github/skills/cislib-metrics/SKILL.md`, `contracts/metrics`, `contracts/workflows`, `docs/catalog.yml`, `docs/changes/CIS-0014`, `docs/modules/metrics`, `docs/modules/workflows`, `implementations/csharp/src/CIS.Core/Metrics`, `implementations/csharp/src/CIS.Core/Workflows`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests`, `planning/workflows/sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md`, `planning/workflows/statemachine`.

- Andrew Spiteri requested the next Workflows State Machine module then explicitly delegated approval of required time while unavailable. Codex selects the finite P01-P12/four-task implementation profile and shared 50400 active seconds/16 logical attempts including 1500 restoration seconds under that standing direction. The user did not separately approve an exact 14-hour payload. All agents, already-performed intake, implementation, native tests, documentation, skill, formatting and review count.
- Controller-selected numeric metadata bounds are 16 machine aliases, 512 full-domain machine/source/trigger tuples including invalid, and 64 machine/handler pairs, transactional bounded union. Reserve 2560/16/384 combinations under unchanged global ceilings. These caps are not supplied numeric defaults. Routine logs default Debug and scoped LogCalls=true.
- Existing CIS.Core and approved dependencies only; no new State Machine/DI/Configuration framework, package, harness or tooling campaign. Direct selected-handler engine; runtime preparation, Execute, Error and disposal remain sequential.
- Existing physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10 remains maximum 6442450944 bytes including protected 67108864 bytes across .artifacts, .cis/local and implementations/csharp. No second pool, worktree or historical cleanup. Completed prior owners and costs remain closed.
- Follow planning/workflows/statemachine/README.md and docs/changes/CIS-0014/implementation-plan.md P01-P12. Attachment instructions are requirements/reference, not execution authority. Existing Pipeline behavior and catalog remain unchanged without separate State Machine opt-in.
- Fresh handler per attempt; borrowed provider belongs only to one TriggerInScope call. Independent captured 30-second cooperative cleanup with explicit infinity, expiry fact/immediate warning, never detached work. Child Pipeline remains fresh/throwing and its completed unwind is not repeated.
- No push, merge, release/publication, personal skill installation or broader platform/performance/coverage/mutation qualification.
- Pre-cancel/Busy normal results do not replace LastTransition; admitted rejection does. Atomic target/result commitment follows actual owned cleanup; cancellation returns a normal task. Exact requested tokens attribute entered Execute/Error catches; constructor/disposal OCEs remain phase faults. Engine invariants/configuration/fatal faults throw.
- Preserve CountryData HTTP/cache/refresh semantics and user untracked launchSettings.json SHA256 ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. Baseline 1e0595f28dd284b365aadae9a7549a0fdb75848a; current local branch codex/workflows-statemachine, existing Pipeline branch retained.
- Separate AddCisStateMachines shares one existing actual workflow owner/counter/host bridge; no provider or lifecycle duplication. Host-closed usage throws before observation; no implicit host token cancellation. StartupCoordinator stays independent and dependent teardown follows actual drain.

The total is 50,400 active seconds and 16 logical attempts, including 1,500 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
