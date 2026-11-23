---
title: "WORK-006 Integrate optional registration and actual Jobs inventory lifecycle"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:f683b44cf6314d4cb5b44278cc0dd3f0"
depends_on: ["WORK-001","WORK-005"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["registration-host","csharp-client"]
authorization_scope_basis: "sha256:d62cdf9f580c3fb23ebf050d2bbada8e1ed7fff345a0f639b1e066a89600d7e5"
---

# WORK-006: Integrate optional registration and actual Jobs inventory lifecycle

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate optional registration and actual Jobs inventory lifecycle

## Acceptance criteria

- [x] AC05/14: opt-in direct initial Startup registration and distinct required/optional outage policies; one existing Heartbeat owner/coalesced 30s observation+catalog and5..60s reconnect, bounded current state, trusted Jobs paged projection without handler construction. Preserve sequential credential startup and accepted dependency drain/failed-Build ownership.

## Targeted validation

- [x] Controlled public client/Heartbeat tests for initial required/optional deadlines, purged vs conflict/closed reconnect IDs, high transition coalescing, hundreds-of-jobs complete pages, cancellation/held callback actual drain and quiet diagnostics. Actual GenericHost/facade/REST-before-Heartbeat order tests, no new lifecycle/readiness framework or duplicate metrics.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Current management units | 163 passed, 0 failed, 0 skipped | `WORK-006/uncertain-inventory-current-management-units`;34 new reporter/host/Jobs/source cases include three lost inventory/fault/cancellation reversion rows. Earlier160 pass retained with its uncovered review finding. |
| Existing native hosting behavior | 56 passed, 0 failed, 0 skipped | `WORK-006/affected-native-hosting-lifetime-regressions`; Startup/Heartbeat/REST shutdown, actual facade lifecycle, failed Build ownership and aliases. |
| Existing Application/Startup units | 9 passed, 0 failed, 0 skipped | `WORK-006/affected-application-startup-unit-regressions`; unchanged by the subsequent inventory-only correction. |
| Source quality and review | Passed | Unit and Integration builds zero warnings/errors; explicit-file whitespace and verification. Independent review's material inventory-uncertainty finding fixed with one bounded flag; no new history, scheduler, coordinator dependency or metrics. Consumer guide: `docs/modules/management/registration.md`. |
| Actual facade reporter against service | Delivery acceptance under WORK-008 | Component proofs include native generic Host and passive facade/failed-construction ownership; live authenticated facade reporting remains separately owned. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2306; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:07d90283955ff4b8941d738a8c48609b8eb61efd331943edd89304cb2551bc70 |
