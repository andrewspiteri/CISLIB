---
title: "WORK-002 Deliver optional bounded CommonTesting helpers and promote the single manual clock"
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
instance_key: "outcome:3756d40061de47188032ae1efed91e95"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0012:outcome:0c9a2fe77bba4a18a1c212c9197d54f2"
authorization_scope_ids: ["common-testing"]
authorization_scope_basis: "sha256:229e99956fbef78fe9a246086a3c4641e7a3962d9de2023f35853bd7922994da"
---

# WORK-002: Deliver optional bounded CommonTesting helpers and promote the single manual clock

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver optional bounded CommonTesting helpers and promote the single manual clock

## Acceptance criteria

- [x] P05: small packable framework-independent Core-only CommonTesting: manual owner/service with explicit triggering and actual accepted-task publication/drain; recording public health monitors/configured snapshot stub; scripted typed/JSON terminal REST results/fault/cancel-pending; bounded structured logger; one promoted manual clock and thin compatibility forwarding type if needed. Keep process/internal probes nonpackable and no friend/reflection/test-host/policy/scheduler/runner clone. Include focused helper tests, package closure, usage/docs/review.

## Targeted validation

- [x] Public helpers handle condition/callback/error-handler/unregister/close lifetimes, cancellation only ends owner waits, trigger cancellation terminates fake operations, no new timer-driven scheduler. Recordings are capacity/clear bounded; unconfigured script fails clearly; JSON scripts can supply internal requested DTO T via normal BCL deserialization. Same clock behavior remains for existing repository tests. Current Core/CommonTesting package deployment/closure proven through existing infrastructure; final compiled CountryCatalog outsider proof is WORK-003.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native helper and package validation | Passed |140 passed,0 failed/skipped in ordinary VSTest; common-cli-results/unit.trx and common-cli.json. Actual package closure/deployment and original clock behavior verified. CIS capture failure retained separately in verification.md. Independent public/XML/package reviews completed. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1558; failed=230; possibleTokenSavings=591; ledgerDigest=sha256:8d8b18a70f65deda4970ef79f224443542f5d8f7349c11b3bf7ee42ca4e30f50 |
