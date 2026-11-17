---
title: "Retained POCO Configuration with coordinated refresh"
type: change-proposal
record_format: proportionate
status: "Closed"
change_id: CIS-0008
outcome: "Deliver type-identified retained Configuration POCOs through DI: discover through prepared Type Manager, apply defaults/common/host overrides before consumer construction, and refresh the same instances with synchronized reads, effective checksums, ordered ConfigurationChanged, actual Startup/Heartbeat/lifetime integration and truthful provider/package evidence under the selected contract decisions."
baseline_kind: git
baseline: "4206a870130f577621277847788022e76f1dbe8f"
graph_build_id: "sha256:fd9b45ba6b94f0a95a15a6ee7a39b6f556af21f591bccdf6e084d61c51000d80"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:316f8fe8b7b674e09c9624f7230e90630ba41341045e023b82b022333750282d"
authority: derived-outcome-authorization
---

# CIS-0008: Retained POCO Configuration with coordinated refresh

## Outcome

Deliver type-identified retained Configuration POCOs through DI: discover through prepared Type Manager, apply defaults/common/host overrides before consumer construction, and refresh the same instances with synchronized reads, effective checksums, ordered ConfigurationChanged, actual Startup/Heartbeat/lifetime integration and truthful provider/package evidence under the selected contract decisions.

## Impact roots

Creation retained the exact Git/graph baseline without an initial root. The
subsequent bounded analysis explicitly supplied the imported Configuration source
document and the actual `StartupHostedService` class; see [impact.md](impact.md).
Both proposed findings are covered by the scope submitted for the single outcome
decision. No existing Type Manager or Heartbeat implementation is being replanned.

## Constraints

- This is a proposal; no Configuration execution authority or allocation exists.
- Reuse actual Type Manager, Startup, Heartbeat, Healthcheck and approved Core dependencies. Preserve existing diagnostics, EnvironmentOptions, source bytes and completed work.
- Select the recommended P01–P08 contract profile once, together with this outcome and its overall limits, before freezing public contracts or persisted keys.
- A host supplies fixed application identity, a bootstrap-capable read/watch adapter and an explicit polling interval when polling is used. A production database, filesystem or broker adapter is not selected.
- Keep the coordinator independent of Configuration. The concrete hosting connection closes Configuration before Heartbeat drain and joins actual module/startup/Heartbeat work before teardown.
- Necessary supporting tasks, tests, documentation, review and resource distribution remain inside one delegated shared pool. No per-agent allowance, new framework, scheduler, test harness or tooling campaign is proposed.
- No publication, default-branch merge, external source disclosure or unrelated cleanup is part of this outcome.

## Acceptance criteria

- U01–U07/U12 and CFG-A01–A04/A13/A21/A26: actual prepared Type Manager supplies eligible types; fixed host/type identities select storage; explicit initial loading precedes consumer construction; one populated retained root is reused across scopes and reloads without a temporary container or discovery rescan.
- U08/U10 and CFG-A05–A12/A24: presence-aware defaults/common/application composition, selected mapping and validation, source bookkeeping and deterministic effective checksum handle removals, masked common changes and invalid/Unchanged reads without false events or loss of accepted values.
- U06/U11 and CFG-A14/A18–A20/A22: shared-lock readers observe complete commits; passive setters preserve the supported contract; helper-backed direct-interface/base events use one after-commit local revision and isolate synchronous subscriber failures.
- U09–U11 and CFG-A15–A17/A23/A25: bounded serialized refresh serves polling and real provider notifications, closes activation/reconnect gaps and stopping admission, and waits for actual provider/handler work before protected dependencies are torn down.
- CFG-A27/A28: existing source/package consumers and actual Startup/Heartbeat/Healthcheck boundaries have current affected native evidence; all six verification obligations retain their proper boundary. Production-store availability and unexecuted qualification are reported precisely.

## Concrete proposal and decision

Review [requirements and the P01–P08 bundle](../../../planning/configuration/README.md),
the [four-task ownership and execution plan](../../../planning/configuration/implementation-plan.md),
and the [acceptance map](../../../planning/configuration/acceptance.md).
The complete machine inputs are [authorization-proposal.json](authorization-proposal.json)
and [task-contracts.proposed.json](task-contracts.proposed.json).

One proposed shared allocation is **21,600 active seconds (six hours), eight
logical attempts and the existing physical six-GiB counted-storage ceiling**,
including **600 seconds and 64 MiB** reserved for restoration. These totals cover
all workers, implementation, tests, documentation and review. They are not applied.
Task organization and distribution within these totals would be delegated to Codex.

The supported Draft plan currently contains its generated unresolved seed. After
the single actual outcome/contract/allocation decision, the controller will use
the supplied concrete task inputs to update that seed and add the three dependent
owners through supported CIS operations, without a second plan approval cycle.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-CONFIGURATION-2026-11-17:USER-APPROVED-PROFILE-ALLOCATION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver type-identified retained Configuration POCOs in CIS.Core using the existing prepared Type Manager, defaults/common/fixed-host overlays before consumer construction, synchronized reads and same-instance validated refresh with effective checksums and ConfigurationChanged; bind actual Startup/Heartbeat/Healthcheck/lifetime and existing source/package consumers, with truthful host-supplied-provider and qualification boundaries.

- `configuration`: Implement U01–U12 and CFG-A01–CFG-A28 from the preserved supplied source using the single recommended P01–P08 profile in planning/configuration/README.md, conditional on the actual human decision. Include the four coherent implementation owners in implementation-plan.md, their tests/docs/review, a narrow optional Configuration stop/drain connection in Startup.Hosting, and existing Console/Web consumers. No production storage backend is selected. Paths: `.cis/local/testing`, `.cis/product-documents.json`, `README.md`, `docs/catalog.yml`, `docs/changes/CIS-0008`, `docs/imports/technical/cb1e5fd4a71b4bfe92a5994af4050414`, `docs/modules/configuration`, `docs/modules/heartbeat`, `docs/modules/startup`, `docs/modules/type-management/configuration-boundary.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/examples/startup`, `implementations/csharp/examples/type-manager`, `implementations/csharp/src/CIS.Core/Configuration`, `implementations/csharp/src/CIS.Core/Environment/Resolution`, `implementations/csharp/src/CIS.Core/Startup/Hosting`, `implementations/csharp/tests`, `implementations/csharp/verification/mutation`, `implementations/csharp/verification/qualification`, `planning/configuration`.

- Four at-most-medium coherent tasks include meaningful tests, documentation, independent review and completion. W2 mapping and W3 provider/refresh can run in parallel after W1 freezes contracts; the controller owns shared public/project/package edits and serializes shared native builds. Runtime startup remains sequential.
- Host supplies a bootstrap-capable read/watch adapter and fixed host-project identity. No production database/filesystem/broker adapter, invented polling interval/environment variable/stale threshold, extra configuration scope, secret service, key migration, proxy/source generator, validation language, mapper framework or scheduler is authorized.
- Keep StartupCoordinator independent of module runtimes. The narrow Startup.Hosting connection must close Configuration commit/trigger admission and cancel owned provider work before Heartbeat drain, then join actual Configuration/startup/Heartbeat work before dependency teardown; no generic closing-participant registry and no changed Heartbeat cancellation semantics.
- No package release, default-branch merge or external source disclosure is authorized by this implementation outcome.
- One 21600-second/eight-logical-attempt pool covers every worker, implementation, test, review and documentation action. Included restoration reserve is 600 seconds and 64MiB, not extra capacity. Controller may organize supporting work and redistribute only this same total.
- Preserve all completed CIS-0001 through CIS-0007 owners, approvals, costs, used attempts, evidence and uncertainty. Their scope does not fund Configuration. Record current actual consumption and stop at an actual hard limit or unresolved mandatory contract.
- Preserve one CIS.Core package/assembly, explicit CIS namespaces, net10.0, pinned SDK/dependency closure and genuine Type Manager build/inventory integration. Reuse TypeManager.CreateAsync and implementation queries; no assembly rescan or second DI container. Preserve existing CIS.Configuration diagnostics and EnvironmentOptions behavior.
- Retain the restored source and user handoff bytes; reference requirements and pending P proposals separately. Preserve accepted product baselines and unrelated user launch settings. Dedicated input/planning operations establish no implementation approval.
- Reuse physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10: existing roots, measured files, claims and earlier results remain counted under the same 6GiB ceiling. No second pool, reset of old consumption/attempts, or cleanup of source/user/historical files. Scratch markers are counting permissions; no cleanup controller is delegated.
- Use existing native MSTest and all six verification obligations proportionately. Include focused affected unit/integration/architecture/compatibility/business tests, existing package/actual supported lanes and applicable bounded native mutation at candidate boundaries under this allocation. Preserve PerformanceObservation/ExtendedStress exclusions and existing 95/90 coverage and 90 mutation release gates; do not claim full release qualification from scoped passes or test providers.
- User-selected contract bundle: P01 explicit async pre-DI initial load; P02 persisted full type plus assembly name/culture/token without assembly version, exact CLR Type internally and no migration/fallback; P03 presence-aware defaults/common/application JSON composition, recursive nested members and full collection replacement; P04 shared private-gate read helper/passive setters; P05 typed local-revision event with explicit helper-backed direct-interface accessors and optional base; P06 separate source state and deterministic effective SHA-256; P07 no initial event, last accepted values on invalid refresh and isolated after-commit handlers; P08 once-captured defaults, eligible parameterless construction or explicit exact-type factory. Andrew Spiteri explicitly approved this complete bundle with the shared allocation on 17 November 2026.

The total is 21,600 active seconds and 8 logical attempts, including 600 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts/configuration/CIS-0008` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
