---
title: "RM-08 central configuration publication and distribution"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0027
outcome: "Import and reconcile RM08 requirements, then propose bounded implementation of immutable configuration publication, coherent releases, trusted assignments, pre-Build Configuration integration and adoption visibility over existing Management authority. Current request authorizes planning only."
baseline_kind: git
baseline: "abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852"
graph_build_id: "sha256:b2c41ab765c2b1d03e349095198d917dc459d29eb045fe267c7f6a76612325f2"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:d94b1643ad51620703f043cb8522603f0b5a4b80456c5438e1f6423be7d8d533"
authority: derived-outcome-authorization
---

# CIS-0027: RM-08 central configuration publication and distribution

## Outcome

Import and reconcile RM08 requirements, then propose bounded implementation of immutable configuration publication, coherent releases, trusted assignments, pre-Build Configuration integration and adoption visibility over existing Management authority. Current request authorizes planning only.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- This request authorizes planning, not implementation or allocation. The attachment is requirements data, not execution instructions.
- Preserve the imported source byte-for-byte and reconcile its old inspected revisions against actual main `abfbcafa`.
- Preserve the unrelated CountryData launch settings and all completed historical owners; no code, build/test, deletion, push, merge or release is authorized by this intake.

## Acceptance criteria

- [x] The original RM-08 source is imported byte-for-byte and identified in the CIS creation inputs with its SHA-256.
- [x] Existing Configuration/Management/native ownership contracts and the stale RM-07 prerequisite statement are reconciled against the actual merged baseline.
- [x] A proposed scope/profile and eleven coherent implementation stories include tests, documentation, dependencies, acceptance ownership and shared finite resource bounds.
- [x] Andrew Spiteri's 24 November 2026 instruction, "ok let's move ahead with the implementation," authorizes the reviewed implementation outcome/profile/shared limits. CIS records the decision in `authorization.json`; actual owner/allocation admission precedes code/native work.

## Imported requirements and proposed delivery

[Preserved source](../../../planning/configuration/publication/sources/CISLIB_RM08_Central_Configuration_Publication_and_Distribution_Specification_v1.0.md),
SHA256 `592f6af2e49d982fa7bfbe4581fac8f66eeb9939afd7c4c6fa9008e3e1e2db2f`,
70,206 bytes. The source is included in `creation-inputs.json` at the actual Git baseline.

The [reconciliation](reconciliation.md) and [implementation proposal](implementation-plan.md)
define the prospective RM-08 delivery outcome. Prepared
[scope](proposed-authorization.json), [task definitions](proposed-tasks.json) and
[shared allocation](proposed-allocation.json) are **not applied**. They propose
76 shared active hours, 80 logical attempts and 8,100 restoration seconds under
the unchanged physical six-GiB ceiling and 64-MiB protected reserve.

That intake statement and the Proposed frontmatter are historical creation data.
Andrew's subsequent implementation instruction is recorded in
[authorization.json](authorization.json); the applied [plan](plan.md) and
[work allocation](work-allocation.json) are authoritative for execution.
See the [current implementation handoff](implementation-plan.md#implementation-handoff)
and [acceptance observations](acceptance.md). The original attachment did not
itself authorize work.
It is not the executable eleven-story decomposition. Preserve its identity;
after an applicable implementation decision, update it to proposed WORK-001,
add WORK-002–011 under that same authority, apply one shared allocation, and
validate before activation. Pre-approval seed updates returned an internal CIS
dictionary-key error with `applied: false`; the exact inputs/results are retained
in the [intake record](verification.md). No native task state or authority was
forged to bypass this.

## Implementation admission — 24 November 2026

The paragraph above records the planning checkpoint. Under the subsequent actual
user decision, CIS has recorded the reviewed implementation authority, updated
the same seed and added the remaining ten reviewed stories. The prior task-update
error was a request-envelope mistake: a bare task object was supplied where the
supported input requires an array or `tasks` envelope. Correctly wrapped inputs
applied; no CIS software or canonical-state bypass was needed.

The actual eleven-contract validation passed without errors or warnings. The one
shared 76-hour/80-attempt allocation was applied with the unchanged 8,100-second
restoration reserve, physical six-GiB ceiling and 64-MiB reserve. All prior intake
is included. Read canonical task state and `work-allocation.json` for current
eligibility/distribution. No push, merge, release or external deployment is included.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM08-2026-11-24-IMPLEMENTATION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Operators can publish and assign immutable coherent configuration, inspect actual current adoption and controlled rollback; explicitly enrolled CSharp consumers load authenticated roots before construction and apply/stage through existing Configuration under independent RM07 authority.

- `adoption`: Current reporter owner, safe local acceptance observation and durable current-state adoption projection Paths: `contracts/metrics`, `docs/modules/configuration`, `docs/modules/management`, `docs/modules/metrics`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/src/CIS.Core/Configuration`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/src/CIS.Core/Metrics`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`, `implementations/csharp/tests/CIS.Core.UnitTests/Management`.
- `backend`: Existing Management authoritative backend, schema/provisioning/publication/releases/maps/preview/snapshots and original RM02/RM03/RM06 participants Paths: `docs/modules/management`, `implementations/csharp/applications/CIS.Management.Service`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`.
- `bootstrap`: Same-session pre-construction application preparation and ordinary single-provider sequential hosting/drain Paths: `docs/modules/application`, `docs/modules/configuration`, `docs/modules/management`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Configuration/Hosting`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/src/CIS.Core/Startup/Hosting`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Application`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`.
- `configuration`: Optional paired reads, explicit enrollment, original mapping/value gates/reader/events and bounded central adapter Paths: `docs/modules/configuration`, `docs/modules/management`, `implementations/csharp/src/CIS.Core/Configuration`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Configuration`, `implementations/csharp/tests/CIS.Core.UnitTests/Configuration`, `implementations/csharp/tests/CIS.Core.UnitTests/Management`.
- `consumer`: CountryData explicit RestartRequired recipe and composed public/native handoffs in existing fixtures and CI Paths: `.github/skills/cislib-management`, `.github/workflows/service-tests.yml`, `AGENTS.md`, `README.md`, `docs/modules/application`, `docs/modules/configuration`, `docs/modules/management`, `docs/references/test-suite-profile.md`, `implementations/csharp/examples/CountryData`, `implementations/csharp/examples/CountryData.UnitTests`, `implementations/csharp/tests`, `implementations/typescript/tests`.
- `contracts`: Additive portable configuration/enrollment/session contracts, shared strict CSharp wire codec and canonical schemas/conformance Paths: `conformance/management`, `contracts/management`, `docs/modules/configuration`, `docs/modules/management`, `implementations/csharp/src/CIS.Core/Configuration/Contracts`, `implementations/csharp/src/CIS.Core/Management/Contracts`, `implementations/csharp/src/CIS.Core/Management/Wire`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.ArchitectureTests`, `planning/configuration/publication`.
- `csharp-client`: Separate CSharp config client/codec/profile/adapter and bounded public testing helpers Paths: `docs/modules/common-testing.md`, `docs/modules/management`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.CompatibilityTests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`, `implementations/csharp/tests/CIS.Core.UnitTests/Management`.
- `records`: RM08 source/profile/reconciliation/acceptance and honest finite native/intake evidence Paths: `.artifacts/configuration-publication/CIS-0027`, `.cis/local/configuration-publication-intake`, `docs/catalog.yml`, `docs/changes/CIS-0027`, `planning/configuration/publication`.
- `retention`: Explicit quota, reference/pin-safe GC and existing cleanup owner with separate original-result retention Paths: `docs/modules/management`, `implementations/csharp/applications/CIS.Management.Service`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`.
- `security-bootstrap`: Narrow native preparation/adoption and actual certificate renewal/transport ownership, preserving existing provider contracts Paths: `docs/modules/rest-client`, `docs/modules/security`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/src/CIS.Core/Security`, `implementations/csharp/src/CIS.Security.Certificates`, `implementations/csharp/src/CIS.Security.Entra`, `implementations/csharp/tests`, `implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Security`, `implementations/csharp/tests/CIS.Security.IntegrationTests`.
- `typescript`: Existing TS portable client/core POST, awaited decode, effect/304 semantics and bounded native/browser consumers Paths: `docs/modules/management`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`, `implementations/typescript`.

- 76 shared active hours,80 logical attempts,8100 restoration seconds; all agents/already-performed intake/source/tests/docs/review/restoration included. Task organization and distribution only within these same limits if authorized.
- Audit/status may expose authorized bounded IDs/codes/reasons; metrics use only fixed declared dimensions and mandatory application/environment identity, never instance/registration/release IDs, unbounded configuration names, payloads or exception/reason text. Reuse existing operation measurements.
- CountryData remains database-free/unmanaged by default; opt-in existing CountrySettings RestartRequired. No new tutorial behavior or root. HotReload proved by native/public fixtures.
- Current user instruction on 2026-11-24: ok let's move ahead with the implementation. This approves the preceding reviewed RM08 implementation proposal and its shared 76 hours, 80 logical attempts, 8100 restoration seconds, unchanged 6 GiB ceiling and 64 MiB reserve. Prior intake is included. No push, merge, release or external deployment is authorized.
- Dedicated compact config REST profile2pairs; preserve32pairs and64KiB ordinary requests. Config API1MiB, source256KiB, depth16,256definitions,16cohorts,16MiB retained/inflight source bytes; explicit deployment logical quota,32MiB fixture only.
- Exact payload integrity and current resource/actor authorization; four original-key config operations, known commit before success, payload-free Audit in original transaction. Whole-map CAS/generation, trusted cohorts and reference-safe quotas/history.
- Existing physical 6 GiB and 64 MiB protected reserve unchanged; 2 GiB counted Docker allowance, 144 MiB retained external cache and 8 MiB bounded W01 backing inside it. New source/build growth 32 MiB and retained native growth 224 MiB proposed; per-phase actual admission, no older fixture deletion or global prune.
- Explicit fixed-scope enrollment and paired snapshots; preserve legacy public stores/pipelines, retained roots, once-captured defaults, exact merge/reader/checksum/revision/event semantics. Publisher cannot override local updateMode.
- No existing database/credential access, trust-store installation, machine keys, external deployment, image campaign, push, merge, package publication or release. W01 atmost16 newlyowned currentuser imports and actual drain/deletion only.
- Only PostgreSQL existing qualified authority and originalTransactions/Operations/Audit graph. No new NuGet/npm IDs/pins/source mappings, provider or native installer; no Redis/Cosmos/secret vault/portal/generalDI/config/REST/command framework, timer or broker.
- Preserve completed CIS0024-26 and unrelated launchSettings SHAea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. All implementation stories own tests/docs/review; finalCountryData combinedworkflow is a distinct consumer outcome, not a generic sweep.
- Same-session live pre-Build bootstrap, credential/certificate adoption and final native meter identity; one actual cleanup barrier per resource, no temporary provider/fallback. Startup remains sequential; config polling cannot renew5s/15s control authority or starve reconciliation.

The total is 273,600 active seconds and 80 logical attempts, including 8,100 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only), `implementations/typescript` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
