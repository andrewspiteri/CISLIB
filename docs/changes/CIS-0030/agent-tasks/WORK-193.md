---
title: "WORK-193 Complete Configuration browser state and Instance status boundaries"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-193
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:48f3738483c44c28b32a4391b72e7eaf"
depends_on: ["WORK-192","WORK-189","WORK-180"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-193: Complete Configuration browser state and Instance status boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete Configuration browser state and Instance status boundaries

## Acceptance criteria

- [x] Use the already approved backoffice R09 and Instance status screens to prove current caller behavior before any instances exist, Shared-reference-only differences against deployed with identical App JSON, finite receipt-storage throw/readback failures before effects, real user/session loss and held late-response retirement. Reuse the already catalog-provisioned preallocated application, exact scenario-specific grants and existing Playwright/MSTest/native host. Preserve ordinary A/B isolation and original-key/ETag authority. Qualify the compatible remaining WORK-168 mixed applied/pending/rejected/restart/stale/offline/noInstances, bounded page, desired/history and session/map/delay semantics through the current layout. No new controls, normal canary wizard, production fake history, package, test harness or Scheduler implementation.

## Targeted validation

- [x] New named journeys must check the actual current UI and native calls, exact UTF-8/SHA/immutable pairs, zero enrollment for predeployment, no effects on storage/session/read-only paths, scoped original decisions and payload-free Audit, and actual callback/browser/host/provider cleanup. Join every route callback and response observer even on failure. Reuse Configuration fingerprints and existing public controls; include scoped formatting, builds, appropriate regressions, independent review and current coverage/docs in this leaf. Source work may proceed independently while shared native execution follows the diagnosis leaf.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current follow-up acceptance | Passed, with retained limits and failures | [Actual verification](../configuration-followup-verification.md): strict native2/2/4/2;395portable and131focused portal checks; scoped format/source/pin manifests; independent review found no required changes. Windows correction is finite mitigation, not exact native stack proof. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3403; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:1359ab756d29afce46ea03b59147793bab33e3dfd2a0060179d36353976ec7f1 |
