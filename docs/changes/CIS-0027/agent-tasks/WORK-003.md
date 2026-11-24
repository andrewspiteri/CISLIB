---
title: "WORK-003 Implement coherent releases and authoritative assignment maps"
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
instance_key: "outcome:e3da542d0b2f42769f27678f93603cdb"
depends_on: ["WORK-001","WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["backend","records"]
authorization_scope_basis: "sha256:780f9b95f24b6073c957f3bf71f17360e9f9ac970b7d68ea2df6d4fa71cd7855"
---

# WORK-003: Implement coherent releases and authoritative assignment maps

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement coherent releases and authoritative assignment maps

## Acceptance criteria

- [x] Exact referenced sources or explicit absence, atomic retention references, whole-map generation/CAS and trusted cohorts, explicit promote/rollback and redacted preview, authoritative target/session-bound snapshots and conditional equality without cache authority.

## Targeted validation

- [x] Provider reference atomicity, distributability, map races, scoped read/preview, cohort spoof/rebootstrap, rollback/pin preservation, equal/old observations, multi-replica/restart/recovery tests and operator docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native PostgreSQL and HTTPS cases | 58 passed, zero failed/skipped; releases/maps, original completion recovery, authoritative snapshots and affected persistence | `.artifacts/configuration-publication/CIS-0027/WORK-003/release-assignment-snapshot-native-tests-corrected/results/configuration-distribution.trx` |
| Current native builds | Service and full parent zero warnings/errors against current007 Core; corrected fixture rebuild clean | WORK-003 `release-snapshot-service-build-corrected`, `distribution-snapshot-fixture-rebuild`; WORK-007 `configuration-current-integration-build` |
| Format | Scoped apply and verify passed | `.artifacts/configuration-publication/CIS-0027/WORK-003/distribution-snapshot-scoped-format-verify/` |
| Review and guide | Independent native ownership, original journal, whole maps/reference locking, preview and snapshot cleanup review accepted | `../review.md`; `../../../modules/management/configuration-distribution.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2425; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:da010e056d71158ce8382910c9150677a8ddcc859cf896d147dcd11027ef8b08 |
