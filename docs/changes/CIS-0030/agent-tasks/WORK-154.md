---
title: "WORK-154 Implement bounded trusted cohort discovery and rollout grant choices"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-154
parent_id: WORK-040
category: security
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-040"
instance_key: "outcome:b2bc985a1ae24544a6f24beaedfca92d"
depends_on: ["WORK-152","WORK-171"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["configuration-portal","capability-contracts","portal","integration"]
authorization_scope_basis: "sha256:ab8ffed169ea58cb00f0de7d61d704d86d22ca02f391a51a4825be2934e3a26c"
---

# WORK-154: Implement bounded trusted cohort discovery and rollout grant choices

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement bounded trusted cohort discovery and rollout grant choices

## Acceptance criteria

- [x] Deliver bounded authorized cohort choices for each exact Configuration binding using trusted configured mappings, frozen admitted registration cohort and retained assignment pins consistently with actual map admission. Return meaningful safe identifiers/labels, never issuer/subject membership or a way to invent a cohort from arbitrary text. Reauthorize current binding/read/recovery/action grants and preserve exact scoped cursor authority. Add narrow typed optional backend/client discovery without a generic permissions engine or alternate Configuration authority. This owns cohort/discovery portions of RM09-AC04, RM09-AC07, RM09-AC11, RM09-AC13, RM09-AC15, RM09-AC16, RM09-AC19 and RM10-AC28, RM10-AC29.

## Targeted validation

- [x] Prove configured/frozen/pinned cohort consistency, unauthorized source/binding/page/cursor refusal and no membership leakage with existing native fixtures; test finite pages/unknown backends and both SDKs. Document cohort/grant ownership and review this small implementation independently. Verify this owner consumes only the declared medium prerequisites (WORK-152, WORK-171) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Cohort admissibility | Configured mappings, frozen native enrollment (including closed registration) and exact-binding retained overrides match real map admission; removed last pin disappears and cannot be re-created by arbitrary text | `work154-evidence.json`; four native Cohort methods |
| Native boundaries | Actual selected legacy backend omits feature and returns501 after exact current binding read; malformed optional outputs bounded/refused503; real scoped cursor/claims/page and H2 unknown-length body refusal | `ManagementConfigurationCohortTests`; native selected result48/0/0 |
| Focused regression | 48 passed,0 failed,0 skipped:24 provider and24 pure query/cursor cases; independent TRX guard | `rm09c-154-management-tests`, `rm09c-154-management-report-verify` |
| Source/build/review | Zero warnings/errors, scoped format+verify, all13 current source hashes, exact service DLL copied to native fixture, independent SQL and HTTP reviews resolved | `.cis/local/testing/rm09-completion/work154-evidence.json` |
| SDK compatibility | Existing unchanged CSharp/TS cohort transports and wire types; prior152 evidence remains its exact source snapshot, Core bytes unchanged | Retained487 Management unit/94 canonical API/309 TS passes; no new SDK execution claimed |
| Documentation and scope | Qualified guide/catalog linked; explicit scoped optional port preserved via default TryAddScoped; existing default qualified backend/lifecycle unchanged | `docs/modules/management/configuration-cohorts.md`; no Scheduler runtime/full portal/release claim |
| CIS and accounting | Native graph0errors/90documentation warnings; all5 approved canonical documents unchanged; same040pool and historical costs | `rm09c-154-native-graph`; physical admission and samples are not continuous peak proof |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2945; failed=368; possibleTokenSavings=591; ledgerDigest=sha256:ef42e27811570517c73100cc1da496a1ec32987363cc6a7efc9cb3677def0e3c |
