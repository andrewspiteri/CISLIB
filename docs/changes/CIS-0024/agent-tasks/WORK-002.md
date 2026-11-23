---
title: "WORK-002 Deliver independent authenticated service and authorized read APIs"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:72a1ec1b3ae24280806604d5fa270e6f"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["service-host"]
authorization_scope_basis: "sha256:202929a3fcbeda0fc3717a6e84bfc281afc6f587f874f44785d2e8c084b18809"
---

# WORK-002: Deliver independent authenticated service and authorized read APIs

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver independent authenticated service and authorized read APIs

## Acceptance criteria

- [x] AC01/02/06: real product CISApplication service, passive single-provider composition, configured native workload/human seams and complete target/resource authorization. Typed routes/service-info/catalog/list/detail/instances/inventory/capabilities use actual stored resources, opaque actor-bound pages, finite admission/body/response bounds and restricted operational listeners.

## Targeted validation

- [x] Native host/startup/readiness, real certificate/JWT validation/projection and invalid/unmapped/wrongenv/forged actor coverage; list/page/lookup isolation and known/unknown auth evidence. No header auth evaluator, anonymous business API or new lifecycle framework. Affected existing hosting/dependency behavior, builds and format.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual product host / resource APIs | 5 passed, 0 failed, 0 skipped | `WORK-002/complete-native-management-host-tests`; real TLS/native JWT, exact resource grants, listener isolation, actor-bound cross-replica cursors and strict request boundaries. |
| Configured workload profiles | 3 passed, 0 failed, 0 skipped | `WORK-002/actual-configured-native-profile-recheck`; LocalDevelopment/certificate mTLS and configured Entra signed validation with owned static metadata. Native gate preserved; original fixture selection failure retained. No external federation/issuance claim. |
| Pure boundaries | 4 passed, 0 failed, 0 skipped | `WORK-002/service-pure-boundary-tests`; actor-scope identity, cursor bindings and finite option capture. |
| Product quality and review | Passed | Actual service and Integration builds zero warnings/errors, explicit-file formatting and verification. Focused independent security/delivery review found no material defect. README documents explicit schema/topology provisioning and resource ownership. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2305; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7d05d3bf08b4ff87419e4475ea407318e36a55ecf5d18d04965b67f96766c85c |
