---
title: "WORK-006 Remove superseded examples and settle active integration references"
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
instance_key: "outcome:3f89bd4c180a422ea1e510729a0de7fd"
depends_on: ["WORK-002","WORK-003","WORK-004","WORK-005"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda"
authorization_scope_ids: ["example-consolidation"]
authorization_scope_basis: "sha256:ecf30cfd510c63003e9c825cd78740318aac031c9541a114255fd388e60dbf2b"
---

# WORK-006: Remove superseded examples and settle active integration references

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Remove superseded examples and settle active integration references

## Acceptance criteria

- [x] AP6 / CLEAN-01–CLEAN-03: after migration delete all seven named old tutorial projects, newer RestApi and redundant type-manager support; CountryData is the only maintained runnable tutorial. Coordinate solution/project/lock/copy-target edits, replace or retire active profile/script paths, module/root guides and existing skill/instruction routing. Preserve the three exact untracked user launch files reversibly with hashes outside removed examples before directory removal. Preserve historical records/results/accounting as history and unrelated user work. Resolve all active old references, document minimal surviving fixtures and exact run command. Include final affected native source/architecture/compatibility/package checks and independent review without another framework/tool campaign.

## Targeted validation

- [x] A12–A14: solution and affected projects build through current eligible CIS owner; selected facade/CountryData/module preservation/source and real Core-only/optional package cases pass on actual Windows. Final rg classifies old paths/names/outputs/self-test flags and leaves no unresolved active refs or copied apps. User/source hashes unchanged, no active reservations/recovery uncertainty at settlement; real platform/smoke gaps remain explicit.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Complete consolidation | Eight tutorials, redundant sample support, ExampleLate and11 obsolete test/helper files removed; one example project, active references resolved; three user files preserved with exact hashes | final-status.log; active-reference-scan.log; user-settings-preservation.json |
| Windows native solution/regression | Ordinary Release build0warnings/0errors; aggregate3034/3034passed,0failed/skipped/aborted; exact Core source-process guard included | native/incremental/a5b190e240d44f0cb2e77a6979d70d8d; final-observed.trx; final-mapped-native.log |
| Independent review and guides | Static retained-project references valid; stale SDK link and status-only retry wording corrected; no other active tutorial references;184/184strict docs | final-docs.log; native source guide; current module/skill routing |
| Settlement | scopeComplete=true,unitCurrent=true,zero pending batches and native reservations/recovery hold at final admission; no release qualification claimed | final-mapped-native.log; final work status |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1502; failed=223; possibleTokenSavings=591; ledgerDigest=sha256:6b205f90fd6e8419c849d2d46dc7a1edb37e7de174f4bde3768cb01f2600ee58 |