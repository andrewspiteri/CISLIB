---
title: "WORK-006 Implement durable retry, terminal rejection and authorized redrive"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:9f36bcbb537c4517b4e820503c99b86f"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["dispositions"]
authorization_scope_basis: "sha256:3c17909eae9120457f6d44cc88e7c00a464850302b92af85626d50a99466a3c2"
---

# WORK-006: Implement durable retry, terminal rejection and authorized redrive

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement durable retry, terminal rejection and authorized redrive

## Acceptance criteria

- [x] P12-P14 and A07/A08/A10/A12: fresh postrollbackexpectedstate arbitration, one5ordinal targetedchain andcurrentretryserviceauthority; retainedprovenance/scope exactmatch, noimpersonation. AtomicTerminal+outbox; separateinvalidrejection/canonicalprotection; explicitbounded authorizedinspection/maintenance/redrive generation/horizon. Source may start after completed WORK002 against the frozen WORK005 failed-delivery/disposition seam. Final completion requires completed WORK004/005 and actual integrated broker dispatch; this source sequencing does not claim PartA delivery.

## Targeted validation

- [x] Real retry/terminalcommit/crash/publication/oldordinal/concurrentsuccessgap races; businessrejectiondistinct, schemadenial/contentconflict/exhaustion and malformed safequarantine. Redrive preservesidentity/protection andpendingreferences, max256bounds, noOperationsmaintenance. Docs/style/build and independentstate/recoveryreview. Final acceptance remains gated on completed WORK004/005 and actual integrated dispatcher handoffs; no replacement worker or security framework.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Bounded acceptance | 425 affected units each actual Windows/Ubuntu and current real-provider component cases; exact candidate custody retained | [Review](../review-006.md), `.artifacts/messaging/CIS-0019/WORK-006/part-a-component-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2026; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:4926096c727f04a30e281e053ebaba98e46048d1352304bee2df8f36e1aecd7f |
