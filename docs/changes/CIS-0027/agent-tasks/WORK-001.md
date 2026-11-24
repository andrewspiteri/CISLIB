---
title: "WORK-001 Freeze RM08 portable and enrollment contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:fb3b686767be4c948405b1ff04920a8d"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["contracts","records"]
authorization_scope_basis: "sha256:f95359a3c4b078aef3d0e1c23c1c0243401c4b8c1d14762e3f0a902970941d1c"
---

# WORK-001: Freeze RM08 portable and enrollment contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Freeze RM08 portable and enrollment contracts

## Acceptance criteria

- [x] Exact nineteen configuration APIs, shared strict CSharp wire codec and independent fixtures, identity/session/source/release/map/adoption meanings, additive enrollment/snapshot and client-result boundaries, compact REST alias budget, explicit bounds and documented compatibility. Shared server/client wire types and codecs are available before backend work; WORK007 owns REST-specific transport integration.

## Targeted validation

- [x] Canonical OpenAPI/schema, shared CSharp wire codec and independent conformance; API/source compatibility expectations, no public UI or unauthenticated business endpoint; all intake included.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Affected native builds | Passed, zero warnings/errors | `metadata-content-unit-build` and `metadata-content-architecture-build` native logs |
| Configuration/Management unit behavior | Passed 1,026; zero failed/skipped | `.artifacts/configuration-publication/CIS-0027/WORK-001/metadata-content-contracts-unit-tests/results/contracts-unit.trx` |
| Canonical schema/interop facts | Passed139; zero failed/skipped;45 independent vectors | `.artifacts/configuration-publication/CIS-0027/WORK-001/metadata-content-canonical-contract-tests/results/contracts.trx` |
| Scoped whitespace and ordinary diff | Passed | Native format/verify phases and `git diff --check`; raw source hard breaks preserved |
| Independent review and necessity | Accepted, no required or optional findings | [Review](../review.md); additive ports/codecs, no new framework/provider/harness |
| Source, user file and finite shared costs | Preserved | [Verification](../verification.md), local `contracts-freeze-evidence.json`, native/metadata journals and original approved allocation |
| Downstream runtime/provider acceptance | Explicitly pending in002–011; not claimed by001 | [Implementation plan](../implementation-plan.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2406; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:fe379d4e1e11b9b8296eab2b028c9f674470823b3235d43af24261693f164b67 |
