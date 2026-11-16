---
title: "WORK-001 Implement once-only sequential startup with real DI construction and lifetime barriers"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:144f9c36fcbd40ac9eda8ffcebfb4ab9"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0"
authorization_scope_ids: ["startup"]
authorization_scope_basis: "sha256:104bba5c3a57048c726589173933e59ceb6c67e313fdad8915953a164cf3d4f6"
---

# WORK-001: Implement once-only sequential startup with real DI construction and lifetime barriers

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement once-only sequential startup with real DI construction and lifetime barriers

## Acceptance criteria

- [x] AC-02 through AC-09: add the single CIS.Startup.IStartable identity; enroll only ordinary unkeyed singleton interface registrations; validate final descriptors and use standard actual-root scope validation, materialize the full sequence once before any startup, reference-deduplicate preserving first occurrence, directly await actual tasks sequentially and retain one operation/first host token and success/fault/cancel without replay. Establish the existing critical condition monitor before participant construction when installed; preserve useful sync/null-task/async/unexpected-cancel failure and neutral known host cancellation. Cancellation never certifies actual settlement; final publication belongs to the same operation's stopping boundary and supports the narrow HB completion connection. Include focused real-DI controlled unit tests, usage documentation and independent review. Do not add a DI framework, second provider, scan, priority, retry, custom graph validator or generic hook registry.

## Targeted validation

- [x] Build affected Core/Unit projects and run focused meaningful Startup enrollment, empty sequence, late-constructor-zero-call, registration/dependency-order, held-task, ref/equal-by-value identity, alias/lifetime/scope-validation, concurrent/repeated terminal-entry, sync/null/async fault, host/unexpected cancellation and actual-settlement tests. Inspect operation retention before user code and no user code under control locks. Preserve existing module behavior and ordinary PerformanceObservation/ExtendedStress exclusions. Record exact native symbols/results and AC mapping, actual cost/storage and responsibility-growth review in this task. Root controller serializes shared builds. Host acceptance remains owned by the dependent integration task.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native core build and57focusedcases | Passed57, failed0, skipped0 | CIS native/incremental/fc42f87a75eb432ea2e81b4c9eb24765; .cis/local/testing/incremental/startup-core-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml |
| Independent read-only review | No blocking module defects or responsibility expansion | Scheduler, acceptance and Healthcheck reviewers; source/tests and bounded framework-validation correction reviewed |
| Scope-validation boundary | Standard DI validation tested; observed upstream mixed-instance limitation retained | docs/modules/startup/core.md and verification.md#standard-di-validation-boundary; no custom validator or dependency upgrade |
| Accounting |986537ms inclusive native charge,1of2attempts, zero reservations/recovery | Core progress/closure; shared limits unchanged |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=792; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:ccd35fc4d51e92cb6a3f544e0bc99aba772b9f3fc1323b2a22c643e4f09e33b9 |
