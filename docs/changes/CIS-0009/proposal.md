---
title: "Bounded portable Metrics and Prometheus integration"
type: change-proposal
record_format: proportionate
status: "Closed"
change_id: CIS-0009
outcome: "Deliver portable CIS Metrics primitives and an optional OpenTelemetry Prometheus integration exposing /_metrics with authoritative identity and finite collection/scrape limits; automatically observe real existing service and incoming HTTP boundaries while preserving startup, readiness, constructors and shutdown; include contracts, focused conformance/tests, one footprint observation, example, docs and agent routing without implementing future RestClient/database services."
baseline_kind: git
baseline: "1f72e9ea4d8630edacb59c01795ebc8467807cd8"
graph_build_id: "sha256:e1de17cd576293665a871ce5385a0b27c2e035b4431a60ddb64d8a3164368437"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:01730dcf13b24f85522c0e7a0dd4161b5d907438a19aa4e8ce65b7568ba8a757"
authority: derived-outcome-authorization
---

# CIS-0009: Bounded portable Metrics and Prometheus integration

## Outcome

Deliver portable CIS Metrics primitives and an optional OpenTelemetry Prometheus integration exposing /_metrics with authoritative identity and finite collection/scrape limits; automatically observe real existing service and incoming HTTP boundaries while preserving startup, readiness, constructors and shutdown; include contracts, focused conformance/tests, one footprint observation, example, docs and agent routing without implementing future RestClient/database services.

## Impact roots

Existing concrete hosting/identity boundaries and the preserved Metrics sources
are reviewed in [the reconciliation](../../../planning/metrics/README.md).
The new module/package do not yet exist. Deterministic impact proposals remain
Draft evidence, not adopted implementation authority.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- Preserve Core's exact existing dependency closure and module behavior. Proposed
  third-party dependencies belong only to the optional Metrics integration.
- Retain finite v1 defaults and actual identity, native aggregation/HTTP recording,
  existing sequential startup and actual callback-owner lifetime settlement.
- No custom metrics engine/exporter, future RestClient/database implementation,
  extra scheduler, test harness, benchmark product or full release campaign.
- All workers, tests, documentation and review share one proposed finite pool.
  Completed owners/allowances are not reusable and source preservation is required.

## Acceptance criteria

- MET-01–MET-06: portable numeric/definition/timer semantics and SDK-independent
  consumer API; optional native SDK/exporter; actual reserved endpoint; exact
  application/environment on every sample; validated finite catalog and scrape
  bounds with visible overflow and business-safe invalid recording.
- MET-07/08/11: native incoming request statistics, real six-module observations,
  preserved constructors/results/readiness/ordering, no bootstrap replay, and
  real host-isolated activation/collection/shutdown barriers.
- MET-09/10/12 and A-01–A-14: portable future-service semantics, useful cumulative
  queries, coherent contracts/fixtures/compiled example, concise repository skill
  and routing, focused native evidence and one measured default-catalog footprint.
  Do not claim unexecuted platform/package/coverage/mutation/release results.

## Concrete proposal and pending decision

Review [requirements/profile](../../../planning/metrics/README.md),
[three outcomes and five bounded leaves](../../../planning/metrics/implementation-plan.md)
and [acceptance](../../../planning/metrics/acceptance.md). The source documents are
preserved there byte-for-byte. The optional SDK/exporter selection is proposed
1.21.0/1.21.0-beta.1, including its prerelease posture and actual restore verification.

Request one shared **36,000 active seconds / 12 logical attempts**, including
**900 seconds / 64 MiB restoration**, under the existing physical **six-GiB**
counted-storage ceiling. Codex may organize tasks and distribute only those same
totals after approval. No allocation or human authorization is recorded yet.

The installed feature-intake preview rejected this authority-repository target;
the proposed in-place change preserves the registered workspace and accepted
product baseline. The generated WORK-001 seed is not an executable task contract.
Concrete ownership/acceptance is prepared above for supported update/add after
one actual outcome/profile/allocation decision, without separate approval loops
for every derived task.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-METRICS-2026-11-17:DIAGNOSTICSOURCE-TRANSITIVE-MAPPING-CORRECTION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver MET-01 through MET-12 and A-01 through A-14 using the supplied finite v1 Metrics profile: portable Core primitives, optional OpenTelemetry Prometheus integration, authoritative identity on every sample, bounded catalogs/scrapes, automatic native HTTP and six existing service observations, true host/callback lifetime, focused conformance/example/skill/docs and one footprint observation.

- `metrics`: Implement the supplied Metrics specification/guide as reconciled in planning/metrics. Keep the three coherent outcomes with five bounded leaves, their tests/docs/review and early identity/drain proofs. Explicitly add only the optional CIS.Metrics.OpenTelemetry package and its verified pinned SDK/exporter closure; keep Core closure and all existing modules compatible. Paths: `.cis/local/testing`, `.github/skills/cislib-metrics`, `AGENTS.md`, `README.md`, `conformance/metrics`, `contracts/metrics`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0009`, `docs/modules`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Build.props`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/eng`, `implementations/csharp/examples/metrics`, `implementations/csharp/src/CIS.Core`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests`, `implementations/csharp/verification/mutation`, `implementations/csharp/verification/qualification`, `planning/metrics`.
- `metrics-package-sources`: Only the explicitly approved one-file NuGet.config source-mapping support for the existing optional Metrics package; no change to original Metrics contracts or limits. Paths: `implementations/csharp/NuGet.config`.

- Establish actual callback admission/drain before owner teardown. Timeout and provider disposal are not proof of callback settlement. Preserve sequential Startup, pre-DI use and constructors; snapshot early facts without replay or second provider.
- Optional SDK 1.21.0/exporter 1.21.0-beta.1 is the proposed compatible pinned selection; prerelease status is explicit and actual locked restore/TFM/native behavior must be verified. No third-party SDK or new runtime NuGet in Core; preserve current 10.0.401/10.0.12 pins and existing approved Core closure.
- Preserve all completed CIS-0001 through CIS-0008 owners and user launch settings; raw source bytes remain unchanged. No package release, default-branch merge, external source disclosure or remote publication is authorized.
- Preserve future RestClient/database/logical-transfer requirements without implementing those services; no custom metrics engine, serializer/exporter, history buffer, scheduler, autoscaler, dashboard or generic lifecycle framework.
- Preserve supplied MET/A requirements and proposed finite v1 defaults. Consumer APIs remain CIS-owned; native BCL/SDK own aggregation, overflow and encoding. Exact names are finalized with compiling examples and portable fixtures.
- Reuse physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10 and all currently counted roots/files/claims under 6442450944 bytes, including 67108864 restoration bytes. No second pool, historical allowance reuse, counter reset or cleanup controller.
- Three coherent outcomes may be organized as five medium leaves; task organization and redistribution stay within one shared 36000-second/12-attempt grant including 900 restoration seconds. Every agent, test, review and document shares these totals.
- Use controlled host-owned Meter instances and one effective policy view; prove two-host isolation. Mandatory identity comes only from EnvironmentOptions and appears on all supported exported samples. No arbitrary public scopes or global latest-provider pointer.
- Use existing MSTest/native/package mechanisms and proportional six-category checks; include only the specified focused defaultcatalog footprint observation. Preserve 95/90 coverage and 90 mutation release gates, prior results/gaps and actual platform distinctions. Do not build another harness or run a full release campaign.

The total is 36,000 active seconds and 12 logical attempts, including 900 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts/metrics/CIS-0009` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
