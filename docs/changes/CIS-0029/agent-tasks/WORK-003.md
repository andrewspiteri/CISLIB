---
title: "WORK-003 Bounded authorized portal discovery contracts and clients"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-003
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:5cd40f9465df491994f60b341db2461d"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["management-prerequisites"]
authorization_scope_basis: "sha256:9d34e1d9a49e8b8eeb74b7fa8b4652642da6a2b59cfc20058bfce3c6f1c2e589"
---

# WORK-003: Bounded authorized portal discovery contracts and clients

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Bounded authorized portal discovery contracts and clients

## Acceptance criteria

- [ ] Provide server-side bounded identity/display-name search and exact environment filtering, authorized environment enumeration and exact application/environment Configuration-definition query. Native per-resource authorization precedes page/cursor disclosure; canonical filter/actor/profile/policy cursor binding preserves original operation recovery. Use compatible optional backend query interfaces; update OpenAPI/schema/C#/TS clients together. Include tests/docs, no whole-fleet query in Next.

## Targeted validation

- [ ] Existing native/provider and portable client tests cover beyond-first-page matches, hidden apps/environments/sources, malformed/Unicode bounds, filter/policy cursor mismatch and old omitted-filter callers. Exact SQL filters before paging, bounded scan behavior and cancellation reviewed. Build affected projects, scoped C# formatting, independent responsibility review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
