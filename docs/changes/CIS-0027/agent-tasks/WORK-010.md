---
title: "WORK-010 Enforce quota and reference-safe configuration history retention"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:20b9712057004a169e09e5d8bdd22ef3"
depends_on: ["WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["retention","backend","records"]
authorization_scope_basis: "sha256:b958efe4bdc9933526c5dd54984e865dc53ddda82a7c8e346669eba73d97d0b5"
---

# WORK-010: Enforce quota and reference-safe configuration history retention

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Enforce quota and reference-safe configuration history retention

## Acceptance criteria

- [x] Explicit total logical configuration quota and bounded cleanup on existing owner, current/pinned release and shared source protection, latest-ten eligible history and no source race, original result retention independent of payload pruning.

## Targeted validation

- [x] Real encoded/retained bounds, source/release reference install versus GC races, pinned-full refusal, rollback-to-pruned denial, old original-result recovery without recreation, and quiet bounded cleanup docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Real retained history | Six native retention cases passed within the 79-case provider selection | WORK-009/adoption-retention-provider-tests-corrected/results/adoption-retention.trx |
| Atomic bounds and recovery | Latest-ten eligible history, assigned/pinned/shared protection, one row budget, exact session/adoption/content occupancy, hard quota, installer/GC races, original result recovery and native acknowledgement loss passed | Same receipt; ../../../modules/management/configuration-retention.md |
| Independent review | No required findings; actual commit/cleanup proof remains separate from source review | ../review.md |
| Formatting | Changed CSharp files verified with the pinned SDK | native-costs.jsonl phase core-host-provider-closing-format-verify |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2442; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:2dc70e2b8285d8aa5ab9310ef7d492c8e84e8c0b6727e53c749a229fc3bb2524 |
