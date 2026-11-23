---
title: "WORK-003 Implement mandatory managed bootstrap freshness and reconciliation"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:4922cb95e0c843ada770b64013102c86"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0025:outcome:56e9417305534bad8bc8776094f659b8"
authorization_scope_ids: ["managed","records"]
authorization_scope_basis: "sha256:28f3f2d3fd5e0e2bfa7ba879820b5f0c7a6ca4bc44c00b686b848df782b1b84a"
---

# WORK-003: Implement mandatory managed bootstrap freshness and reconciliation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement mandatory managed bootstrap freshness and reconciliation

## Acceptance criteria

- [x] Closed-from-construction gate, sequential mandatory bootstrap, same nonoverlapping Heartbeat controls/reporting, request-start freshness and first-failure closure, fenced reconciliation/recovery and joined stop. Passive client unaffected. Shared ports frozen on disk before parallel source; integrated native verification waits for matching implementations.

## Targeted validation

- [x] Focused bounded CommonTesting lifecycle/gate/clock/late-response/cleanup tests plus actual-host integration and guide.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Managed lifecycle and ownership | Passed: mandatory startup, freshness/reconciliation/late-response tests on Windows and Ubuntu; actual CISApplication and native outage recovery | [Verification](../verification.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2352; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:a615d713cf231e701ca9a94fea387698b08dba1177b73c5bbc3a181901df882b |
