---
title: "WORK-155 Implement bounded operator-bound original command receipts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-155
parent_id: WORK-050
category: data
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-050"
instance_key: "outcome:4b85dfa29088424c9bdda928b82f354e"
depends_on: ["WORK-152","WORK-171"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-155: Implement bounded operator-bound original command receipts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement bounded operator-bound original command receipts

## Acceptance criteria

- [x] Deliver the typed browser session-storage receipt primitive used by portal commands: create original key before submit, retain only operator association/family/exact target/key/time, at most16 receipts with24-hour UI retention, no credentials/claims/payload/draft persistence. Expiry removes only UI references and never backend duplicate protection. Recovery stays with original key/family/target and fresh permission, one selected recovery request at a time; timeout/NotFound/lookup denial cannot authorize automatic replacement work. No server draft database or generic workflow ledger. Owns receipt portions of RM09-AC09, RM09-AC10, RM09-AC12, RM09-AC17, RM09-AC19 and RM10-AC28.

## Targeted validation

- [x] Test bounded strict parsing, count/byte/age/family/target, same-user reload and original-key retention through uncertain outcome/lookup refusal with existing portal unit tools. Document UI receipt versus RM03 protection and independently review the implementation. Verify this owner consumes only the declared medium prerequisites (WORK-152, WORK-171) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Primitive behavior | 13 passed,0failed/0skipped/0cancelled; minimal finite identities, strict bounds, explicit expiry, exact original-key retention and joined controlled lookup | `rm09c-155-receipt-tests`; `.cis/local/testing/rm09-completion/work155-evidence.json` |
| TypeScript compatibility | Existing pinned Next16.4.6 production build/strict TS passed | `rm09c-155-portal-build`; source frozen SHA-256 reviewed |
| Independent review | Expiry/duplicate-key/local-reset findings resolved; no remaining required pure-primitive finding | `work155-receipt-review.md`; real auth/context/dispatch/projection joins not claimed |
| Documentation | Guide/catalog/module links; UI references distinguished from RM03 protection; no body/draft/token/claims/outcome/session-marker storage | `docs/modules/management/portal-command-receipts.md` |
| CIS/source/resource preservation | Native graph0errors/90documentation warnings; all5 canonical approvals unchanged; initial storage admission refusal retained, no native execution; exact owned cache dispositions and estimate variance retained | `rm09c-155-native-graph-after-cache`; unchanged050/120h128attempt/6GiB64MiB-reserve limits |
| Scope boundary | Only browser primitive and controlled callback promise settlement. Context producer/protected response/state/UI integration stays156/component-owned; no server DB, opened auth/session route or Scheduler runtime | Source and native evidence above; no fullportal/release claim |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2952; failed=368; possibleTokenSavings=591; ledgerDigest=sha256:a5097dcadbcbd0cddd0c900526c33970f85476d88fc8da031826f25ed589f2a3 |
