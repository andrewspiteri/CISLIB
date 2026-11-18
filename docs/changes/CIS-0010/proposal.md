---
title: "Bounded RestClient execution and outgoing HTTP integration"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0010
outcome: "Prepare REST-01 through REST-10 and A01 through A22 for a portable CIS RestClient, native HTTP/content and effective cancellation, separately optional explicit retry execution, bounded automatic Metrics and real consumers. Preserve current module contracts and source inputs; implementation requires a later decision on the concrete plan, scope and finite shared allocation."
baseline_kind: git
baseline: "7b5923c031027b932f730e1cf45b2d51270b17cc"
graph_build_id: "sha256:887b3ae20a87ae34b9c0a99cd53df2d5d6855f2a5426fa1c27b4e61377e2c2a2"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:24b2f9ea49a3c5ccea6ea4cb53d5d1883fd19474073abc6d02a606c5cac7906c"
authority: derived-outcome-authorization
---

# CIS-0010: Bounded RestClient execution and outgoing HTTP integration

## Outcome

Prepare REST-01 through REST-10 and A01 through A22 for a portable CIS RestClient, native HTTP/content and effective cancellation, separately optional explicit retry execution, bounded automatic Metrics and real consumers. Preserve current module contracts and source inputs; implementation requires a later decision on the concrete plan, scope and finite shared allocation.

## Impact roots

Bounded deterministic review covers the existing Metrics collection context and
stream catalog, and the Core Startup/Heartbeat hosting cutoffs. The findings in
[impact.md](impact.md) remain proposed; they do not adopt scope or approve execution.
The detailed new-module/package scope is in [the implementation proposal](implementation-plan.md).

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- Current user authority covers supplied-source intake, reconciliation and plan
  preparation. No RestClient implementation grant is recorded.
- Preserve Core's dependency closure, runtime pins, existing module contracts and
  sequential startup. Keep optional retries and the existing one-provider Metrics
  integration separate from ordinary consumer APIs.
- No new HTTP/resilience/DI/lifecycle framework, test harness, Configuration
  module, installable skill, broad qualification campaign or historical cleanup.
- The proposed twelve-hour/sixteen-attempt allocation is shared by all tasks and
  agents, includes restoration, and retains the physical six-GiB ceiling.

## Acceptance criteria

- REST-01–REST-07: native C# HTTP/content facilities behind portable contracts;
  guarded typed results, explicit absence/null semantics, ordered customization
  and authentication, typed expected failures and visible programming defects.
- REST-08–REST-09: explicit inherit/None/replacement policies, one upstream retry
  owner, replay and server-minimum delay rules, and entry-based cancellation that
  awaits actual owned cleanup before result completion or resource teardown.
- REST-10: one logical duration, balanced active work and actual payload counts;
  bounded exact-host outgoing native selection with mandatory identity and no
  duplicate provider or unbounded URL/operation state.
- A01–A22: meaningful focused component, actual host/export and genuine package
  consumer evidence, with matching portable fixtures, runnable examples and
  documentation; preserve actual platform, qualification and accounting limits.

## Review package

The [preserved sources and actual intake state](../../../planning/rest-client/README.md),
[P01–P12 reconciliation](../../../planning/rest-client/reconciliation.md),
[seven proposed task contracts and recommended limits](implementation-plan.md),
[acceptance map](acceptance.md) and [authorization proposal](authorization-proposal.json)
are preparation for a later implementation decision. The generated Draft WORK-001
seed is not the seven-leaf implementation plan or executable authority.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RESTCLIENT-TRACE-2026-11-18:USER-APPROVED-30-MINUTES`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Deliver supplied REST-01 through REST-10 and A01 through A22 under user-approved reconciliations P01-P12: portable Core REST results/options/extensions, native content and real cancellation/owner drain, separately optional explicit upstream retries, bounded exact-host outgoing/logical/payload Metrics, and actual source/package consumers. No custom framework, harness, Configuration module, new skill or release campaign.

- `rest-client`: Approved RestClient module scope with seven coherent implementation leaves, their tests/docs/review, exact optional Polly dependency/source mapping, conditional outgoing Metrics and narrow existing hosting cutoff changes. No new Core NuGet or generic infrastructure. Paths: `.cis/local/testing`, `.github/skills/cislib-metrics/SKILL.md`, `AGENTS.md`, `README.md`, `conformance/metrics`, `conformance/rest-client`, `contracts/metrics`, `contracts/rest-client`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0010`, `docs/modules/heartbeat`, `docs/modules/metrics`, `docs/modules/rest-client`, `docs/modules/startup`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/examples/rest-client`, `implementations/csharp/src/CIS.Core/CIS.Core.csproj`, `implementations/csharp/src/CIS.Core/Heartbeat/Hosting`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/src/CIS.Core/Startup/Hosting`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/src/CIS.RestClient.Resilience`, `implementations/csharp/tests`, `implementations/csharp/verification/mutation`, `implementations/csharp/verification/qualification`, `planning/decisions.md`, `planning/rest-client`.
- `rest-client-logging`: Andrew explicitly requested automatic REST completion logging and approved an additional shared active hour on18November2026. The numeric total46800seconds supersedes the original43200-time constraint only;16attempts,1200restorationseconds and6GiB/64MiB storage limits remain. Deliver the reviewed one-terminal-summary format through existing ILogger/Core provider, actual duration/primary counts, finite Info/Error labels and credential-safe URI, including a borrowed standalone factory overload, meaningful focused tests and documentation. No new logging runtime/provider, dependencies, retry/lifecycle framework or tooling campaign. Paths: `.cis/local/testing`, `docs/changes/CIS-0010`, `docs/modules/rest-client`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient`.
- `rest-client-tracing`: Andrew explicitly requested optional trace-ID forwarding, off by default except the final .local hostname, and approved30additional shared active minutes:48600seconds/13.5hours total supersedes prior time statements only. Keep16attempts,1200restorationseconds and6GiB/64MiB storage limits/history. Add nullable client/call ForwardTraceId with per-call override and final-host automatic policy. Forward existing entryW3Ctraceparent before signing; no fabricated IDs/spans/baggage/tracestate or implicit native propagation. Preserve explicit manual traceparent and nativeMetrics, ownership, deadlines/retry, existing contracts. Include focused tests/docs/review; no tracer/provider/framework/package, DNS hooks/hosts edits, tooling campaign or publication/merge. Paths: `.cis/local/testing`, `docs/changes/CIS-0010`, `docs/modules/rest-client`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient`.

- Andrew Spiteri explicitly approved the RestClient plan and proposed allocation on 17 November 2026 and directed implementation: 43200 active seconds, 16 logical work attempts, 1200 seconds included restoration, under the existing physical 6-GiB ceiling. The reviewed plan includes P01-P12, its optional Polly.Core dependency candidate and bounded public-API retry proof; no native conformance is presumed.
- Begin RC4 with at most 1800 active seconds and one logical work attempt for the public-API delay proof, inside its own 7200-second/three-attempt proposed allocation. Preserve max(local jitter, Retry-After), one chosen delay for admission/waiting, actual cleanup and terminal cause. Unsupported semantics stops retry-dependent work; no copied engine, silent relaxation or additional helper package.
- CIS represents external counted roots as declared native scratch; cleanupControllers remains empty and this representation does not authorize deletion of historical/user artifacts. Counted directories, 6-GiB ceiling, 64-MiB restoration reserve and no-cleanup constraint are unchanged.
- Keep Core Logging/DI abstractions 10.0.12 and its approved ASP.NET framework unchanged. The optional package candidate is Polly.Core 8.9.1, with a net10.0 dependency group containing no further packages, one exact NuGet mapping and normal lock. No Metrics dependency exception or new Core package is reused. Keep SDK 10.0.401 and runtime 10.0.12.
- Keep runtime startup/request stages sequential. Signal REST stopping in existing Startup/HB hosting before waiting for HB/Configuration, then join actual cleanup. Keep the coordinator independent; do not add a generic lifecycle-participant registry or DI framework.
- One user-approved 43200-active-second/16-logical-work-attempt shared allocation includes 1200 restoration seconds and every worker, test, document and review. Codex may organize tasks and redistribute within this same total. Preserve actual consumption, earlier closed owners and original authority; no reset, extra pool or renewed unchanged approval.
- Preserve both existing user launchSettings files and all closed CIS-0001 through CIS-0009 records. This decision authorizes the scoped RestClient implementation and necessary tests/documentation/review. It does not approve product baseline/backlog records, package publication, main merge or remote source push. Update existing routing/guidance against implemented APIs; create no new installable skill or parallel instruction framework.
- Preserve current Metrics-only catalog minima through conditional REST reservation and final-composition outgoing Auto/true/false capture. The full proposed profile has 27 streams, 23 Core definitions and 4416 configured capacity, within unchanged 96-definition/128-stream/16384-capacity/four-MiB limits. Use exact root meter references, one provider and actual payload boundaries.
- Preserve source bytes and REST/A requirements. Review P01-P12 with the plan. Freeze result, absence/null, truncation, correlation, cancellation and ownership seams before parallel work. Unexpected defects remain faults; expected cancellation priority caller, total deadline, stopping, attempt timeout, actual outcome is proposed.
- Retain physical ceiling reference CISLIB-CIS-0001-STORAGE-2026-11-10 and all prior counted roots/claims: 6442450944 bytes including 67108864 restoration bytes. The intake sample is 5661509307 bytes, leaving 713832773 after reserve. Count all .artifacts, not only the new REST subdirectory. Historical/user cleanup and an extra worktree are excluded. Future owned scratch claims must remain within the same counted ceiling without double-counting or broadening disposal authority.
- Use existing MSTest/native/Kestrel/packaging/time-provider fixtures and proportionate checks, including actual Core-only/optional package consumers, a two-host scrape and true owner drain. Preserve 95/90 coverage and 90% conservative mutation release obligations; no default broad memory, mutation, performance, stress or release campaign and no claims about unexecuted platforms.
- Use the supplied request/result defaults and one explicit retry owner. No automatic retries, circuit breaking, hedging, queue, token store or implicit body buffering. An opaque borrowed handler chain cannot be claimed validated or host-isolated.

The total is 48,600 active seconds and 16 logical attempts, including 1,200 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
