---
title: "WORK-003 Move concrete Heartbeat lifecycle wiring from startup coordination into hosting"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:6379f681fd8549ad8968e5eaae1a2bbf"
depends_on: ["WORK-001","WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0"
authorization_scope_ids: ["startup"]
authorization_scope_basis: "sha256:104bba5c3a57048c726589173933e59ceb6c67e313fdad8915953a164cf3d4f6"
---

# WORK-003: Move concrete Heartbeat lifecycle wiring from startup coordination into hosting

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Move concrete Heartbeat lifecycle wiring from startup coordination into hosting

## Acceptance criteria

- [x] Follow the user's explicit same-module refactor: StartupCoordinator has no concrete Heartbeat reference, field, constructor dependency, resolution or lifecycle calls. Hosting owns optional Heartbeat activation, cancellation registration, cutoff and actual drain. Preserve one retained sequential startup operation and terminal outcome, complete construction before invocation, activation acknowledgement before readiness, failure health reporting, cancellation during activation, no premature/reopened dispatch/readiness and actual startup/HB settlement before dependency disposal. Use the smallest internal completion connection if needed to keep the final transition in the existing retained operation; no new public lifecycle/DI framework. Preserve optional-module absence, current APIs, source bytes, prior results/owners and the user-owned untracked example launch settings. Include focused meaningful regression/dependency-boundary tests, usage documentation and independent review in this task.

## Targeted validation

- [x] Build affected native projects and execute focused Startup core/hosting and affected Heartbeat tests, real host readiness/cancellation/drain cases, a structural dependency guard, and the existing runnable source story as justified. Move activation-specific tests to exercise the actual hosting boundary rather than synthetic duplicated wiring. Preserve ordinary PerformanceObservation/ExtendedStress exclusions. Inspect original-task history, unchanged runtime dependencies/public surface and source hash; record actual counts, failures, costs and safe settlement within the same shared allocation. Serialize shared builds; no toolchain upgrade, new test harness, release campaign, publication or default-branch merge.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Coordinator independence and retained completion | Complete; no concrete Heartbeat reference or lifecycle operation | StartupCoordinator.cs; StartupCompletionTests; compiled StartupArchitectureTests guard and six negative controls |
| Concrete hosting ownership and preserved behavior | Complete; 180 unit and 25 real-host integration cases passed | startup-host-boundary-unit / startup-host-boundary-integration settled native receipts and TRX reports |
| Architecture and runnable source consumer | 341 architecture and 11 BusinessCases passed; zero failures/skips across 557 cases | startup-host-boundary-architecture / startup-host-boundary-example receipts; verification.md follow-up section |
| Documentation and independent review | Updated five module/planning records; no required production/test/authority/history defect found | docs/modules/startup; docs/modules/heartbeat/README.md; planning/startup; independent reviews recorded in verification.md |
| Authority, cost, storage and source identity | Existing outcome; 2636486 ms native charge, one logical attempt; no reservations/recovery; sampled storage 5405598970 B | work-allocation-refactor.json; canonical work status; verification.md identity/accounting/storage section |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=829; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:84132d8db3e3eb614f7c1d6711d67a3eca8d2a17e1f21ad44f05b28f347b733e |
