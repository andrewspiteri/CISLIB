---
title: "WORK-006 Deliver isolated native development credential setup and reset"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:d2fdd9e488d544dd9212383d5322e82a"
depends_on: ["WORK-004"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["local-development"]
authorization_scope_basis: "sha256:2768d89b878bcba673609212e506dbb0c3b9bae4d36e7d239df82e0adf5ab0cc"
---

# WORK-006: Deliver isolated native development credential setup and reset

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver isolated native development credential setup and reset

## Acceptance criteria

- [x] P14: one small nonpackable C# tool creates per-workspace isolated CA/server/per-app PEM material with restricted ignored files, explicit non-secret config and deliberate renewal/reset. Reuse certificate adapter; enforce environment/origin/trust gates and no machine trust installation.

## Targeted validation

- [x] Run real tool generate/renew/reset into owned scratch; inspect native leaf/CA bounds, distinct identities, Windows ACL or applicable Unix mode, and trust replacement semantics. Validate native runtime activation gates and process-specific trust. Include runnable local guide and affected tests/build/format/review. Independent utility source may start after the native certificate contracts settle; final runtime activation/adoption proof and completion wait for actual WORK-005 public source APIs and verification.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Utility/native proof | Passed:14public native tool cases,0failures/skips | `remaining/10-corrected-native-proof/results/native-security-modules.trx` |
| Real CLI | setup/renew/reset pass; same-root renewal/new-root reset verified in exact owned workspace | `remaining/11-country-and-final-package-proof/cli-lifecycle.json` |
| Real activation/adoption | Actual E20source cases and G3facade/caller cases pass using public contracts | `remaining/10-corrected-native-proof`; `remaining/11-country-and-final-package-proof` |
| Ownership/review/style | All known owned restores/cleanup attempted, original faults/private buffers preserved; actual Windows privacy,0warn/errors and scoped style pass | `docs/modules/security/local-development.md`; final custody receipt |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1791; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:a2a8b9ada841de15ae396e4031ef464894c64219e6d127d31ef81dc05700acc5 |
