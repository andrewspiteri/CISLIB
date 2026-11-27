---
title: "WORK-005 Approved desktop inspection UI and connected behavior"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:8c8463e5c3a440758bce30594fc0f24e"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:b2121a841254ff5f31a1197b7f4b995161dc36c7e27fd6b8e17ede20d750005b"
---

# WORK-005: Approved desktop inspection UI and connected behavior

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Approved desktop inspection UI and connected behavior

## Acceptance criteria

- [x] Backoffice: implement approved R06 Applications filters, selected-app Instances/Configuration/Capabilities/Scheduled jobs, read-only quick panels and Job Overview/local Configuration. Keep app/env/registration/report/scoped identities and reported versions; truthful Not reported/unknown/unavailable states. Run history and Task/Event APIs unavailable until RM10; no synthetic production records or inferred cron/bindings. Abort stale reads, clear denied session state, preserve keyboard/Escape/focus/context. Include runnable docs/tests/review; no new journeys. The typed portal-model/API routes and SDK contracts are now frozen handoffs, so UI source may proceed independently after WORK001. Connected acceptance and completion still require WORK002/003/004 completed with current native evidence; no backend/session check is waived.

## Targeted validation

- [x] Focused UI and existing connected native/provider tests prove bounded paging, per-registration versions, definition metadata only, inventory identity, target switching/cancellation/stale response, denial/session expiry and panel keyboard/focus behavior. Actual desktop screenshots compared with approved R06; material new journey requires screenshot review. Build affected clients/portal, record proof limits and native costs; no broad qualification campaign. Before completion, verify native WORK002/003/004 are Complete and reference their actual current focused evidence, then validate the connected UI. Source work may run in parallel against the frozen DTO/API handoff.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native prerequisites | Complete before connected execution | WORK-002/003/004 completed through normal CIS transitions before rm09-native-desktop-connected-proof; their accepted scoped results remain distinct. |
| Production build and source review | Passed | Final approved-layout Next production build/strict types and focused browser-test syntax check; independent source review resolved focus-return lifecycle and preserved data/authority boundaries. |
| Actual installed-Edge desktop | Passed | rm09-native-desktop-approved-layout: 1 MSTest passed in41s, 0 failures/skips. 10 actual native browser scenarios and3 explicitly controlled delay/expiry/paging scenarios; common26 host/session scenarios also completed with real native cleanup. |
| Fresh visual comparison | Passed | 14 fresh screenshots; compact approved Job Overview grouping, Clear filters, shared panels/context/readability reviewed with no required visual findings. Related links fully visible in ordinary1600x1000 viewport. .cis/local/testing/rm09/desktop-visual-review.md. |
| Integrity and reproducibility | Retained | .cis/local/testing/rm09/desktop-verified-evidence.json binds final browser/host reports and screenshots by SHA256. Original source, protected user launch settings and approved renderer remain byte-identical. |
| Scope and limits | Preserved | Current desktop inspection increment only. Future history/Task/Event backends, writes/publication/assignment, mobile, live Entra/deployment, other OS lane and release qualification remain outside this delivery. No push or merge. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2734; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:aa03c50c65aec759ebb903f1ccdc9748ac971300e806b9fd7a1642e2c552443a |
