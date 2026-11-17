---
title: "WORK-003 Instrument actual Logging Heartbeat and Healthcheck runtime boundaries"
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
instance_key: "outcome:01003f7b5d254994a9769d56df3bc6ce"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics"]
authorization_scope_basis: "sha256:ce451dd7eb927ccdd3a6aa8473f946cddc546f9d4161af350f43a8a47ccd8c7d"
---

# WORK-003: Instrument actual Logging Heartbeat and Healthcheck runtime boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Instrument actual Logging Heartbeat and Healthcheck runtime boundaries

## Acceptance criteria

- [x] Use the frozen host-owned facade/seam to observe actual Logging admission/consumption/discard/writer facts, real Heartbeat execution duration/active/outcome and actual Healthcheck evaluation/readiness/state. Keep finite configured aliases or aggregate safe categories, no GUID/type/logger labels or invented general spool bytes/skipped waiting. Preserve constructors, results, cancellation, overwrite/readiness/scheduling behavior, and isolate metrics failures from module failure paths. Do not edit shared hosting/project/provider files; include coherent catalog/tests/docs/review.

## Targeted validation

- [x] One real normal/failure path per changed runtime module emits automatically when enabled and behavior stays unchanged when disabled; held work/owner behavior and real snapshot states are preserved. Existing native Unit/Integration/Business and affected compatibility/architecture checks use actual service entry points, not test-only intercepted counters. Logging stage boundaries remain distinct; HC scraping does not cause evaluation and HB capacity wait is not a skip.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native runtime Unit | 22 passed,0 failed/skipped | metrics-runtime-unit TRX/state; actual Logging, Heartbeat and Healthcheck entry points. |
| Independent review | No remaining blocking finding | C/D paired review; dead weak handles are ignored without sweep/retirement side effects and regression passed. |
| Accounting | 215710 inclusive measured native ms; one logical attempt, no reservation/hold | native/incremental/580b500c22a241498b5f68f1a68432fd |

Automatic optional-host source attachment remains WORK-005 scope.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1083; failed=159; possibleTokenSavings=123; ledgerDigest=sha256:c99302015d589fad92d2be1411888edf48e0420bbde1dd051f64c9545dbde885 |
