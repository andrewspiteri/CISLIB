---
title: "WORK-014 Preserve declared nested defaults in structural release composition"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-014
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:443f90233a7445a7a4ab8d61ea4e4281"
depends_on: ["WORK-002","WORK-003","WORK-004"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["contracts","backend","configuration","csharp-client","typescript","consumer","records"]
authorization_scope_basis: "sha256:a61c6e69d1e0b593dcd687414078c914d891a0c8703b6b74b3ecac4907f78856"
---

# WORK-014: Preserve declared nested defaults in structural release composition

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Preserve declared nested defaults in structural release composition

## Acceptance criteria

- [x] Data-only trusted structural declarations/composition preserve existing Configuration declared-child defaults when an upper partial object follows lower null and within replacement arrays/dictionaries. Raw sources retain validation before overlay; effective complete validation and existing finite bounds remain. No consumer CLR execution on service.

## Targeted validation

- [x] Native publication and release creation for nullable child and partial collection objects plus actual Configuration acceptance produce existing mapper values. Missing/invalid default declarations remain explicit refusals; portable manifests/wire/docs remain coherent, focused negative/default/limit tests and independent review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native declared-default parity and bounds | All16 new cases passed inside101 actual provider/host cases, including native publication/release/self-snapshot plus local commit, explicit templates, immutability and expansion refusals | WORK-014/reviewed-service-provider-and-handoff/results/reviewed-service.trx |
| Independent review and compatibility | No remaining finding; original flat constructor/serialized bytes retained, templates local-only/immutable/quota-counted, guide explains nested declaration/version requirement | ../review-corrections.md; docs/modules/management/configuration-service.md; strict current parent/fixture build receipts |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2455; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:330efd5e7c3e40ce3359ce3f824df4c05856fa2bb859dd48fb787634cc0e0930 |
