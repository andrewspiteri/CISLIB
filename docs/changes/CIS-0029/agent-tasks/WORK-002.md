---
title: "WORK-002 Native independently validated human operator authority"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:59faadf688ef4873a9a9964a04da7b39"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["management-prerequisites"]
authorization_scope_basis: "sha256:9d34e1d9a49e8b8eeb74b7fa8b4652642da6a2b59cfc20058bfce3c6f1c2e589"
---

# WORK-002: Native independently validated human operator authority

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Native independently validated human operator authority

## Acceptance criteria

- [x] Implement application-local independently validated human/delegated JWT identity through current Security extension points, strict fixed issuer/tenant/audience/client/delegated scope and trusted resource grants. Preserve workload Entra strictness, native listener isolation and distinct human/calling-application attribution. Include current signed-token/native fixtures and documentation; no copied actor headers or new auth framework.

## Targeted validation

- [x] Meaningful affected unit/native HTTPS signed-token tests prove correct user projection plus wrong issuer/audience/client/scope/app token/forged identity/resource denial. Format changed C# and build affected projects. Controlled issuer is explicitly not live Entra proof; independent review checks responsibility growth.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native build and format | Passed | Integration build zero warnings/errors; scoped changed-C# whitespace and verification passed. |
| Native identity and ownership boundaries | Passed | Combined 54-case identity/metadata/query-boundary selection; actual 5-case native operator/workload/certificate/local/Entra selection passed after fixture policy correction. See verification.md for exact phases and retained failed observations. |
| Independent source review | Resolved | .cis/local/testing/rm09/operator-independent-review.md; linked cancellation/body limit correction executed in focused tests. |
| Scope and proof limits | Preserved | Fixed application-local provider and existing native Security ownership; controlled signed-token/TLS proof is not live Entra/AKS deployment or release qualification. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2716; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:fd11df4a43f11c9e77a8e339e9d20264fca5af586b745db230f741426eacc629 |
