---
title: "WORK-179 Deliver finite Configuration authoring, exact immutable publication and release"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-179
parent_id: WORK-177
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:0a705e3ce73148e29f29fcd3ce775cee"
depends_on: ["WORK-171","WORK-154","WORK-155","WORK-156","WORK-173","WORK-176","WORK-174","WORK-178"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-179: Deliver finite Configuration authoring, exact immutable publication and release

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver finite Configuration authoring, exact immutable publication and release

## Acceptance criteria

- [x] Deliver authorized source selection, existing content/default scope, finite trusted text/number/Boolean/enum/duration/group controls and bounded exact generic JSON fallback. Render only known declared field hints; preserve unrelated/inherited members and exact numeric/Unicode lexical meaning instead of round-tripping through Number. Separate read/default/value authorization, redacted/unavailable versus empty/absent and owner semantic adoption versus structural validation. Validate/preview side-effect-free, review exact payload then publish immutable content and create an exact common/application coherent release under original independent keys/ETags. Publication/release creation does not assign or activate. Conflict retains only still-authorized in-memory draft for explicit reconciliation; partial/unknown steps recover original outcomes without replay. Use the shared closed BFF/receipt primitive, not a new store/framework. Owns RM09-AC07, RM09-AC09, RM09-AC10, RM09-AC11, RM09-AC12, RM09-AC14, RM09-AC15, RM09-AC16, RM09-AC17, RM09-AC18, RM09-AC19, RM09-AC20 and RM10-AC26, RM10-AC27, RM10-AC28, RM10-AC29. Consume the exact binding-editor context sibling for actual source pair/default scope; redacted or unavailable source/default state never becomes empty or publishable. Do not require or activate the future trusted Scheduler catalog. Use the candidate-preview sibling for actual unpublished effective-change review, never call immutable-reference preview a candidate pass. Source history is UUID-ordered, not current/latest; edit only an explicitly selected authorized publication or a precisely labelled current assignment-pinned reference, with no fleet/history scan. Trusted shape has no duration-unit hints; use only declared finite controls and explicit JSON/string fallback without guessed units.

## Targeted validation

- [x] Test declared hint/path/type limits and unsupported components, exact large numbers/Unicode/false-zero-null/unrelated members, no mask/reset/hidden value publication, conflict and same-key partial recovery. Run actual native RM08 author-review-publication-release desktop flow with read/write/lookup separation, session/outage/grant loss and focus/keyboard. Document authoring semantics and exact content handling. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-171, WORK-154, WORK-155, WORK-156, WORK-163) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites. Add exact context sibling dependency once native IDs exist; original163 is preserved Decomposed and future catalog175 stays unstarted. Once native child IDs exist, add the candidate-preview sibling as a concrete prerequisite. Preserve all original177 publication/release/draft/recovery tests and full accepted R07 workflow.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Exact authoring | Implemented and directly observed | `work179-current-inputs.json`, 21 exact source hashes and7 built inputs; exact candidate review, lexical field controls, current scope and separate native publication/release. |
| Focused pure/build checks | 94 pure cases and production/strict TypeScript passed | `rm09c-179-portal-pure-reviewed`, `rm09c-179-portal-build-after-cache`; pinned integration rebuild0warnings/errors and scoped format/verify. |
| Actual desktop/native | Fresh desktop-only1passed/0failed/skipped; saved TRX guard accepted44.964s | `rm09c-179-native-desktop-fresh-process`, `rm09c-179-desktop-fresh-report-path-fixed`; real DB/Audit/map/digest assertions and actual owner drain. Authentication has separate passing observations; no final green two-case batch claimed. |
| Failure and platform limits | All failures retained; intermittent Node native fast-fail remains unresolved | `work179-evidence.json`, `work179-native-node-observation.md`; no runtime pin/flags changed and no platform/release reliability claim. |
| Review and closure | Independent source/fixture/visual review; Native CIS Complete accepted, valid with zero blockers; graph admission refused before execution | `work179-boundary-review.md`, `work179-ui-review.md`, `work179-native-fixture-review.md`; same R07 interaction approval. Current direct source/build/native evidence is retained; prior graph is supporting and stale for these changes. No Scheduler/runtime/rollout delivery claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3119; failed=372; possibleTokenSavings=591; ledgerDigest=sha256:02c7def0c108a7d89f6a308130c81e6d2a981081777e6c3fcc0a1304d3ca88d0 |
