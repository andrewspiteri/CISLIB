---
title: "WORK-001 Approved desktop backoffice design and current-slice requirements"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: design
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:a0825dfb97b043358515b3f3ffd0a0a6"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["intake-design"]
authorization_scope_basis: "sha256:37661d32d72c195c404e90b4c2aedce890073262160eddd62ceb9b66988b10d5"
---

# WORK-001: Approved desktop backoffice design and current-slice requirements

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Approved desktop backoffice design and current-slice requirements

## Acceptance criteria

- [x] Backoffice: preserve original RM09 and all R01-R06 history/costs; approved R06 exact desktop Applications/four-tab/quick-full details design is current through normal UI-direction reconciliation. Current implementation excludes mobile/writes/RM10 runtime and explicitly retains P6/P7/P8. Protect user launch settings. All docs meet the approved current slice.

## Targeted validation

- [x] Native design validate and plan validate pass; exact PNG manifest matches approved R06; source/protected file hashes unchanged; source-aware and independent reader reviews retained. Record supported provenance-only reconciliation under actual R06 human decision.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Exact original R06 decision and unchanged pixels | Passed | Native original approval plus `r06-provenance-pixel-comparison.json`: all21 PNGs identical, same manifestff55f6da; only native-generated provenance refreshed. |
| Current UI scope/direction | Passed | Native UI postgraph status Active/valid/current; original NoUI and failed init retained in local preservation receipt/Git. |
| Native current plan/design/docs | Passed | `rm09-inspection-plan-validate-final`, `rm09-inspection-design-validate-final`, `rm09-inspection-docs-validate`: zero errors; docs historical warnings remain. |
| Original source and user launch settings | Passed, unchanged | Exact source1574ec26 and protectedea7e7219 hashes rechecked. |
| Independent plan/source/reader review | Accepted | `inspection-plan-review.md` plus retained R06 source/visual reader records; no new framework or verification campaign. |
| Shared finite authority and storage | Applied | `authorization.json`, `work-allocation.json`; exact pre-existing ceiling reference reused, old estimates and failed metadata preserved. |
| Implementation status | Not delivered by this task | WORK002–005 own production code, native tests and connected behavior; P6/P7/P8 and original deferred scenarios remain open future scope. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2669; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:8fa42a443d476548c4372e05dc16cbd5d4f5fc77026fc5b2e19906fef46506f8 |
