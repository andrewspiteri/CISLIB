---
title: "WORK-005 Deliver bounded PEM credential reload and mTLS generation adoption"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:24d4d99871d0475fae553efa4bc8a5ca"
depends_on: ["WORK-002","WORK-004","WORK-008"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["certificate-renewal","rest-binding"]
authorization_scope_basis: "sha256:e9be967e16dcde55aea6d4034f1a2448b20e5529040053f61ee9ce0d1687c754"
---

# WORK-005: Deliver bounded PEM credential reload and mTLS generation adoption

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver bounded PEM credential reload and mTLS generation adoption

## Acceptance criteria

- [ ] P07/P09-P13: PEM-only bounded source validates consistent material/local identity, uses existing Heartbeat30second coalesced checks, atomically adopts within two native generations, retains valid old owners and cuts off on stopping. Publish safe status/transition metrics and pinned cert-manager recipe without claiming AC-24 deployment. Explicit RequiredForReadiness defaults false; essential unavailability uses existing Healthcheck without changing liveness or receiver-only role validation. Retain immutable once-only server trust initialization before the first credential generation; loading trust and key files remains sequential Startup work, with no live trust reconfiguration.

## Targeted validation

- [ ] Native file/TLS tests cover real new connection after adoption, mismatch/malformed/wrong identity/expired rejection, held responses/third changes/deferred expiry, concurrent leases/retry delay, source cancellation and actual drain. Check finite repeated diagnostics and memory-only gauges; record AC-24 prerequisites separately. Include credential/recipe docs/build/format/review. Verify sends/adoption cannot construct material before initial trust is explicitly initialized and a second initialization is refused.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
