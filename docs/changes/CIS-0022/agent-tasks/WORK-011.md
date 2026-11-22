---
title: "WORK-011 Complete bounded coordination and jobs observations with native exporter integration"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-011
parent_id: WORK-006
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-006"
instance_key: "outcome:d922438a95ef4de88949900eb03ae7ff"
depends_on: ["WORK-001","WORK-003","WORK-004","WORK-005"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["hosting-observations"]
authorization_scope_basis: "sha256:a8c80fbb6b6c75e20535a07dc7d13a52fc2e72788769ac0ae9a134f52faf646e"
---

# WORK-011: Complete bounded coordination and jobs observations with native exporter integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete bounded coordination and jobs observations with native exporter integration

## Acceptance criteria

- [x] Complete cached truthful health, bounded quiet transitions and seven fixed-cardinality Metrics profiles using actual physical operation/callback boundaries, terminal loss and rejected configuration observations. Include optional collector lifecycle and actual HTTP export tests and docs; retain callback settlement before health/Logging/collector teardown.

## Targeted validation

- [x] Current runtime and jobs units including callback cancellation and physical settlement, meaningful health/logging failures and no histories, fixed catalog budget, actual native Prometheus HTTP scrape and cumulative observations; existing Metrics and scoped formatting checks. Share WORK006 funding and evidence.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current pinned builds and scoped formatting | Passed | SDK10.0.401/runtime10.0.12, zero warnings/errors; see verification.md and retained hosting-native-proof.json. |
| Windows affected unit + actual native integration | Passed | 533/533 units and215/215 integration, zero failed/skipped; includes real HTTP scrape and actual failed Build. |
| Actual Linux and native credential ownership | Passed | 172/172 Linux units and7/7 existing Windows credential-owner regressions, zero failed/skipped. |
| Source, ownership and consumer documentation review | Complete | Known-module sequential host/barrier, original StartupCoordinator independent, bounded cached observations, entry captures; docs/modules/coordination and application/core.md reviewed. No cloud/failover/Linux SQL qualification claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2212; failed=301; possibleTokenSavings=591; ledgerDigest=sha256:e8054a899e8fe22ff4db0edfb5ae104c1ca9e0f80cbefabb8d4756c182cd8a85 |
