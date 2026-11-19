---
title: "WORK-005 Emit cleanup-budget expiry diagnostics while handlers remain active"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:00f6fcf2ba1c43ceb5d75ad6d23aa551"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0013:outcome:9c519b131ea94ffd9a16e9ad0e8c1eaa"
authorization_scope_ids: ["instrumentation"]
authorization_scope_basis: "sha256:4605486f39afd1fdd24459c989839004dc54f3a588a1662545b82927376ce2a8"
---

# WORK-005: Emit cleanup-budget expiry diagnostics while handlers remain active

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Emit cleanup-budget expiry diagnostics while handlers remain active

## Acceptance criteria

- [x] Correct the confirmed specification section9 / P06-P08 logging defect: emit exactly one cleanup-budget expiry observation at the actual expiry outside the report gate, while an uncooperative Error remains active. Preserve independent cancellation, all handlers/actual cleanup, prior outcome, metrics and LogCalls isolation. Include the existing held-budget regression and guidance/evidence.

## Targeted validation

- [x] Extend the existing manual-clock held-handler case to observe ExpiredCount==1 before release and exactly1 afterward. Run affected execution/instrumentation/collector tests plus current public/package consumption after rebuilding; use existing projects/fixtures and scoped formatter only.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Expiry-warning correction | Passed:64 unit and19 integration/package cases | Existing held-budget case observes one expiry event before release and one after; actual handlers/cleanup still awaited; final solution build zero warnings/errors. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1638; failed=238; possibleTokenSavings=591; ledgerDigest=sha256:01431db1ad42201a9148ba701a75e1f3135c9ac8f04457458be74876ba962123 |
