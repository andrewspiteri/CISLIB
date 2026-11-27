---
title: "WORK-200 Scope peer invalidation to the retired session"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-200
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:accae1e08fab4465a594641242b3ed05"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-200: Scope peer invalidation to the retired session

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Scope peer invalidation to the retired session

## Acceptance criteria

- [x] Correct review 85711214 P2 stale-tab invalidation. A terminal old-session notification must clear only matching session/service peers, never a newer valid session draft or its original-operation references. Use only existing nonsecret session/service associations in a closed bounded notification; omit operator identity, credentials, Configuration values and operation references. Capture the retired association before clearing local context. Preserve matching peer signout, authoritative focus checks, unavailable masking, idempotence, clock rules, owner limits and actual callback custody. Ignore legacy unscoped/malformed notifications. No new endpoint, auth framework, polling, screen, package or pin.

## Targeted validation

- [x] Use production coordinator, protected owner and receipt store for old expiry and changed-context notifications, matching-peer invalidation, newer draft/reference preservation and malformed/wrong-service boundaries. Extend the existing actual two-tab native fixture with a finite newer-session draft preservation journey while retaining real signout/operator-change/held-response/no-effect assertions. Existing fixture owners must join all work. Include pinned build, affected tests, documentation and independent review within this leaf; no push, merge, release or Ubuntu CI claim.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current review correction | Passed local qualification | [215 pure checks, both real native journeys, builds/guards/identity checks and independent review](../review-857-verification.md); failed compiler attempt and all resource history retained, Ubuntu CI separate. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3463; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:9a051be6e344945d5216df7c8bd8c236a23af37b55eaa98f342e3dd4413abb7b |
