---
title: "WORK-003 Bounded authorized portal discovery contracts and clients"
type: agent-task
status: Draft
task_status: Complete
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

- [x] Provide server-side bounded identity/display-name search and exact environment filtering, authorized environment enumeration and exact application/environment Configuration-definition query. Native per-resource authorization precedes page/cursor disclosure; canonical filter/actor/profile/policy cursor binding preserves original operation recovery. Use compatible optional backend query interfaces; update OpenAPI/schema/C#/TS clients together. Include tests/docs, no whole-fleet query in Next.

## Targeted validation

- [x] Existing native/provider and portable client tests cover beyond-first-page matches, hidden apps/environments/sources, malformed/Unicode bounds, filter/policy cursor mismatch and old omitted-filter callers. Exact SQL filters before paging, bounded scan behavior and cancellation reviewed. Build affected projects, scoped C# formatting, independent responsibility review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native client and contract checks | Passed | Corrected198-case Core client/wire/profile selection,94-case canonical contract selection,189-case affected TypeScript SDK selection, SDK build and portable browser-import compile. |
| Actual PostgreSQL discovery | Passed | Five native cases prove pre-page filtering, hidden source/environment handling, UTF-8 continuation, actual4096-candidate refusal and cancellation draining blocked SQL before releasing its lock. |
| Independent source review | Resolved | .cis/local/testing/rm09/discovery-independent-review.md; scoped OpenAPI example and required regressions corrected. |
| Finite profile and compatibility | Preserved | New environment API uses existing applications observation family so the unchanged32-pair REST profile still passes; exact HTTP URI and typed result remain distinct. Optional backend/default-interface compatibility retained. |
| Scope and proof limits | Preserved | This closes bounded discovery and portable clients only; portal host/browser and original deferred RM09/RM10 workflows remain separate. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2718; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:dc1f0ff0e04010a16b51f75d877968abe0d8389f8902b263dab58c05a7050eee |
