---
title: "WORK-001 Address implementation impact: Program.cs"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-001
task_type: core.legacy
task_type_version: 1.0
parent_id: none
category: implementation
complexity: medium
requirement_ids: []
impact_ids: ["IMPACT-C3273A4304"]
depends_on: []
approval_gate: none
frontend_type: not-applicable
targets: []
decision_ids: []
instance_key: "ad-hoc-impact:IMPACT-C3273A4304"
work_owner_task_id: null
verification_obligations: [{"Id":"task-contract","Criteria":["The accepted impact IMPACT-C3273A4304 is satisfied without unrecorded scope expansion."],"Description":"Build the affected component and run its connected deterministic tests.","EvidenceRoute":"review","EvidenceExpectations":["Build the affected component and run its connected deterministic tests."],"SharedGateIds":[],"BasisReferences":[]}]
ad_hoc_impact_id: "IMPACT-C3273A4304"
ad_hoc_impact_basis: "sha256:2a39d485646d4c7a6f95b7cb6891f3d0fbb7a1e5f375a1e68c7e87027d8092ba"
ad_hoc_task_path: "agent-tasks/WORK-001.md"
ad_hoc_acceptance_criteria: "The accepted impact IMPACT-C3273A4304 is satisfied without unrecorded scope expansion."
ad_hoc_validation: "Build the affected component and run its connected deterministic tests."
authority: human-approved-plan
---

# WORK-001: Address implementation impact: Program.cs

## Objective

Complete the existing ConsoleHuman Logging application using the current public
API, then verify that same example as an isolated consumer of the exact CIS.Core
package on Windows and Ubuntu. Keep implementation, focused tests, documentation,
review and completion within this one medium task.

## Required changes

- Address the accepted implementation impact `IMPACT-C3273A4304`: Program.cs.
- Keep the work bounded to `cislib::source-file::implementations/csharp/examples/logging/ConsoleHuman/Program.cs` and the reviewed change outcome: An application developer runs the existing ConsoleHuman example from source or the exact CIS.Core development package, performs an asynchronous contextual operation with real human or JSONL/file records, observes truthful write/flush/stop and actual completion outcomes, and verifies ordinary build/folder-publish behavior on pinned Windows and Ubuntu without reopening CIS-0001 or CIS-0002.
- Replace configuration-only output with a small application-owned CoreLogRuntime and logger. Perform meaningful awaited work inside a scope opened and disposed in its owning method; write useful named records and typed operation/context values. Preserve default settings, environment overrides and the 64 KiB maximum-entry default.
- Use existing TryCreate, write, FlushAsync, StopAsync, DisposeAsync and Completion APIs. Print admission observations without claiming delivery. Observe flush/stop completion, success and loss; observe actual worker/resource completion separately. Keep a failed or timed-out stop unsuccessful even if cleanup later finishes. Report startup/application/shutdown failures directly and return a useful nonzero exit, without trying to log a logging failure recursively.
- Exercise the actual example source and project in disposable clean package-consumer copies. Replace its Core ProjectReference with an ordinary exact-version PackageReference, retain its real assembly identity and ordinary SDK defaults, and supply explicit net10.0/compiler/pinned-runtime properties. Use visible local-only NuGet configuration, an isolated package cache, and repository import boundaries; no source target import, Type Manager opt-in, cached Core or old assets may mask first restore.
- Reuse the available unchanged candidate with an exact hash check across related checks and platforms. Build/pack only if shipped inputs actually change or the candidate is unavailable; example-only changes do not require rebuilding unchanged production binaries. Compare the consumer's Core with the package contents, rather than equating cross-build metadata differences with a product defect.
- Reuse or minimally extend CandidatePackageFixture and existing bounded ProcessRunner/TemporaryDirectory support where useful; a small example-specific fixture is acceptable when simpler. Keep the existing Logging probe protocol and behavior assertions intact. Add only example-specific native assertions for build/published human and JSONL/file output, operation/structured values, normal lifecycle observations and actual completion. One cheap startup-failure check covers the new example error/exit path; add no pressure, rotation or shutdown-fault matrix.
- Update the existing ConsoleHuman README with source/package setup, ordinary build/run/folder-publish commands, environment configuration, expected output and runtime/resource ownership. Clarify that writer-buffer flush is not durable storage or collector acknowledgment, DisposeAsync joins bounded stop, and Completion observes actual cleanup without a timeout.
- Configure a separate scoped CIS unit profile, inspect its selection before executing, run actual supported Windows/Ubuntu checks, retain compact current/per-platform evidence, clean temporary consumers/caches/source build copies, obtain focused independent review and close accounting/lifecycle through existing controls. Commit and push the development branch.

## Required outputs

- The bounded change and reproducible evidence for the accepted impact.
- Maintained ConsoleHuman source/README, focused native package-consumer tests and a compact completion record stating actual outcomes, reused candidate/results, review, work settlement and any remaining acceptance item.

## Constraints and exclusions

- Do not expand the accepted change/impact scope or bypass product, plan, design, allocation or completion authority.
- Preserve the single CIS.Core package, public APIs, approved dependencies, environment semantics, Logging defaults and runtime ownership. Change production code only for a concrete in-scope defect; do not replace working runtime behavior.
- Preserve completed CIS-0001/WORK-001 and CIS-0002/WORK-001, their reports/current state, profiles, approvals, allocation, costs and used attempts. Their capacity does not authorize this new task. Do not rerun their accepted tests.
- Keep existing native C# MSTest projects and process/disposable-directory support. Add no logging abstraction, orchestration framework, generalized package runner, evidence archive, replay receipt or separate accounting system.
- Scope is actual Windows 11 x64 and Ubuntu 24.04 x64, SDK 10.0.401 and NET/ASP.NET Core 10.0.12, framework-dependent untrimmed net10.0, ordinary build and folder publish. No main merge, package publication, Healthcheck example integration, full regression, coverage, mutation, performance, release campaign or new module.
- Keep the packed root README and unchanged shipped inputs frozen for candidate reuse. Add no borrowed custom writer or application-owned resource that is disposed beneath active I/O; built-in runtime-owned resources stay alive until actual completion, and borrowed console streams are not disposed by the application.

## Context and evidence

- Accepted impact `IMPACT-C3273A4304`: `cislib::source-file::implementations/csharp/examples/logging/ConsoleHuman/Program.cs`.
- Impact evidence: implementations/csharp/examples/logging/ConsoleHuman/Program.cs:file=implementations/csharp/examples/logging/ConsoleHuman/Program.cs
- Andrew Spiteri explicitly accepted the exact impact and rationale from proposal commit `9c57f0b2aba8aacdc3bc87e78ef98373beb78883`; native CIS recorded acceptance before building this plan. This is not plan approval or execution allocation.
- [Reviewed scope and finite allocation proposal](../proposal.md). ConsoleHuman currently prints metadata only. Existing CandidateLoggingProcessTests and LoggingBusinessStories cover established behavior using different probe applications; they do not verify this example. Existing runtime APIs already support the intended integration, and no production defect has been established.
- This is medium scope because one application flow composes startup, asynchronous contextual logging, truthful lifecycle/resource observations and clean package/deployment checks on two actual OS lanes. Use the simplest direct application and focused native fixture, not a new framework.
- Candidate `CIS.Core.0.1.0-dev.1.nupkg`, 237,842 bytes, SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`, is retained under `.cis/local/testing/type-manager-package-integration/candidate/`. Bounded Git comparisons confirm shipped inputs unchanged through baseline `385b8855`. Reuse it while those inputs remain unchanged; do not confuse current build-identity differences with a need to repack.

## Dependencies and approval gates

- No task dependencies.
- The current plan requires actual approval; execution requires an eligible task and applicable finite work/storage authority.
- The exact plan/task and the separate allocation/controller decision remain pending. Apply allocation only after plan approval, with the current revision token and unchanged reviewed limits/roots/reasons. Reuse accepted product and no-UI decisions; no foundational approval is reopened.
- The [plan's allocation section](../plan.md#proposed-allocation-and-controller) proposes one shared 3,600-second/three-attempt allowance with 180 seconds included restoration at change and fixed task levels, the existing six GiB shared storage ceiling/64 MiB reserve and fifteen roots, and the bounded external Ubuntu limitation. No native execution is authorized by this draft.

## Acceptance criteria

- [ ] The accepted impact IMPACT-C3273A4304 is satisfied without unrecorded scope expansion.
- [ ] The example creates its owned runtime/logger, performs meaningful awaited work in a correctly owned scope, and emits useful real records with typed values and operation/context; defaults and environment override semantics remain intact.
- [ ] Write summaries describe admission honestly. Flush and stop results are inspected separately from actual Completion; cleanup is observed, scopes are disposed by their creator, runtime-owned resources and borrowed console streams follow the existing contract, and failure produces clear diagnostics/nonzero exit.
- [ ] Clean isolated first restore/build uses the exact candidate's ordinary PackageReference with no Core source reference, Type Manager SDK, inherited imports, warm Core cache or stale assets; deployed Core matches the intended package.
- [ ] Build and normal folder-published output run the actual example, showing human logs and JSONL/file records with expected operation/structured values plus successful normal flush/shutdown and actual cleanup completion. A focused startup-failure case verifies the new failure exit path.
- [ ] Actual Windows and Ubuntu execution uses the pinned toolchain and identical package. Unavailable execution remains open rather than inferred. Unchanged accepted work is reused without replay.
- [ ] The existing README contains working source/package commands, configuration choices, expected output and the precise ownership/shutdown explanation.
- [ ] Focused independent review has no required finding, necessary current evidence remains, disposable consumers/remote copies are removed, native time/storage is settled, the logical attempt is closed and supported task lifecycle is Complete. Record any actual unmet item without a broader qualification claim.

## Targeted validation

- [ ] Build the affected component and run its connected deterministic tests.
- [ ] Before execution, record plan/allocation decisions through existing controls, transition the new task to InProgress and inspect its legitimate remaining time/attempts/storage. Do not use either completed owner's capacity.
- [ ] Build only the affected IntegrationTests project and connected dependencies. Inspect the scoped profile's nonempty exact test selection first; clear the older fixture's 16 KiB test override when checking the example's 64 KiB default.
- [ ] Run the new example-specific native checks on actual Windows, reusing the unchanged candidate. Reuse the same package on actual Ubuntu with bounded SSH, actual elapsed/output and manual two GiB task-directory inspection; do not duplicate/reset the authoritative ledger or claim remote CIS containment/storage enforcement.
- [ ] Check build output and ordinary folder-publish output using matching restore/framework/runtime settings and actual process exit/log/lifecycle observations. Reuse established logger-semantic coverage; broaden only for a concrete remaining risk or in-scope defect.
- [ ] Record compact native reports, candidate/source identity, separate platform outcomes, exact limitations and independent review in this task and ../verification.md. Update documentation before read-only plan/status/verification so ordinary reuse is checked without launching unchanged tests again.
- [ ] Validate affected documentation and the approved plan, inspect the actual diff and required findings, close the logical attempt and complete the task through supported CIS controls. Keep any dossier-wide template limitation separate from actual scoped evidence; invent no approval or feature metadata.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Accepted impact | No execution yet | Not run | Record actual scoped evidence before completion. |

## Deferrals and residual risk

- No deferral is approved by generation. Record any actual unmet criterion, owner, follow-up and decision.
- Ubuntu's previously checked pinned installation must be verified again when executing this task; availability or cross-publishing proves no OS pass. Its externally supervised time/storage checks are not cross-host CIS enforcement and remain inside the same finite allowance.
- This example is not volume-sensitive work: a few conventional records demonstrate the existing lifecycle/package path. No capacity, load, performance or release qualification is claimed. Broader recorded obligations and excluded profiles retain their separate authorization requirements.
