---
title: "WORK-002 Implement presence-aware effective values and prepared retained-object commits"
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
instance_key: "outcome:a1d7580464a447239930b639f402c065"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0008:outcome:cb8dfe5d5f71451f94bc5d7743cfcb5e"
authorization_scope_ids: ["configuration"]
authorization_scope_basis: "sha256:0321dc009b4b64ac159f1b1d5dcd1f25709f61e15500133956d713330e600af1"
---

# WORK-002: Implement presence-aware effective values and prepared retained-object commits

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement presence-aware effective values and prepared retained-object commits

## Acceptance criteria

- [x] Implement defaults/common/application presence composition through W1 seams, nested POCO merge and full collection/dictionary replacement; explicit false/zero/empty/null, omissions and confirmed deletions follow selected P03. Fixed supported passive property mapping, deterministic typed representation/effective SHA-256 and per-type validation precede live writes. Prepared commits preserve root/events under the shared read gate; invalid candidates preserve accepted values/revision, and unexpected live setter failure faults/disables the entry rather than claiming rollback. No general mapper/validation language; own tests/docs/review.

## Targeted validation

- [x] Native independent expected-value cases for all scope-presence combinations, override removal, current masked common changes, once-captured defaults, nested/collection/null conversions, duplicate/unknown/ambiguous names, secret-safe failures and representation stability across property order/culture/number/date/enum choices. Controlled slowed commit with synchronized reader proves no mixed state; root and subscriptions survive. Parallel with W3 only in named files after W1; queue shared-seam changes through root. Record mapping/checksum source scope and meaningful affected native/mutation applicability.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Mapping/commit native cases | 68 passed, 0 failed, 0 skipped; Core/Unit compiled | configuration-mapping-unit retained receipt/TRX, exact frozen Mapping source and three Unit files |
| Value/ownership/concurrency behavior | Literal scope/presence/removal/canonical vectors; independent SHA; shared-child isolation; real gate/poison/cancel boundaries | ConfigurationMappingTests and ConfigurationCommitTests; docs/modules/configuration/mapping.md |
| Source and test review | No remaining required W2 defect after full duplicate-subtree check | Root source review; separate test/API/expected-value review; W4 nullable-enum Objects follow-up retained explicitly |
| Scope boundary | No provider cache, watch, scheduler or event publication hidden in mapper | WORK-003 owns source bookkeeping/notifications; WORK-004 binds real modules and closes the Objects follow-up |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=921; failed=132; possibleTokenSavings=123; ledgerDigest=sha256:2572d60527687ce87ebf6c27b06dbb46ae0e2288613a8b058d22e22fbb6e43b8 |