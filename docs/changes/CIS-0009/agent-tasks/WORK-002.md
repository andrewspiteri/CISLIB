---
title: "WORK-002 Implement bounded OpenTelemetry collection policy and early lifecycle proof"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:c6b9898f62aa402bb3a910930b6811d0"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics"]
authorization_scope_basis: "sha256:ce451dd7eb927ccdd3a6aa8473f946cddc546f9d4161af350f43a8a47ccd8c7d"
---

# WORK-002: Implement bounded OpenTelemetry collection policy and early lifecycle proof

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement bounded OpenTelemetry collection policy and early lifecycle proof

## Acceptance criteria

- [x] Add only the optional pinned compatible SDK/exporter package and lock closure. One provider/reader, exact host Meter selection and total nonthrowing Drop for foreign/unselected instruments; one effective view combines reserved-key exclusion, allowed keys, capacity/buckets. Capture authoritative EnvironmentOptions identity and finite catalog/late native capacities, disable exemplars, preserve overflow and bounded diagnostics. Establish early actual all-sample identity and callback-admission/drain proof before broad integration; do not treat provider Dispose/HTTP timeout as settlement. Own policy tests/docs/review, no exporter/aggregation replacement.

## Targeted validation

- [x] Actual compatible locked restore/build and native A04/A06-A08 controls; parse real exporter data for authoritative native/CIS/histogram/overflow/target_info identity, resource/tag override attempts, late caps and static startup options. Verify two host-owned Meter selections and held callback after timeout/stop using existing native fixtures. Retain exact package identities, failed observations and actual costs; optional closure cannot enter Core.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Locked native collection | 31 passed,0 failed/skipped; current unit inputs, zero pending batches | metrics-collection-integration TRX/state; native fd6199e633ea4200ae005be912f159e2. |
| Actual closure | SDK/API/hosting1.21.0, exporter1.21.0-beta.1, DiagnosticSource11.0.0 net10 assets | Optional and Integration lockfiles; pinned runtime10.0.12 preserved and Core closure unchanged. |
| Failed observations retained | NU1100 mapping, then cached NU1102; cache-bypassed repair first passed31 but generated lock | Current locked-mode rerun supplied stable declared inputs; no forced reuse or version/feed change. |
| Accounting | 900973 inclusive measured native ms; one logical attempt, no reservation/hold | Four native executions include failures and justified repaired-lock verification. |

Source review found no blocking issue. Actual product reserved branch, module source attachment, callback owner service order, package/example deployment and footprint remain WORK-005 scope.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1084; failed=159; possibleTokenSavings=123; ledgerDigest=sha256:bbeb225fbc6cd78ab778f48f8cac7bbaf9472e3e0f8a60bd9f4a64b7162e07d0 |
