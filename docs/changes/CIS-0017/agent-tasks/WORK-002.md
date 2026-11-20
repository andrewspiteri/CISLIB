---
title: "WORK-002 Complete guarded ADO.NET participation and terminal transaction lifecycle"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:82796033214c4fa6a918ec71470f0f45"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["native-lifecycle"]
authorization_scope_basis: "sha256:fdde3b3a224bece99dad93b0fea61f3e5303b7bb79f7b12a527dd57acc428059"
---

# WORK-002: Complete guarded ADO.NET participation and terminal transaction lifecycle

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete guarded ADO.NET participation and terminal transaction lifecycle

## Acceptance criteria

- [ ] P04-P08 and sourceAC05identity/08/13-21 plus relevant11/12/26: public mockable capability/extensions, one nonqueued callback, rollback-only and terminal atomic claim, exact native boundaries and Committed/NotCommitted/Unknown outcomes. Join real cancellation/disposal work, same-callback misuse detection, fresh cooperative rollback budget, ordered at-most-once cleanup and immutable safe report. No replay, resource escape guarantee, host registry or generic transaction framework.

## Targeted validation

- [ ] Deterministic provider doubles/control gates prove samehandles, rejected overlap/reentry/insidecallbackcompletion, externaldispose heldcallback/commit, late accepted mark vs completion, pre/postcallback cancellation and original fault, precommit checkpoint vs postinvoke Unknown vs confirmedcommit, independent rollback budget actualsettlement and threeordered cleanup failures. Inspect primary/cleanup separation, at-most-once calls, immutable metadata/repeatedDispose, no datasource disposal; include contracts/docs/style/review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
