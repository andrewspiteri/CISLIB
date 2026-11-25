---
title: "WORK-002 Native independently validated human operator authority"
type: agent-task
status: Draft
task_status: InProgress
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

- [ ] Implement application-local independently validated human/delegated JWT identity through current Security extension points, strict fixed issuer/tenant/audience/client/delegated scope and trusted resource grants. Preserve workload Entra strictness, native listener isolation and distinct human/calling-application attribution. Include current signed-token/native fixtures and documentation; no copied actor headers or new auth framework.

## Targeted validation

- [ ] Meaningful affected unit/native HTTPS signed-token tests prove correct user projection plus wrong issuer/audience/client/scope/app token/forged identity/resource denial. Format changed C# and build affected projects. Controlled issuer is explicitly not live Entra proof; independent review checks responsibility growth.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
