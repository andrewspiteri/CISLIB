---
title: "WORK-003 Qualify the isolated PostgreSQL three-table reference consumer"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:01b768a73a834d689650536672696456"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["postgres-consumer"]
authorization_scope_basis: "sha256:e27f2657a1d9d8a6f77e3116209ed1d140035a350ce9a179166710cd6935c408"
---

# WORK-003: Qualify the isolated PostgreSQL three-table reference consumer

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify the isolated PostgreSQL three-table reference consumer

## Acceptance criteria

- [ ] P11-P12 and sourceAC05-07/09-12/22-23/DB28: test-onlyNpgsql10.0.4 outsideCore/CommonTesting closure and one exact new mapping; one explicitlyowned localDockerPostgreSQL18 container/data volume with pinned officialamd64 digest, private credentials and counted resources. Three ordinarylogged fixture tables, independently observed allcommit/allrollback/declinedresult behavior, supported isolation and actual query cancellation. Document the compiling public consumer and narrower unexecuted EFrecipe; no productionRM03/RM04 or existingDB access.

## Targeted validation

- [ ] Confirm actual locked dependency graph and Corepackage closure, exactserver/provider versions/durability, Windows/Ubuntu native lanes actually executed. Independentconnection visibility aftercommit, laternativefailure/caughterror+mark/rollback/disposal, decline-onlyresult, allthree serverisolation values, ownedadvisoryblocker confirmedactivequery then cancellation/settlement, reusedappdatasource survivesowners. Exactowned resource restore, scope/style/docs/review; unexecutedDB/platform acceptance stays unverified.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
