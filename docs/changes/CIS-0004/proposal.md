---
title: "Close Logging, Type Manager and Healthcheck developer integration"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0004
outcome: "Developers consume the existing CIS.Core package through the three accepted feature examples; CountryList package build and folder publish demonstrate startup, failure, refresh, shutdown, HTTP and local-pipe/probe behavior on Windows and Ubuntu, while completed evidence and accounting remain intact."
baseline_kind: git
baseline: "d84e463839d1a3ffc5397ced4a1a53cf4ef52f87"
graph_build_id: "sha256:236afe55c7c632a7de6ef96ceb49f51b80e9ce29a93502fffddea75d35779da5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:c6ec7c6f3c41f789fb40e2e8346cd2297900d74ddc9ea39393f43d90c5de8df1"
authority: derived-outcome-authorization
---

# CIS-0004: Close Logging, Type Manager and Healthcheck developer integration

## Outcome

Developers consume the existing CIS.Core package through the three accepted feature examples; CountryList package build and folder publish demonstrate startup, failure, refresh, shutdown, HTTP and local-pipe/probe behavior on Windows and Ubuntu, while completed evidence and accounting remain intact.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- AC-1: Logging retains its completed ConsoleHuman package workflow, real contextual output and truthful flush/shutdown behavior, with applicable results referenced or genuinely affected checks passing.
- AC-2: Type Manager retains its completed Console/Web package workflows, automatic inventory, preparation before container construction and cached fixed catalog, including the accepted missing-assembly failure behavior and defaults.
- AC-3: The actual CountryList source works from the exact package in build and folder-published output. Its existing startup/failure/refresh/shutdown observations pass, and supported HTTP and matching local-pipe/probe consumption are demonstrated on actual Windows 11 x64 and Ubuntu 24.04 x64.
- AC-4: A developer can follow concise existing documentation to restore, build, run and publish each example. Commands, package prerequisites, ownership and failure behavior match the implementation.
- AC-5: The verification record identifies applicable inputs, package and platform for each result, what was reused, what ran and any unresolved item. New work is reviewed and settled through supported CIS transitions; completed history remains intact.

These criteria retain Andrew Spiteri's actual 15 November 2026 instruction,
attachment `cislib-three-feature-closure-prompt-2026-11-15.txt` (SHA-256
`9f0c6883a77bc807e8cbc9f2fd1122d8cef7e74bd22232fa0797622c538ff850`).
They map to accepted `CIS-BR-003` through `CIS-BR-007`; `CIS-BR-008` release
qualification remains separate. This delegated integration outcome does not
replace product-baseline approval or any completed owner's contract.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-THREE-FEATURE-INTEGRATION-2026-11-15:user-instruction:existing-storage`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Close implementation and developer/package integration for Logging, Type Manager and Healthcheck against AC-1 through AC-5 in the actual 2026-11-15 user instruction.

- `integration`: Finish exact-package CountryList build and folder-publish, existing startup/failure/refresh/shutdown and HTTP/local-pipe/probe paths on actual Windows and Ubuntu; retain Logging and Type Manager accepted integration, bounded currentness diagnosis, concise guides, shared acceptance, focused review, cleanup and branch commits. Paths: `.cis/local/testing`, `AGENTS.md`, `docs/changes/CIS-0004`, `docs/modules`, `implementations/csharp/examples/healthcheck`, `implementations/csharp/tests`, `implementations/csharp/tools/CisHealthProbe`, `planning/cis/cleanup-disposition.md`, `planning/decisions.md`.

- Currentness diagnosis at most900 active seconds; Ubuntu disposable output at most2147483648 bytes; all work shares7200 seconds,3 logical attempts,180 included restoration seconds.
- No Configuration, Heartbeat scheduler, generic startup coordination, new harness, evidence archives, coverage/mutation/stress campaign, new deployment profiles, main merge or package publication. Full release obligations remain separate.
- One concrete Healthcheck task owns AC-1..AC-5 including required review, guides and settlement. No unresolved seed execution.
- Preserve Core public contracts, defaults, dependencies and application ownership. Use same exact applicable candidate across both platforms; SDK10.0.401 NET/ASP.NET10.0.12, framework-dependent untrimmed build/folder publish.
- Preserve all completed CIS-0001/0002/0003 contracts, results, approvals, costs, attempts and limits; no fingerprint/report edits or forced reuse.

The total is 7,200 active seconds and 3 logical attempts, including 180 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.cis/local`, `implementations/csharp/eng/CIS.TypeManager.Build/bin` (count only), `implementations/csharp/eng/CIS.TypeManager.Build/obj` (count only), `implementations/csharp/src/CIS.Core/bin` (count only), `implementations/csharp/src/CIS.Core/obj` (count only), `implementations/csharp/tests/CIS.Core.IntegrationTests/bin` (count only), `implementations/csharp/tests/CIS.Core.IntegrationTests/obj` (count only), `implementations/csharp/tests/CIS.Core.TestSupport/bin` (count only), `implementations/csharp/tests/CIS.Core.TestSupport/obj` (count only), `implementations/csharp/tests/fixtures/DiscoveryHost/bin` (count only), `implementations/csharp/tests/fixtures/DiscoveryHost/obj` (count only), `implementations/csharp/tests/fixtures/HealthcheckHttpHost/bin` (count only), `implementations/csharp/tests/fixtures/HealthcheckHttpHost/obj` (count only), `implementations/csharp/tools/CisHealthProbe/bin` (count only), `implementations/csharp/tools/CisHealthProbe/obj` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
