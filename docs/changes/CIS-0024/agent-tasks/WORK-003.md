---
title: "WORK-003 Persist sequenced registration, presence and complete inventory"
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
instance_key: "outcome:cb8c17e636b044b2a05e9aec04ae0a52"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["management-store"]
authorization_scope_basis: "sha256:29e72b1c253845558e6cf0ad251f61ce33a3721f6d122a19eb82a91fa24b43ec"
---

# WORK-003: Persist sequenced registration, presence and complete inventory

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Persist sequenced registration, presence and complete inventory

## Acceptance criteria

- [x] AC03/04/05/15: application-owned PostgreSQL schema/version/explicit installation, stable exact app identities, immutable bound registration, cross-replica newer/equal/older sequence behavior and DB receipt time, terminal close/purge/re-register, complete per-instance inventory, byte/count capacity and bounded telemetry-only retention.

## Targeted validation

- [x] Real shared-DB two-root uniqueness/sequence/capacity/replay tests; rejection preserves previous full snapshot, reported precision and no freshness replay. Hundreds of jobs/multibyte bounds, read reconstruction/restart and schema readiness. Existing realPG/Testcontainers only, no process lock authority or new idempotency store; native build/format/docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Real provider and two roots | Passed | 16 actual PostgreSQL18 tests; zero failures/skips, pinned isolated Testcontainers, shared occupancy/receipt/sequence/CAS/retention/unknown-commit/cleanup boundaries. `.artifacts/management/CIS-0024/WORK-003/corrected-real-postgresql-persistence-tests/results/management-persistence.trx`. |
| Affected build and source quality | Passed | Integration build zero warnings/errors; explicit-file ordinary formatting and `--verify-no-changes` passed (`postgres-explicit-file-format-verify`). Earlier analyzer, regclass and format-selection failures/costs remain retained. |
| Persistent reconstruction and readiness | Passed | Existing-state reconstruction, read-only schema validation, unsupported-version refusal and dependency-unavailable behavior included in the sixteen native cases. Original reported strings remain separate from shared receipt time. |
| Review and ownership | Passed | Participant methods do not complete/dispose borrowed owners; exact UUID/application/issuer binding, no process-lock authority, bounded complete inventory admission and telemetry-only cleanup reviewed. README includes uniform startup limits and finite ceilings. |
| Evidence limit | Explicit | HTTP native authentication, recoverable metadata/Audit, both SDK consumers and final host drain belong their active/dependent tasks; no deployment or production qualification claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2299; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:11b0c01dd3f83abe1a4760a813723c0f252614fdb82a4cff63c44c6a5c45df19 |
