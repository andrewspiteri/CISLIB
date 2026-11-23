---
title: "WORK-013 Correct Visual Studio messaging process fixture build ordering"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-013
parent_id: none
category: implementation
complexity: low
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:2b46a44ac8cd4bb5ab11b6000548e147"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["consumers"]
authorization_scope_basis: "sha256:91d89d3432c818ed12b1dcfa668802728d1e8ba13587636d85bc2811dd5a56e0"
---

# WORK-013: Correct Visual Studio messaging process fixture build ordering

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Correct Visual Studio messaging process fixture build ordering

## Acceptance criteria

- [x] The existing Messaging/CrashHost process fixture is loaded under the solution Fixtures folder so Visual Studio schedules its ProjectReference before the parent copies executable files. Preserve parent Debug/Release configuration and the fail-closed DLL/runtimeconfig/deps guard, existing fixture/public contracts and no-service defaults. Include a brief build note and actual native verification; no runtime, dependency or test-harness expansion.

## Targeted validation

- [x] Use installed Visual Studio 18 MSBuild with IDE-style reference scheduling plus the pinned CLI where appropriate to build Debug and Release, starting without Debug CrashHost output. Verify matching configuration-specific fixture DLL/deps/runtimeconfig copies and no-argument process loading with expected bounded rejection before any service access. Record actual output/warnings/errors and user-file preservation, without claiming interactive GUI or SQL/broker acceptance. Supporting docs and independent review belong to this one task; preserve completed owners/costs and original shared limits.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Genuine original IDE reproduction | Expected failure reproduced | WORK-013/vs-debug-original-reproduction/vs-build.log; absent Debug CrashHost output, actual Visual Studio2026 devenv.com /Build /Project,17projects succeeded/1failed with exact parent copy error. |
| Corrected actual IDE builds | Passed | vs-debug-fixed-solution and vs-release-fixed-solution:19projects succeeded/0failed each; CrashHost precedes IntegrationTests. Existing MSB3270 CountryData architecture warning retained. |
| Pinned CLI whole solution | Passed | vs-fixture-cli-release-solution-build:SDK10.0.401 Release build,0errors/1existing MSB3270 warning. No functional test suite or services launched. |
| Actual copied process files/runtime | Passed | Both configurations:13files hash-match exact same-configuration fixture output, DLL/deps/runtimeconfig present, NET/ASP10.0.12 and RollForwardDisable preserved. Pinned no-descriptor launches exit2 with expected InvalidDataException before service access. Ignored vs-fixture-process-proof.json. |
| Minimal change, docs and independent review | Passed | One existing fixture project added under existing solution Fixtures; parent reference/copy guard unchanged. TestsREADME note and independent source review pass. No C# source, package, lock or pin changes. |
| Authority, bounds and preserved user input | Recorded | v5/v6 supported scope history preserves original global constraints; native draft-task reconciliation and distribution use existing40h48attempt6GiB limits. WORK013 has2880seconds/2attempts including intake, agents, builds/docs/review. User launch settings hash unchanged. No push/merge/release. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2238; failed=307; possibleTokenSavings=591; ledgerDigest=sha256:466ab634992d99a904173a360fd70a07fe8155859fa6ea8a3f73fbf21515f398 |
