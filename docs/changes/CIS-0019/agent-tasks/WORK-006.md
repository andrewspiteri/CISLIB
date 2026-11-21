---
title: "WORK-006 Implement durable retry, terminal rejection and authorized redrive"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:9f36bcbb537c4517b4e820503c99b86f"
depends_on: ["WORK-004","WORK-005"]
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

- [ ] P12-P14 and A07/A08/A10/A12: fresh postrollbackexpectedstate arbitration, one5ordinal targetedchain andcurrentretryserviceauthority; retainedprovenance/scope exactmatch, noimpersonation. AtomicTerminal+outbox; separateinvalidrejection/canonicalprotection; explicitbounded authorizedinspection/maintenance/redrive generation/horizon.

## Targeted validation

- [ ] Real retry/terminalcommit/crash/publication/oldordinal/concurrentsuccessgap races; businessrejectiondistinct, schemadenial/contentconflict/exhaustion and malformed safequarantine. Redrive preservesidentity/protection andpendingreferences, max256bounds, noOperationsmaintenance. Docs/style/build and independentstate/recoveryreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
