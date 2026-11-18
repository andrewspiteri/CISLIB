---
title: "CISApplication, CountryData and complete example consolidation"
type: change-proposal
record_format: proportionate
status: "Closed"
change_id: CIS-0011
outcome: "Deliver a small CISApplication over the existing host, one provider, sequential Startup and existing module ownership; one real REST-backed CountryData application with atomic local lookup and hourly Heartbeat refresh; remove superseded runnable tutorials and migrate all active consumers to the canonical app or minimal distinct test fixtures. Include focused tests, docs and review without a new framework, backend, harness or release campaign."
baseline_kind: git
baseline: "86a97e047e2a17590c8187cfeb7f441ea0e7646f"
graph_build_id: "sha256:0dbed0e4cc8c4e4b5fe5f2f7470be717e270917d879250939ca02f5b5287a84c"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:10831f1355c9ec9671a802751d4312bca30d2d2db59d8fcef44d3dc2e977f5ee"
authority: derived-outcome-authorization
---

# CIS-0011: CISApplication, CountryData and complete example consolidation

## Outcome

Deliver a small CISApplication over the existing host, one provider, sequential Startup and existing module ownership; one real REST-backed CountryData application with atomic local lookup and hourly Heartbeat refresh; remove superseded runnable tutorials and migrate all active consumers to the canonical app or minimal distinct test fixtures. Include focused tests, docs and review without a new framework, backend, harness or release campaign.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver APP-01–APP-07, DATA-01–DATA-06 and CLEAN-01–CLEAN-03 from the preserved supplied specification, reconciled against the current checkout.
- Satisfy A01–A14 through the focused proofs and complete cleanup in [the bounded implementation plan](implementation-plan.md); no facade-only or additional-example partial completion.
- Preserve existing public module contracts, dependency closure, native workflow, historical evidence and completed-work accounting. No new framework, configuration backend, test harness, release campaign, publication or main merge.
- This Proposed dossier records authorized preparation. A finite shared outcome/allocation decision is still required before executable work is derived; no prior module grant is reused.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-APPLICATION-2026-11-18:USER-APPROVED-PLAN-ALLOCATION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver a small CISApplication over the existing host, one provider, sequential Startup and existing module ownership; one real REST-backed CountryData application with atomic local lookup and hourly Heartbeat refresh; remove superseded runnable tutorials and migrate all active consumers to the canonical app or minimal distinct test fixtures. Include focused tests, docs and review without a new framework, backend, harness or release campaign.

- `application-facade`: APP-01–APP-07: Core CISApplication builder, explicit defaults-only Configuration, ordinary core registrations, retained one-host lifecycle/web pipeline and one narrow installed Metrics registrar. Existing module contracts/options, optional disablement and supported replacements remain authoritative; no new runtime dependency. Paths: `conformance/application`, `contracts/application`, `docs/catalog.yml`, `docs/changes/CIS-0011`, `docs/modules`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/CIS.Core.csproj`, `implementations/csharp/src/CIS.Core/Configuration`, `implementations/csharp/src/CIS.Core/Healthcheck`, `implementations/csharp/src/CIS.Core/Heartbeat/Hosting`, `implementations/csharp/src/CIS.Core/Logging`, `implementations/csharp/src/CIS.Core/Metrics`, `implementations/csharp/src/CIS.Core/Startup/Hosting`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests`, `planning/application`.
- `country-data`: DATA-01–DATA-06: one normal CountryData application using the facade, actual accepted REST and explicit existing optional retry, atomic validated snapshots, hourly real Heartbeat, safe availability/cancellation and exactly two application-state gauges. Include focused tests, guide and actual package-consumer evidence. Paths: `conformance/application`, `contracts/application`, `docs/catalog.yml`, `docs/changes/CIS-0011`, `docs/modules`, `implementations/csharp/examples/CountryData`, `implementations/csharp/tests`, `planning/application`.
- `example-consolidation`: CLEAN-01–CLEAN-03: migrate distinct behavior to existing minimal test/process/assembly/package fixtures; remove all eight superseded tutorials and redundant support; update every active solution/project/test/profile/script/guide/skill reference. Preserve three exact user launch files reversibly outside removed examples; retain historical evidence and accounting. Paths: `.cis/local/application-intake`, `.cis/local/testing`, `.github/instructions`, `.github/skills/cislib-metrics/SKILL.md`, `AGENTS.md`, `README.md`, `docs/catalog.yml`, `docs/changes/CIS-0011`, `docs/modules`, `docs/references`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/build`, `implementations/csharp/eng/fixtures`, `implementations/csharp/examples`, `implementations/csharp/support`, `implementations/csharp/tests`, `implementations/csharp/verification`, `planning/application`, `planning/decisions.md`.

- Before obsolete path removal, preserve only the three pre-existing untracked launchSettings files under startup/CountryList, metrics/MetricApi, rest-client/RestApi in ignored .cis/local/application-intake/user-settings with original relative paths, identical hashes and restore-on-abort. No unrelated user-file deletion or historical evidence cleanup.
- Complete cleanup in this change: remove the seven named tutorial projects plus newer RestApi and redundant type-manager support. No copied whole applications, replacement archives, self-test flags/reports/barriers in CountryData or temporary duplicates. Minimal surviving fixtures require a named distinct process/assembly/package requirement.
- Constructors collect setup without async bootstrap. Type Manager then Configuration initial load precede one final provider/Build. Construct all scheduled roots before sequential IStartable calls in registration order. Keep the coordinator independent of concrete optional modules, readiness and retained start failure/cancellation semantics.
- Core closure, SDK10.0.401/runtime10.0.12 pins and approved optional OpenTelemetry/Polly versions remain unchanged. No new NuGet, DI/lifecycle/configuration/backend/plugin/routing/scheduler framework or runtime dependency on CIS. Optional registration is a small deterministic hook over prepared Type Manager; no arbitrary scanning/loading/provider/startup.
- CountryData validates provider business success and complete data before one atomic snapshot swap, exact ordinal-ignore-case local aliases, stable400/404 codes, same catalog for startup/lookup/refresh. Failed refresh retains data/time/availability; initial failure keeps readiness closed. Settings captured for catalog lifetime, no live rescheduling or history.
- Defaults-only Configuration is explicit with no external reads/watch/polling; reuse retained roots/reader/validation. Real selected stores retain existing failure/checksum/identity/refresh guarantees and never silently fall back. Configuration scope is host assembly name, telemetry identity remains EnvironmentOptions.
- Deliver the supplied APP/DATA/CLEAN requirements and A01–A14 as reconciled against main86a97e047e2a17590c8187cfeb7f441ea0e7646f. Preserve both source inputs byte-for-byte. Six coherent medium leaves include their tests/docs/review; task organization and redistribution are delegated only within this single shared grant.
- No invented REST backend or default retries. Collect existing finite named client options and use ordinary keyed clients. CountryData selects countries/refresh and the existing TransientHttp GET policy explicitly; overall30second request timeout includes retry/body/cleanup. Default Heartbeat InitialDelay and Interval both1hour, completion-based and nonoverlapping.
- One host/provider and one retained disposal path. Respect supported Replace/RemoveAll/disable choices and alias identity, distinguish AddSingleton from replacement. Actual drains and owned cancellation precede dependency disposal; no timeout or caller-wait cancellation is proof of settlement. Dispose failures propagate; RunAsync reports only observed path outcomes.
- One shared43200active-second/16logical-attempt ceiling includes1200restoration seconds and every controller/agent/native/test/doc/review cost. Retain physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10, all current counted roots/claims, maximum6442450944bytes including67108864restoration bytes. No counter reset, second pool, extra worktree or historical allowance reuse.
- Preserve closed CIS-0001 through CIS-0010 owners, all prior costs/attempts/receipts and history. Update or retire current launch/profile/script references without relabeling prior passes or forcing reuse. No publication, remote source push, default-branch merge, full release/coverage/mutation/benchmark campaign or new personal skill.
- Use existing native MSTest, local controlled HTTP and timing fixtures; coordinate shared outputs and actual Core-only/optional package build/folder-publish boundaries. No public-internet automated dependency or real-hour waits. A supplementary bounded live-provider smoke reports reachability separately. Preserve95/90coverage and90mutation release obligations without claiming them satisfied.
- Use the existing Metrics skill and automatic native/module observations with central application/environment labels. Add only app.country_cache.entries Count and app.country_cache.last_success_timestamp Seconds, no dimensions, no I/O in callbacks. Omit unknown last success; prove real exporter names and owner drain.

The total is 43,200 active seconds and 16 logical attempts, including 1,200 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
