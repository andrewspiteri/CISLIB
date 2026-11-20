---
title: "WORK-003 Qualify the isolated PostgreSQL three-table reference consumer"
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
instance_key: "outcome:01b768a73a834d689650536672696456"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["postgres-consumer"]
authorization_scope_basis: "sha256:e27f2657a1d9d8a6f77e3116209ed1d140035a350ce9a179166710cd6935c408"
---

# WORK-003: Qualify the isolated PostgreSQL three-table reference consumer

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify the isolated PostgreSQL three-table reference consumer

## Acceptance criteria

- [x] P11-P12 and sourceAC05-07/09-12/22-23/DB28: test-onlyNpgsql10.0.4 outsideCore/CommonTesting closure and one exact new mapping; one explicitlyowned localDockerPostgreSQL18 container/data volume with pinned officialamd64 digest, private credentials and counted resources. Three ordinarylogged fixture tables, independently observed allcommit/allrollback/declinedresult behavior, supported isolation and actual query cancellation. Document the compiling public consumer and narrower unexecuted EFrecipe; no productionRM03/RM04 or existingDB access.

## Targeted validation

- [x] Confirm actual locked dependency graph and Corepackage closure, exactserver/provider versions/durability, Windows/Ubuntu native lanes actually executed. Independentconnection visibility aftercommit, laternativefailure/caughterror+mark/rollback/disposal, decline-onlyresult, allthree serverisolation values, ownedadvisoryblocker confirmedactivequery then cancellation/settlement, reusedappdatasource survivesowners. Exactowned resource restore, scope/style/docs/review; unexecutedDB/platform acceptance stays unverified.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Build/provider cases | Passed | Integrated build0warnings/errors; 11/11 Windows and11/11 Ubuntu, exact current candidate in integrated-receipt.json. |
| Real database and restoration | Passed | postgres-drained.json confirms0schemas/0other sessions/data66252800bytes; postgres-restoration.json exact owned container/volume/image/secrets removed. |
| Dependencies/docs/review | Passed | Only test-onlyNpgsql10.0.4 added; all54retainedversion/hash entries unchanged; review-003.md and supplementary approved WORK006/review-006.md. |

Full current evidence: [verification](../verification.md#integrated-rm-02-result--21-november-2026) and ignored .artifacts/transactions/CIS-0017/integrated-receipt.json. EF, real network/lost acknowledgements and full release qualification remain unexecuted. No automatic CIS cost/attempt was fabricated; external source estimates and measured native costs are retained separately.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1888; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:6e7ad77fb617d8ce4e62e2575cbdca1179ed7a65f1b0ec4be84d71d6e6b1d9b1 |
