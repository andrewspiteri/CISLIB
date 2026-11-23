---
title: "WORK-012 Correct managed jobs activation transitions and partition health"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-012
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:6f1791b1ba094a78936d5c10866f4390"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["continuous","hosting-observations","scheduled"]
authorization_scope_basis: "sha256:8726b80be8aca83ae3eb4fb29791a3a27c97acbc6925a7ec9145e9c52bad1e4d"
---

# WORK-012: Correct managed jobs activation transitions and partition health

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Correct managed jobs activation transitions and partition health

## Acceptance criteria

- [x] Accepted disable/re-enable or mode/binding transitions remain visible to the supervisor when intermediate revisions coalesce; exactly one replacement starts after the original callback and scope settle. Ordinary completion/failure/capacity notifications do not replay the same activation.
Unresolved failed resource duties remain unhealthy while other resources run or complete successfully. Only corresponding-resource recovery supersedes the failure; retention stays bounded without a resource-history cache.
Correct PostgreSQL guide composition link; document activation/failure retention and record focused native verification and service-workflow status truthfully.

## Targeted validation

- [x] Deterministic regressions for coalesced revisions, exactly one replacement after scope settlement, no failure replay, and two-resource health retention/recovery/bounds. Affected Jobs/Coordination unit and hosting integration tests, scoped format verification and independent review.
Preserve public API/dependencies/pins and prior completed tasks/costs. No provider/tool/harness, merge or release. Workflow evidence requires actual corrected remote commit and receipt.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Coalesced transitions and atomic activation admission | Passed | Six new activation rows including Host/Category/Job transitions, stale later-target admission and initial/later cleanup failure. No automatic failure replay; actual prior callback/scope settle before replacement. |
| Resource-specific health and bounded retention | Passed | Three new health/pressure rows preserve A failure while B runs/completes, require actual A cleanup for recovery, retain failures across Busy/failed cleanup and keep unrelated jobs eligible. Original 200-definition fault-retention case passes. |
| Native affected validation | Passed | Current Windows units542/0/0, existing host/application/HTTP-metrics integration215/0/0, actual Linux units181/0/0. Pinned scoped format+verify and builds0W/E. |
| Original completion diagnostics | Preserved | First542 selection539pass/3fail retained; corrected cleanup recovery flag and reran unchanged original scheduled result/last-outcome assertions successfully. |
| Docs and independent review | Passed | Hosting/runtime/observability guides updated, PostgreSQL hosting link fixed;46local guide links resolve. Independent cross-review plus final race/cleanup review passed. |
| Limits, provenance and remote status | Recorded | review-follow-up.md, effective-review-owner-allocation.json, verification.md and ignored review-native-proof.json preserve original shared limits/costs/reserves and user settings. No push/merge/release or workflow run performed; pre-merge service workflow remains explicit. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2225; failed=304; possibleTokenSavings=591; ledgerDigest=sha256:396b4596a78284f15a8a7e07af909b6237a9d6b864270a3eb8a4ea4fbed7a02f |
