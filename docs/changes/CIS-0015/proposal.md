---
title: "CountryData unit testing examples using CommonTesting"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0015
outcome: "Add discoverable host-free MSTest examples for the real CountryData catalog and lookup steps using public CIS.CommonTesting controls, and document expected results and the boundary with existing integration/package tests."
baseline_kind: git
baseline: "31739a247efba609f27dd50caab1312bb775bd79"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:fb509d074fa02e22db91ce7a5491dd667a9c8456426d10780d715fcf348b311a"
authority: derived-outcome-authorization
---

# CIS-0015: CountryData unit testing examples using CommonTesting

## Outcome

Add discoverable host-free MSTest examples for the real CountryData catalog and lookup steps using public CIS.CommonTesting controls, and document expected results and the boundary with existing integration/package tests.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Provide discoverable unit examples for the real compiled CountryData catalog and lookup steps using existing public CommonTesting controls.
- Document expected results and the separate responsibilities of unit, HTTP/host, workflow-engine and package tests.
- Preserve production behavior, live provider data, dependencies/versions, user files and prior completed owners; use the approved fresh finite allocation.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-COUNTRYDATA-UNIT-EXAMPLES-2026-11-19:APPROVED-ALLOCATION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver discoverable CountryData unit-test examples using existing public CommonTesting controls and actual compiled application, with expected outcomes, test-layer guidance and focused native evidence.

- `unit-examples`: One coherent unit-example task: compiled application project/lock reference; catalog and direct lookup-step examples with minimal test-owned reader/lifetime; CommonTesting/example guidance; scoped format, focused native validation and review. Paths: `.artifacts/countrydata-unit`, `.cis/local/countrydata-unit-intake`, `docs/catalog.yml`, `docs/changes/CIS-0015`, `implementations/csharp/examples/CountryData/README.md`, `implementations/csharp/src/CIS.CommonTesting/README.md`, `implementations/csharp/tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj`, `implementations/csharp/tests/CIS.Core.UnitTests/CountryData`, `implementations/csharp/tests/CIS.Core.UnitTests/packages.lock.json`.

- Actual mapper/codec/retry, HTTP statuses, workflow engines, health policy and hosted shutdown remain existing integration responsibilities. No production code, provider dataset, defaults, DI framework or harness expansion.
- Andrew Spiteri explicitly approved CIS-0015 one-task plan, 7200 shared active seconds and4logical attempts including300restoration seconds on19November2026. All agents, already-performed intake, source/tests/docs/format/restore/build/review count.
- No push, merge or release is included in this new task.
- Preserve existing physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10:6442450944bytes including67108864protected bytes across.artifacts,.cis/local,implementations/csharp; no new pool/worktree or historical cleanup.
- Preserve user untracked launchSettings.json SHA256 ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. Baseline main31739a247efba609f27dd50caab1312bb775bd79; branchcodex/countrydata-unit-examples. Prior completed owners remain closed.
- Unit examples instantiate application collaborators directly: no native host, HTTP sockets, live provider, sleeps or optional collector activation. ManualHeartbeat requires explicit TriggerAsync; clocks and health/logging controls preserve their documented simulation boundaries.
- Use existing MSTest Unit project, existing CommonTesting public helpers and compiled CountryData reference. Refresh only affected locked dependency closure using already-approved packages/versions. No new dependency IDs/versions, test framework/project, friend access or copied business source.

The total is 7,200 active seconds and 4 logical attempts, including 300 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
