---
title: "WORK-185 Deliver expanded current and retained past instance presentation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-185
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:00b555b3e9f7410aac725a02ed73269e"
depends_on: ["WORK-173"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:72fd6ed65b7baaf8857ec63520a74a7187368b9309b75583733ed200c849bca4"
---

# WORK-185: Deliver expanded current and retained past instance presentation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver expanded current and retained past instance presentation

## Acceptance criteria

- [x] Implement approved R08 expanded Current and Past instance sections from actual authorized presence facts. Keep recent and not-recently-seen unclosed observations distinct; only explicit closed retained registrations count as past. Preserve exact perregistration versions, row identity/panel/Technical details and actual loaded-page counts with the existing bounded pager; no inferred process death, deduplication or full-fleet/history claim.

## Targeted validation

- [x] Pure partition/paging/unknown-state cases, existing native fixture seeded actual active/stale/closed records and browser inspection/focus/selected-tab tests, scoped build/docs and independent review. Keep implementation/tests/docs coherent, pin and lifetime contracts unchanged.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| R08 scoped acceptance | Passed; prior failures retained | .cis/local/testing/rm09-completion/r08-accepted-evidence.json; actual158source/4built snapshot, focused native/pure/build checks and independent source reviews. No Scheduler, release or full-suite qualification claim. |

| Final isolated native checkpoint | R08 and existing authoring each1passed/0failed/0skipped; guards and parentSQL/Audit passed | Latest candidate retained in r08-final-isolated-inputs.json. 15provider plus separate1Auditrollback passed. Earliercombinedbatches and unknownNodefastfails retained. Old181/168rollout/adoption acceptance remainsopen; no globalgreen claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3216; failed=374; possibleTokenSavings=591; ledgerDigest=sha256:55478e319e97c91f974b8b846667c4f770a3b8f98ead6a2db5ee101c4e97c04d |
