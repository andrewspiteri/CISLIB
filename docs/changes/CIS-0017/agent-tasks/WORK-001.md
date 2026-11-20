---
title: "WORK-001 Deliver portable transaction contracts, named configuration and native begin"
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
instance_key: "outcome:b0c52cd4483b42aba543f3ca28b60472"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["contracts-begin"]
authorization_scope_basis: "sha256:aabd234f59cbf644c6e758a0c7db3b9b038b3cc11783a436a0ac8d126ac86819"
---

# WORK-001: Deliver portable transaction contracts, named configuration and native begin

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver portable transaction contracts, named configuration and native begin

## Acceptance criteria

- [x] P01-P03 and sourceAC01-04/registrationAC23: exact portable owner/context/completion/error shapes, passive immutable bounded/idempotent named database setup, exact isolation validation and ambient exclusion before resolution/IO, one native lease/transaction, primary begin fault preservation and partial cleanup. Includes portable contract and already-performed intake; no full-runtime claim before terminal lifecycle completes.

## Targeted validation

- [x] Build/API/package boundary, zero-IO registration/metadata tests, invalid/unknown/duplicate/max16 aliases, immutable options/isolation capture, ambient exclusion, successful lease identity and partial open/begin cancellation/fault cleanup. Native provider doubles, scoped style, docs and independent review; preserve actual failures/costs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Scoped style | Passed, including verify-no-changes | `.artifacts/transactions/CIS-0017/WORK-001/foundation-format-pipe-access` and `foundation-format-verify`; initial sandbox pipe failure retained separately |
| Affected Release build | Passed; zero warnings/errors | `.artifacts/transactions/CIS-0017/WORK-001/foundation-build/native.log` |
| Windows foundation | 29 passed, zero failed/skipped | `.artifacts/transactions/CIS-0017/WORK-001/foundation-test/results/foundation-windows.trx` |
| Actual Ubuntu foundation | 29 passed, zero failed/skipped | `.artifacts/transactions/CIS-0017/WORK-001/foundation-linux/results/foundation-linux.trx` |
| Exact Core candidate | Current source build equals executed bytes | `.artifacts/transactions/CIS-0017/WORK-001/foundation-receipt.json`; SHA256 `7c41840425b9c005f37ff60199b289b50155261af337b9a928877d6c0a8c5d5d` |
| Independent foundation review | No required findings; source and both TRX scopes/hashes confirmed | `../review-001.md` and `../verification.md`; source/bounded provider doubles only |
| Remaining module work | Explicitly retained in WORK-002 through WORK-005 | Deeper lifecycle races, real PostgreSQL, concrete telemetry and public packages are not claimed by this foundation |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1860; failed=264; possibleTokenSavings=591; ledgerDigest=sha256:60522c6f33be15bb6e0f5944e0b2628b8e802c5ed7b22dbda99ecf684088ddbf |
