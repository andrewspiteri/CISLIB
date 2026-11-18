---
title: "WORK-003 Compose ordered customization authentication and response mapping"
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
instance_key: "outcome:010b44953eb3493d99ef3c7d30d3fb10"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-003: Compose ordered customization authentication and response mapping

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Compose ordered customization authentication and response mapping

## Acceptance criteria

- [x] Implement ordered CIS exchange handlers and customization/final-auth/mapper seams against the fixed contract. Enforce at-most-once next, zero-dispatch short circuits, isolated credentials/defaultreplacement/explicitdisable, final allowed-origin/TRACE guards, exact signing representation and no post-sign ordinary mutation. Typed expected prep/auth failures return bounded outcomes; unexpected defects surface after owned work settles. Single reader coordinates decoding and business/error mapping without native objects/history. No new pipeline graph or independent retry owner; own tests/docs/review.

## Targeted validation

- [x] A03-A05/A08/A13/A16: asyncA/B/C outwardC/B/A inward order and final signed native request; concurrent shared options without state/defaultheaders leakage; HTTP200 business rejection and non2xx notdecodedasT; caller/total cancellation and throw-after-next ownership; TRACE body/credential/defaultauth and customized/disallowed origins. Preserve nativebody reader and actual original causes.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Callback/mapping components | 40 targeted cases passed within the 134-case Core checkpoint | `.cis/local/rest-client-intake/rc23-components-pass.trx`; `native/incremental/ba6fdecdc301435ab18876d85338c20f` |
| Review and documentation | Origin/TRACE/content acquisition, native signing headers and actual signing task ownership corrected | [Extensions guide](../../../modules/rest-client/extensions.md), [verification](../verification.md) |
| Boundary | Component outcome complete; actual public caller/deadline/concurrent operation/host integration remains WORK-005 | No full-runtime or release qualification inferred |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1237; failed=181; possibleTokenSavings=310; ledgerDigest=sha256:758459b758eaa5f393142da0f309b1043a3819e5dbb16baa7193b40c28efc699 |
