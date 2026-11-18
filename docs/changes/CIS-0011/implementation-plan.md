---
title: CISApplication and CountryData bounded implementation plan
type: delivery-plan
status: Approved
change_id: CIS-0011
---

# Bounded delivery plan

Deliver APP-01–APP-07, DATA-01–DATA-06, CLEAN-01–CLEAN-03 and A01–A14 from the
[preserved specification](../../../planning/application/sources/CISLIB_CISApplication_and_CountryData_Specification_v1.0.md),
as [reconciled](../../../planning/application/README.md) with merged main.
Andrew Spiteri approved this six-task plan and shared allocation on 18 November
2026. Supported CIS operations recorded the authorization and six canonical work
owners; no previous module grant was reopened. The leaves include their tests,
documentation and independent review.

The proposed builder collects existing finite named `RestClientOptions` through
`RestClients`; zero selections create no REST owner. It invents no backend or
unkeyed default. CountryData selects `countries`/`refresh` and intentionally makes
the existing optional TransientHttp policy available through its ordinary
registration seam. The facade owns module ordering/lifetime; retry selection
remains an application choice. Unknown or unavailable configured policy fails
startup rather than silently selecting None.

## Approved shared allocation

One allocation: **12 active hours (43,200 seconds), 16 logical attempts**, including
**1,200 restoration seconds**, shared by the controller, all agents, native work,
tests, docs and review. The controller may organize and redistribute within it.
Retain storage group `CISLIB-CIS-0001-STORAGE-2026-11-10`, the physical
**6,442,450,944-byte ceiling** and **67,108,864-byte restoration reserve**.
All existing counted files/claims remain counted. No historical owner or unused
allowance is reopened, and there is no extra agent/worktree pool.

| Leaf | Coherent outcome / owned files | Dependencies | Seconds / attempts / restoration seconds |
| --- | --- | --- | --- |
| AP1 | Core builder/composition and bootstrap: Application setup/registration code; narrow Configuration defaults/prepend seam; Core Metrics no-op; related unit/architecture tests and guides. Freeze shared signatures and replacement rules. | None | 9,000 / 2 / 180 |
| AP2 | Facade lifecycle and web: Application lifetime/pipeline code and narrow existing hosting seams; retained start/stop/dispose operations, preparation ownership and readiness; focused real-host tests and API guide. | AP1 | 7,800 / 3 / 240 |
| AP3 | Optional adapter composition: one Core registrar contract and optional Metrics registrar/options bridge, existing provider and pipeline; Core-only/optional/disabled/replacement and two-host checks; adapter guide. Coordinate the shared contract with AP1. | AP1 | 4,800 / 2 / 120 |
| AP4 | CountryData: Program, settings, provider mapping, catalog, routes, README and dedicated application tests. Real REST, atomic cache, actual Heartbeat, safe availability, cancellation and two gauges. | AP1; integrated proof after AP2/AP3 | 8,400 / 3 / 180 |
| AP5 | Minimal consumer migration: existing test/package/process/assembly fixtures and distinct replacement source stories; preserve unique module and genuine package boundaries, remove redundant tutorial-report tests. | AP1; final consumers after AP4 | 6,600 / 3 / 240 |
| AP6 | Complete consolidation: solution/shared project/copy targets, example removals, active profiles/scripts, contract routing and root/module/skill guides; final affected build/source/package checks and active-reference review. | AP2, AP3, AP4, AP5 | 6,600 / 3 / 240 |

One controller owns all shared solution/project/package/lock/copy-target edits and
native builds sharing outputs. After AP1's API is fixed, AP2, AP3 and the
application/fixture work may proceed independently with exact file ownership.
The runtime sequence always remains preparation → configuration → one Build →
pipeline → construct all startup roots → sequential starts → readiness.
Integration checks wait for the actual dependencies. No task is a separate test
harness, tooling campaign, approval-only phase or deferred cleanup ticket.

The current distribution transfers 1,200 unreserved seconds each from AP2/AP5 to
AP1 for its actual bounded verification continuation. The original 43,200-second,
16-attempt totals and every restoration reserve remain unchanged. See
work-allocation-core-continuation.json; this is delegated distribution, not a new
human limit increase.

Provide concise implementation checkpoints when core defaults/startup/ownership
work, and when real web/optional integration works. Count failed attempts and
measured costs honestly; narrow failures do not justify unrelated reruns.

## Acceptance and focused verification

| Source acceptance | Owning proof |
| --- | --- |
| A01–A03 | AP1/AP2: all constructor forms and small web Main, one provider, exact prepared identities/default roots in constructors, external store failures retained, construct-all then ordered start-once, failure/cancellation never ready. |
| A04–A05 | AP2/AP3: held initial start through real HTTP, health500/business503 then ready200; customized effective instances; automatic installed adapter/disabled/Core-only modes, one provider and unchanged Core runtime closure without collector/Polly dependencies. The real Type Manager build SDK remains. |
| A06–A08 | AP4: Malta name/ISO2/ISO3 and case/trim variants, stable 400/404, no lookup I/O; actual local HTTP mapped success/business/HTTP/malformed/empty failures, conflicting aliases, atomic replacement and retained old data/time after failure. |
| A09–A10 | AP2/AP4: both default delays one hour, controlled time/no overlap/no duplicate prime; actual in-flight REST cancellation and held cleanup/drain; partial preparation/build/start failure ownership and repeated concurrent cleanup. |
| A11 | AP3/AP4: real exporter scrape, central labels, automatic incoming/outgoing/module observations and exact two gauge names/values; no remote work on scrape, disabled metrics preserve behavior, callback-owner drain. |
| A12–A13 | AP5/AP6: one canonical example, no embedded self-test controls, no disguised old app; named minimal fixtures and all active paths resolved. Historical hits are classified and retained as history. |
| A14 | AP5/AP6: pinned Windows affected builds and native MSTest through the new eligible CIS owner; changed genuine Core-only/optional package build and folder-publish consumers. Record actual platforms/results; no Ubuntu/release claim without execution. |

Use controlled local HTTP, existing test timing and ordinary source/package
boundaries. Retain useful Configuration reload/identity, Type Manager deployment,
Healthcheck transport, Heartbeat failure/recovery/drain, Logging flush and Metrics
compatibility cases. Do not re-prove the entire REST transport, run mutation or
benchmarks, wait an hour, use internet-dependent automated tests or force reuse.
One bounded live-provider smoke is supplementary when network access permits;
failure to reach it is reported separately. No new runtime dependencies, backend,
routing/DI/lifecycle/scheduler framework, release qualification, publication,
remote source push or main merge is included.

## Complete cleanup and retained fixture boundaries

Retire `examples/startup/CountryList`, `healthcheck/CountryList`,
`heartbeat/Polling`, `logging/ConsoleHuman`, `metrics/MetricApi`,
`type-manager/Console`, `type-manager/Web` and newer `rest-client/RestApi` under
`implementations/csharp`. Remove redundant type-manager Contracts/Unused/Shared
support after their named remaining requirements move to existing test fixtures.
Retire or reduce the parent `examples/type-manager/README.md` too; its obsolete
runnable/package commands cannot remain current guidance.
Do not copy their full programs or preserve report/self-test schemas.

| Surviving distinct requirement | Smallest intended retained boundary |
| --- | --- |
| Type discovery, unused/late membership, no construction and missing deployment | Existing `tests/fixtures/TypeManager` Host/Contracts/Transitive/Unused/Runtime; retain only tiny passive roots/markers if a distinct current test needs them. Retarget real package SDK build/publish and ordinary PackageReference controls. |
| Configuration discovery/load-before-construction/reload identity | Existing Configuration module tests plus minimal TypeManager discovery roots/host assertion where needed; replace Console/Web application/report dependency and its three build-only references. Preserve exact candidate-byte verification at surviving process boundaries. |
| HTTP/pipe/probe and process ownership | Existing HealthcheckHttpHost and CisHealthProbe, plus genuine CountryData package consumption. Keep transport-specific tests; remove duplicate CountryList self-test protocol. |
| Human logging/context/output and flush | Existing DiscoveryHost/LoggingBusinessFixture and LoggingBusinessStories plus module tests; no ConsoleHuman report host. |
| Heartbeat false/error/recovery/drain | Existing in-process HeartbeatPollingStories and owner/module tests; canonical sample proves actual scheduled refresh. |
| Optional Metrics and REST deployment/SDK closure | Retarget genuine optional package consumer/source story to CountryData; keep small Core-only REST consumer and existing collector/runtime/logging/trace cases. Temporary copies of the current CountryData source for genuine package builds are allowed; no retained copied old application/order tutorial fixture. |

Migrate references before deletion. Inspect solution, Integration/BusinessCases
project references and copy targets, ExampleLate support, support/contract-targets,
active native profiles/scripts, catalog, root README, all affected module guides
and the existing Metrics skill/instruction links. Replace or retire active old
launch bindings. Prior snapshots, supplied historical requirements, receipts,
closed owners and accounting remain immutable history, not current commands or
evidence for CountryData. Final rg classifies old paths, assembly names, outputs
and self-test flags; no unresolved active reference is a completed outcome.

Three untracked user launchSettings files exist under startup/CountryList,
metrics/MetricApi and rest-client/RestApi. Preserve only those exact files byte for
byte in ignored `.cis/local/application-intake/user-settings/<original path>`
before removing their directories; verify original/preserved hashes and restore
them if cleanup is aborted only into absent or unchanged original paths, never
overwriting later user edits. This reversible three-file disposition is part of the proposed plan,
not an archive of an application. Do not delete other user work. Delete only
identified superseded sources/exclusive build output after resolved-path checks;
this is not authority for historical evidence or general disk cleanup.

The intake storage sample is 5,871,810,059 counted bytes, leaving 503,532,021 bytes
after restoration reserve. Coordinate incremental outputs and actual obsolete
example removal to stay under the same physical ceiling; do not collect duplicate
run bundles. Re-sample before native execution and preserve original accounting.
