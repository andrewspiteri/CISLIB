---
title: "WORK-176 Deliver exact authorized Configuration binding editor context"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-176
parent_id: WORK-164
category: data
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:d0e025a0606a4cd0996806ff0befed7c"
depends_on: ["WORK-171","WORK-154","WORK-155","WORK-156","WORK-163","WORK-151","WORK-152"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-176: Deliver exact authorized Configuration binding editor context

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver exact authorized Configuration binding editor context

## Acceptance criteria

- [x] Deliver one finite optional read-only Configuration binding-editor context for the exact visible application/configuration/schema and selected Common or Application source. Resolve the actual enabled binding and immutable definition plus at most two actual source slots in one qualified clean snapshot; preserve historical source semantics elsewhere. Independently authorize actual binding and each disclosed source; selected defaults require actual source ContentRead. Explicitly distinguish absent, redacted, disabled/unavailable and unsupported, preserving exact schema/default JSON bytes and bounds without a fleet scan, manifest disclosure, nested ObjectDefaults template exposure, invented empty object or merged-value authority. Reuse existing native backend, typed response/codecs/client and current security/cleanup owners. No mutation, new schema/package, Scheduler runtime or full editor delivery is claimed. This is the backend/client prerequisite of original164, whose author/review/publication/release obligations remain in the sibling.

## Targeted validation

- [x] Use existing unit/canonical/native Management fixtures to prove exact application/environment/schema pairing, actual Common and Application binding sources, disabled retained sources, independently denied Common metadata and selected defaults, hidden/missing/unsupported targets, malformed optional backends, cancellation and true cleanup. Validate exact lexical defaults/shape and compatible bounded CSharp/TypeScript decoding/client echo checks; no all-fleet discovery or authorization by browser names. Include affected docs, scoped format and independent security review. Same original100 parent/shared allocation; no extra capacity.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Qualified native read and affected regression | 14 actualprovider cases,507 Management units,159 canonicalcontract cases and94 affectedTypeScript cases passed0fail/skip; guards passed | `.cis/local/testing/rm09-completion/work176-evidence.json` |
| Build,format,review and scope | Affected CSharp builds0warnings/errors, scopedformat/verify and independentreviews passed; retain initialfixture failures and exact snapshots | Exact optionalbinding/source/defaultread only; candidatepreview/UIauthoring remain laterowned work; no Scheduler/runtime/release claim |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3017; failed=370; possibleTokenSavings=591; ledgerDigest=sha256:aa58b1e309b43e59e120ea2d7a29a1f15fb3aa2a0f16d7551465887777b61ecc |
