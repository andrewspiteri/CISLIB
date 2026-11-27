---
title: "WORK-184 Deliver R08 readable navigation and JSON-first Configuration workflows"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-184
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b136e179276f4409aa7e24336aaf1c82"
depends_on: ["WORK-173","WORK-179","WORK-180"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration","capability-contracts"]
authorization_scope_basis: "sha256:ab8ffed169ea58cb00f0de7d61d704d86d22ca02f391a51a4825be2934e3a26c"
---

# WORK-184: Deliver R08 readable navigation and JSON-first Configuration workflows

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver R08 readable navigation and JSON-first Configuration workflows

## Acceptance criteria

- [x] Implement exact approved R08 layout and interactions: readable app/tab/config URLs with stable native selectors and legacy links, compact assigned-layer list with accessible perrow actions and Override, actual authorized JSON detail, app-only large formatted JSON editor with Technical details last/no main source-reference controls, and real new Configuration name-plus-JSON review/create/publish using the new native sibling. Keep explicit assignment separate, same-key recovery and independent grants, exact pinned Common/Application references, unknown/redacted states, finite paging and protected draft/session/callback ownership. No automatic mutation retries, client merge authority or fake server draft.

## Targeted validation

- [x] Meaningful pure route/duplicate/rename/legacy/job-detail tests; formatter exact numeric/Unicode/member/null/overflow tests; scoped production/strict TypeScript build; actual native/browser app/tab/back/JSON/menu/override/creation/recovery/grant/keyboard proof through existing fixture and sibling backend, independent source review and updated docs. Preserve old failures and outstanding rollout/adoption acceptance; design approval is r08-user-approval.json with renderer309502759a49.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| R08 scoped acceptance | Passed; prior failures retained | .cis/local/testing/rm09-completion/r08-accepted-evidence.json; actual158source/4built snapshot, focused native/pure/build checks and independent source reviews. No Scheduler, release or full-suite qualification claim. |

| Final isolated native checkpoint | R08 and existing authoring each1passed/0failed/0skipped; guards and parentSQL/Audit passed | Latest candidate retained in r08-final-isolated-inputs.json. 15provider plus separate1Auditrollback passed. Earliercombinedbatches and unknownNodefastfails retained. Old181/168rollout/adoption acceptance remainsopen; no globalgreen claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3215; failed=374; possibleTokenSavings=591; ledgerDigest=sha256:764c7b1ee30f9869e67e0c0068338c65f0e372951c1ceb24530b0e52c7032cc8 |
