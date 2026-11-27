---
title: "WORK-190 Implement approved Configuration version saving deployment and rollback"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-190
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:fb170381af0f4d57ab8cbcfa04f104a5"
depends_on: ["WORK-189"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-190: Implement approved Configuration version saving deployment and rollback

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement approved Configuration version saving deployment and rollback

## Acceptance criteria

- [x] After exact R09 screenshot approval, implement one Configuration page with actual immutable coherent saved versions, exact deployed selection, local JSON editing/save and Deploy any saved version including rollback. Reuse existing native setup/publication/release/whole-map clients with separate originals, actual current grants/session, exact bytes and fresh reviewed ETags; Save never silently deploys and Deploy explicitly replaces targeting for this configuration after the all-instance review. Preserve known committed stages and recovery on partial/unknown results without new-key replay or reload continuation. Correct the duplicate creation/editor dirty ownership and Back/breadcrumb/deploy navigation with one discard decision, and contain/rescale the actual editor and list at changing desktop widths. No new backend/framework/counter or normal canary/release/assignment maze. Include the user-requested collapsed bottom Compare versions panel: explicitly select another saved version for a bounded read-only JSON diff, no mutation or hidden source data disclosure.

## Targeted validation

- [x] Use existing pure and real native fixtures to verify saved history and deployed selection, exact pair rollback, single user Save with distinct native effects and truthful partial/recovery, no assignment after Save, whole-map replacement only after Deploy confirmation, conflict/session/read-only denial, exact large-number/Unicode bytes, new-name handoff, one canceled/accepted dirty navigation, and editor/list geometry at narrow/wide changing desktop viewports. Include scoped builds, docs, independent review and actual cleanup. Preserve earlier Windows Node failures and historical evidence; no broad qualification or Scheduler work. Compare tests retain exact numeric/string tokens, demonstrate additions/removals/changes and permission/session/version mismatch handling with bounded memory/output; comparison never submits any native mutation.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current R09 functional acceptance | Passed for this bounded outcome | [Implementation verification](../r09-implementation-verification.md): current production build, 66 focused existing checks plus 9 completed-deployment/recovery checks, actual native 1 passed / 0 failed / 0 skipped and TRX guard; six browser cases with exact SQL/Audit and owned cleanup. Historical failures retained. WORK-190 remains active only for the separate finite user-review lease and its actual drainage. |

| Historical first-version checkpoint | Initial-draft fix and build passed; both real browser runs saved the first version, then failed at shutdown | [Initial correction and evidence limits](../first-version-correction.md). Each initial native run had0 passed/1 failed/0 skipped after browser success; its post-Node SQL/Audit checks did not run. Those outcomes and costs remain retained. The later complete qualification is recorded below. |

| Current shutdown and Configuration automation | 131 focused checks passed;12 distinct real native cases passed in four successful reports, zero skipped, all report guards and scoped formatting passed | [Automated coverage and exact results](../configuration-testing.md), [26-row matrix](../configuration-coverage-matrix.md), [actual host correction](../host-shutdown-correction.md) and [pinned Next close correction](../next-response-close-correction.md). Current first-version, Save stage recovery, release conflict, Deploy with pins/recovery/rollback, metadata-only/readonly, Instance status and backend authority assertions include independent SQL/Operations/Audit and actual cleanup. Windows native crashes and earlier mixed failures remain retained; runtime reliability and whole-portal release qualification are not inferred. The owner remains active for any finite fresh review lease and its actual drainage. |


| Final compatible Configuration closure | Qualified and owners settled | [Follow-up verification](../configuration-followup-verification.md), [obligation reconciliation](../configuration-obligation-reconciliation.md): current mixed/paging/status/real-session browser4/4 plus earlier compatible map/history/low-authority evidence and actual finite review fixture/assembly cleanup. Earlier failed wizard attempts remain failed; R09/deployed-only supersession retains its own exact decisions. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3406; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:66f571c0be2052cc8298acb44f2334cd9f34c2dce0ec69019db15cc387590207 |
