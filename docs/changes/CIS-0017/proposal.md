---
title: "Local transaction integration (RM-02)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0017
outcome: "Implement the specified portable local transaction owner/context and guarded ADO.NET participation in Core, with conservative persistence outcomes and actual cleanup; qualify the PostgreSQL three-table pattern outside Core dependencies, add bounded existing telemetry/public testing/package consumption/docs/skill, preserving laterRM03/RM04 and actual platform/evidence boundaries."
baseline_kind: git
baseline: "539a05d184432b5b8c131caf8454a44d0134c692"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:e2121662ba37300252e1400ba733da030906866c39e3560fc25776ac1c57303e"
authority: derived-outcome-authorization
---

# CIS-0017: Local transaction integration (RM-02)

## Outcome

Implement the specified portable local transaction owner/context and guarded ADO.NET participation in Core, with conservative persistence outcomes and actual cleanup; qualify the PostgreSQL three-table pattern outside Core dependencies, add bounded existing telemetry/public testing/package consumption/docs/skill, preserving laterRM03/RM04 and actual platform/evidence boundaries.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Core exposes the specified portable contracts and public ADO.NET participation without a new shipping dependency, ambient transaction selection or framework.
- Named composition is passive/immutable/bounded; begin, admission, rollback-only, cancellation, completion and cleanup preserve exact ownership, original application faults and the three persistence outcomes.
- A real isolated PostgreSQL consumer proves the three-table unit, caught-error marking, decline-only result, isolation and active-query cancellation from independent connections; actual provider/platform limits remain explicit.
- Existing Logging/Metrics and bounded CommonTesting/public packages work through current contracts. Documentation, compiling usage, agent skill, focused native tests and independent review are included with their owned implementation outcomes.
- All source AC-01–28 groups are mapped by the five-task plan. Missing native database/platform proof remains unverified; scripted lost-acknowledgement behavior is not a real network-failure claim.

## Requirements and proposed execution

[Preserved source](../../../planning/transactions/sources/CISLIB_RM02_Local_Transaction_Integration_Specification_v1.0.md), SHA-256 `8a9e056a7ad30a2d2d6af475a1d1f0e40a138f10e1f61905a374f6f1d1fcbbb7`.

Read the proposed [P01–P14 plan and allocation](implementation-plan.md),
[dependency/Docker profile](dependency-profile.md), `task-contracts.json`,
`authorization-request.json` and `proposed-allocation.json`.
No authority or completion is asserted by these drafts. One actual human decision
is required for fresh RM-02 scope, Npgsql and the amended physical storage ceiling.
After it, CIS registers the five canonical tasks under the delegated outcome;
there is no separate per-task approval stage. No product-document approval,
existing database access, external deployment, push, merge or release is included.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM02-2026-11-21:retained-mappings2:supporting-scope`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement and qualify CISLIB RM02 local transaction integration under the preserved specification and P01-P14: portableCore owner/context/guardedADO.NET, accurate persistence and cleanup, actual isolatedPostgreSQL three-table proof, existing boundedLogging/Metrics, publicCommonTesting/package consumer and currentdocs/skill. Keep laterRM03/RM04 and broader release work separate; report actual platform/evidence limits.

- `contracts-begin`: P01-P03: portable contracts, immutable named registration/isolation and native begin/partial cleanup; preserve Core dependency/host ownership and prove passive/ambient/invalid-input behavior. Paths: `.artifacts/transactions`, `.cis/local/testing`, `.cis/local/transactions-intake`, `contracts/transactions`, `docs/catalog.yml`, `docs/changes/CIS-0017`, `docs/modules/transactions`, `implementations/csharp/src/CIS.Core/Transactions`, `implementations/csharp/tests`, `planning/transactions`.
- `native-lifecycle`: P04-P08: guarded public native capability, sequential admission, rollback-only/completion synchronization, conservative outcomes and actual cancellation/cleanup settlement; same owner writer, no new framework or replay. Paths: `.artifacts/transactions`, `.cis/local/testing`, `.cis/local/transactions-intake`, `contracts/transactions`, `docs/catalog.yml`, `docs/changes/CIS-0017`, `docs/modules/transactions`, `implementations/csharp/src/CIS.Core/Transactions`, `implementations/csharp/tests`, `planning/transactions`.
- `observations`: P09-P10: existing Logging/Metrics integration, conditional128-combination catalog and exact host binding, safe failures/disabled/late attachment/actual scrape; no new collectors, capacities or histories. Paths: `.artifacts/transactions`, `.cis/local/testing`, `.cis/local/transactions-intake`, `.github/skills/cislib-metrics`, `contracts/metrics`, `docs/catalog.yml`, `docs/changes/CIS-0017`, `docs/modules/metrics`, `docs/modules/transactions`, `implementations/csharp/src/CIS.Core/Metrics`, `implementations/csharp/src/CIS.Core/Transactions`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests`, `planning/transactions`.
- `postgres-consumer`: P11-P12: Npgsql10.0.4 test-only reference and exact mapping, bounded owned localDockerPostgreSQL18 fixture, independent three-table/isolation/query-cancellation proof and compiling consumer/EFrecipe. Paths: `.artifacts/transactions`, `.cis/local/testing`, `.cis/local/transactions-intake`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0017`, `docs/modules/transactions`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/tests`, `planning/transactions`.
- `public-testing`: P13-P14: finite portable CommonTesting helper, actual outside-friend Core/CommonTesting package consumer, current contract/guide/indexes, checked-in transaction skill/routing and final integration; no new tutorial/harness. Paths: `.artifacts/transactions`, `.cis/local/testing`, `.cis/local/transactions-intake`, `.github/skills/cislib-transactions`, `AGENTS.md`, `README.md`, `contracts/transactions`, `docs/catalog.yml`, `docs/changes/CIS-0017`, `docs/modules/common-testing.md`, `docs/modules/transactions`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/tests`, `planning/transactions`.
- `retained-mappings`: Specifically approved two exact retained-package source mappings only: Microsoft.Extensions.Configuration.EnvironmentVariables and Microsoft.Extensions.Logging.Configuration. Preserve old resolved versions and content hashes; qualify fresh Integration restore under the existing sources, dependency pins and shared totals. Paths: `.artifacts/transactions`, `.cis/local/transactions-intake`, `docs/changes/CIS-0017`, `implementations/csharp/NuGet.config`, `implementations/csharp/tests`.

- All agents and already-performed intake, implementation, native work, tests, formatting, docs, review and restoration share86400active seconds28logical attempts including2700restoration seconds. Codex may organize/coherently split and redistribute within those totals; completed historical owners/costs remain intact.
- Andrew Spiteri explicitly approved the CIS-0017 P01-P14 profile, five-task plan, test-onlyNpgsql10.0.4 and isolatedDockerPostgreSQL fixture, shared24active hours28logical attempts including2700restoration seconds, and samephysicalcounted ceiling amended6to8GiB including1GiB Docker and64MiB reserve, on20November2026. Exact user answer: Approve profile and allocation. Existingdatabase access, externaldeployment, push, merge and release remain excluded.
- Andrew Spiteri subsequently approved retaining the original shared CISLIB-CIS-0001-STORAGE-2026-11-10 ceiling of6442450944bytes with67108864reserve, including1073741824Docker allowance INSIDE6GiB. Exact answer: Approve 6 GiB and exact fixture cleanup. This supersedes the earlier8GiB storage amendment only; time/attempts/restoration/product/dependency scope are unchanged.
- Follow actualsource acceptanceAC01-28. RealDB and platform groups notexecuted remain unverified, never substituted by fakeSQL or scripted lostack. EFis a documentationrecipe only; no full release/mutation/coverage/benchmark campaign is included.
- No new DI/Configuration/transaction framework, host-drain service/registry/scheduler, genericrepository/SQLengine, durablejournal/audit/productionresult/outgoingdispatcher or testharness. CountryData remains sole maintainedtutorial. NoUI/publicHTTPendpoint/manualcatalogue work.
- One transactionowner source writer across first two outcomes; then parallelize only separate DB/telemetry/publichelper files after contracts/hooksfreeze. One coordinator owns sharedproject/pin/lock/catalog files and serializes sharedoutputs. Runtime callbacks and native terminalcleanup remain sequential.
- Preserve current static environment/Core/Workflow/REST/Startup contracts and untrackedCountryData launchSettingsSHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. No existingdatabase/credential use, cloud/externaldeployment, push, merge or release.
- Preserve explicit local context, one ownedlease/native transaction, applicationowned data source, no ambient participation, no nested/savepoint authority, immutable safe outcomes and actualsettlement. Any ordinary failed native commit invocation isUnknown; confirmedcommit survives cancellation/cleanup. No automatic work/commit retry or invented recovery.
- Select Npgsql10.0.4 only in test/reference closure with one new exact source mappingNpgsql. Verified official net10manifest requires Logging.Abstractions>=10.0.0 satisfied by existing10.0.12. No Core/CommonTesting provider dependency, EF/Testcontainers/mocking/Npgsql-telemetry package or SDK/runtime upgrade. Unexpected new package IDs or changed pin require a specific decision.
- The Docker allowance covers selectedimage unpacked512MiB, compressedcache128MiB, owneddata256MiB, logs8MiB and metadata/overhead120MiB. Use exact fresh owner-labelled container/volume and private generated credentials; loopback/local listener only, twoCPU/512MiB/128PIDs/16serverconnections, pool4, threeordinarylogged tables/128rows pertable/1KiB payload. Measurebefore native expansion and preserve durability; only exact newlyowned RM02 resources may be removed. Use task-private Docker client configuration and the known local engine endpoint; public image retrieval does not use existing operator registry credentials.
- The additional approved cleanup is limited to the exact Debug/net10.0/cis-type-manager-fresh and Release/net10.0/cis-type-manager-fresh roots beneath implementations/csharp/tests/CIS.Core.UnitTests/bin, described in storage-resolution-proposal.md. Verify all relevant test hosts exited; retain hash manifest; resolve absolute paths and reject reparse/unexpected entries. Preserve source, user launch settings, current binaries outside those roots, TRX/results/logs, packages and all CIS evidence/accounting. No other historical cleanup, Docker pruning or new physical pool.

The total is 86,400 active seconds and 28 logical attempts, including 2,700 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
