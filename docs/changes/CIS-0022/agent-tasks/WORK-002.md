---
title: "WORK-002 Implement PostgreSQL authority and transaction guard"
type: agent-task
status: Draft
task_status: Decomposed
task_id: WORK-002
parent_id: none
category: implementation
complexity: high
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:ad319765e21a41cc8e0a72eecb3ffac3"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["postgres"]
authorization_scope_basis: "sha256:12d3945bd012bfba7ea080899854a3e21c8a9bfc5cc0837ed67315edf3a8200b"
---

# WORK-002: Implement PostgreSQL authority and transaction guard

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement PostgreSQL authority and transaction guard

## Acceptance criteria

- [ ] AC01/02/03/06/07/08: explicit immutable AuthorityId/version installer, logged CACHE1 NO CYCLE sequence, first/recreation perkey upsert locking+separate currenttime, committed grants/exact observation/conditional renew-release-prune. Original RM02 alias/ReadCommitted guard locks current row, checks time afterwait and poisons rejection without hidden commit/reentrancy. Preserve common RM03/Messaging lockorder and exact outcome/cleanup facts.

## Targeted validation

- [ ] Use existing typed ownedPG, independent application sources/sessions, real contention/unrelatedkeys, stalegrant rejection, pooledgeneration/release/prune/restart, sequenceproperties/authoritymismatch, real guard-lockwait expiry and transaction/takeover serialization. Focus Windows/actualLinux, zero selectedskips, exactcleanup; provider/lockorder/migration docs and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
