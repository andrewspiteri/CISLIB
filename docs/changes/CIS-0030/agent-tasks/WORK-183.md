---
title: "WORK-183 Deliver native predeployment Configuration creation and bounded assignment origins"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-183
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:0e51b6545141436984cda575edac7f31"
depends_on: ["WORK-171","WORK-176","WORK-180"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration","capability-contracts"]
authorization_scope_basis: "sha256:ab8ffed169ea58cb00f0de7d61d704d86d22ca02f391a51a4825be2934e3a26c"
---

# WORK-183: Deliver native predeployment Configuration creation and bounded assignment origins

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver native predeployment Configuration creation and bounded assignment origins

## Acceptance criteria

- [x] Implement the approved R08 genuine application-scoped name-plus-JSON creation workflow through the existing Management Configuration authority/store, with explicit native capability, a server-defined generic JSON structural contract rather than inferred trusted schemas, zero-registration support, immutable identity/schema reconciliation and separate original creation/publication/release/assignment facts. Reuse original Transactions/Operations/Audit and exact resource reauthorization, no browser draft success or CatalogApply bypass. Provide bounded authorized default-assignment origin metadata without exposing protected values, distinguishing shared/application override/application-only/unassigned/mixed. Preserve optional backend compatibility and existing omitted-query/wire behavior. No Scheduler/new package/pin/second framework.

## Targeted validation

- [x] Include exact CSharp/TypeScript wire/client tests, schema/OpenAPI/conformance changes where applicable, real PostgreSQL authority/duplicate/concurrent/original-replay/grant/rollback/quota/catalog-reconciliation tests and service discovery bounds, focused formatting/build, actual provider proof and independent review. Creation must really persist before first registration, never infer owner adoption, and preserve audit/effect counts. Document new consumer/operator contract and proof limits; share assigned outcome totals.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| R08 scoped acceptance | Passed; prior failures retained | .cis/local/testing/rm09-completion/r08-accepted-evidence.json; actual158source/4built snapshot, focused native/pure/build checks and independent source reviews. No Scheduler, release or full-suite qualification claim. |

| Final isolated native checkpoint | R08 and existing authoring each1passed/0failed/0skipped; guards and parentSQL/Audit passed | Latest candidate retained in r08-final-isolated-inputs.json. 15provider plus separate1Auditrollback passed. Earliercombinedbatches and unknownNodefastfails retained. Old181/168rollout/adoption acceptance remainsopen; no globalgreen claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3214; failed=374; possibleTokenSavings=591; ledgerDigest=sha256:6585f42cda35af2c24c767217142a76fee900cbd1745146f9c41a78553bcdb27 |
