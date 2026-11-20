---
title: "WORK-001 Deliver trusted Core caller and capability integration with explicit host stages"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a73a80d0fc5842c88a05df5a921a3f5c"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["core"]
authorization_scope_basis: "sha256:f9ecb83fe8d456cf6b99fc25092837cde1bdc195927da1f3f84d2601ec7aed23"
---

# WORK-001: Deliver trusted Core caller and capability integration with explicit host stages

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver trusted Core caller and capability integration with explicit host stages

## Acceptance criteria

- [x] P01-P05/P11-P13: immutable policy and scoped trusted caller, complete-grant native endpoint/imperative parity, explicit facade order and role validation; portable fixtures, audit contract and three bounded instrument definitions preserve identity, native requirements, cancellation settlement and old hosts. Publish shared REST binding and typed security failure signatures before parallel provider handoff. Registration and constructors do no credential I/O or premature resolution; existing sequential Startup owns activation.

## Targeted validation

- [x] Focused Core/native-host tests cover grant cross-products, multiple capabilities plus independent native denial, spoofing and issuer qualification, caller restoration/background isolation, replacement snapshots, receiver-only roles and actual facade health/readiness order. Scoped formatting, bounded catalog tests and contract/guide review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Core complete-grant/security tests | Passed71;0failed/skipped | .artifacts/security/CIS-0016/WORK-001/09-core-final/results/security-unit.trx; includes late-policy replacement, extra native requirements and stale HTTP evaluation regressions. |
| Actual facade and bounded collector composition | Passed12;0failed/skipped |09-core-final/results/security-integration.trx; real Kestrel ordering/projection, readiness/health and collector registration orders. Core fake native scheme is not JWT/TLS proof. |
| Affected existing behavior | Passed92;0failed/skipped |08-core-verification/results/affected-regression.trx; unchanged application/REST/Pipeline/StateMachine collector paths. Retains that candidate's exact scope. |
| Build and style | Passed |09-core-final Unit and Integration Release builds0warnings/errors; scoped pinned whitespace format+verify pass. |
| Contracts/docs/ownership | Reviewed | Portable v1 fixtures, immutable snapshots/scoped authority, shared additive REST failure signatures, explicit public provider bridge, guides and conditional metrics. No new Core dependency or friend access. |
| Authority/resources | Within approved finite totals | Independent source estimates and all native receipts retained in .cis/local/security-intake/resources.json; user launch-settings hash preserved and physical reserve retained. See verification.md for final totals and external acceptance gaps. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1749; failed=251; possibleTokenSavings=591; ledgerDigest=sha256:ef032bb7f42657235aac592f324774ca6a8ac99565fe7fdf1dc9c8ac46c362e2 |