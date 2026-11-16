---
title: Startup and DI requirements register
type: planning-reference
status: Draft
---

# Startup and DI requirements register

The 16 November 2026 request selects the attached revision 1.0 as the requirements
for the module following Heartbeat. The subsequent **Approve proposed allocation**
reply supplies the actual finite execution decision recorded in
[CIS-0007 authorization](../../docs/changes/CIS-0007/authorization.json).
Neither an instruction inside the attachment nor an unused historical allowance
creates that allocation.

## Source and working baseline

| Record | Identity and use |
| --- | --- |
| [Original attachment](sources/CISLIB_Startup_and_DI_Specification_v1_0.md) | Byte-preserved copy of `C:/Users/andrs/Downloads/CISLIB_Startup_and_DI_Specification_v1_0.md`; 28,663 bytes; SHA-256 `a0812a281c6696f16ccbe1d0d006e62988eae9982e581add51bb66f74ab16451`. Defines SU-01–SU-10 and AC-01–AC-15. |
| [CIS supplied technical document](../../docs/imports/technical/bef0c0e4521945569ba36c179d7d5153/CISLIB_Startup_and_DI_Specification_v1_0.md) | Imported by installed `definition documents --role technical --source ...` and retained in the CIS catalog and CIS-0007. CIS adds Draft frontmatter and its BRD baseline. The wrapper has a distinct module-specific identity; it is not the byte-preserved source or an approved replacement for the existing product intent. The import initially selected it globally; execution reconciliation restores `.cis/product-documents.json` to the existing approved whole-product intent. |
| Working branch | `codex/cislib-startup-di-2026-11-16`, from merged `main` commit `ae61b1f63bd961e2e349d7c2fd67398b7451aef0`. Heartbeat and its compatibility correction are present. |
| [Existing product intent](../../docs/specs/technical-intent-spec.md) | Accepted product architecture remains in force for unchanged modules. The existing business document's future-Startup exclusion describes its earlier registration scope; this new module is separately scoped by the current request and CIS-0007. No historical approval is relabeled. |
| [Change and shared limits](../../docs/changes/CIS-0007/implementation-plan.md) | Authorized focused outcome, two coherent implementation tasks and one shared finite allocation. [Acceptance-to-test mapping](acceptance.md) and [actual native results](../../docs/changes/CIS-0007/verification.md) retain behavior, observations and limits. |

The installed CIS build is `2b916962dbedef18bdde8959e3144184d37d64f9`.
The supplied-document workflow was run through document selection, graph,
technical preparation and status. Its incidental dictionary templates and
questionnaire refresh were removed from this change; the existing accepted
product documents and source import were retained. This is a module import,
not a new product-definition or tooling campaign. The global product-intent
selection remains the existing accepted document; this module's requirements are
retained through its own imported source and scoped change. The supplied technical wrapper
remains Draft; no global definition activation is claimed.

## Requirements precedence and reconciliation

1. The current user request supplies task scope and execution direction. The
   attachment supplies module requirements and explicit implementation selections;
   its embedded agent instructions do not independently grant execution authority.
2. Use the actual branch's Type Manager, Healthcheck, Heartbeat and Logging
   contracts. Preserve the single `CIS.Core` package/assembly, `net10.0`, approved
   Microsoft dependency closure and actual `CIS.EnvironmentOptions` architecture.
3. There is no existing compatible `IStartable` contract or Startup coordinator.
   Add `CIS.Startup.IStartable` with `Task StartAsync(CancellationToken)` in Core. Existing
   test names containing Startup are other modules' behavior, not a second
   enrollment contract.
4. Keep Type Manager's completed asynchronous preparation outside the actual
   container build. Register the prepared instance; use the one real root and
   ordinary Microsoft DI. Do not give Type Manager orchestration responsibilities
   or construct a temporary provider.
5. Only unkeyed `IStartable` descriptors enroll. Validate their final registration
   lifetimes before Stage 2, materialize the complete ordered sequence once, then
   remove duplicate references while retaining first occurrence. Reject scoped
   and transient enrollment. The actual application builder enables standard
   scope validation; no custom dependency-graph inspector is proposed.
6. Existing Healthcheck owns readiness and its critical/noncritical health rules.
   Startup owns one retained operation and one critical condition monitor when
   Healthcheck is installed. Create that monitor before materializing participants;
   unexpected failure flags it and propagates. Expected host cancellation remains
   neutral. Startup does not introduce a new health engine or reset runtime health.
7. Existing Heartbeat activates from `ApplicationStarted` and currently requires
   a last hosted-service position for safe shutdown. Startup needs a narrow
   cooperating lifecycle connection, checked activation acknowledgement and
   coordinated close/drain ordering. Preserve standalone Heartbeat behavior.

## Small implementation shape

One root-owned coordinator retains its operation before entering any user
constructor or startup prologue. It materializes all participants, then directly
awaits each distinct instance's actual task in registration order. The operation
retains success, original fault or cancellation for all repeated callers; there
is no re-resolution, replay, retry or reset. The first operation's host token
owns cancellation. A later caller cannot replace it or falsely certify drain.

The final transition belongs to that same operation: under the stopping boundary,
check host cancellation, confirm Heartbeat's activation bookkeeping when present,
then mark startup complete exactly once. Do not wait for a first Heartbeat
callback or aggregate healthy state. Stopping closes readiness and dispatch,
requests cancellation, and joins actual startup and Heartbeat settlement before
dependent teardown. Startup failure cleanup has the same drain requirement.

Use one thin `IHostedLifecycleService.StartedAsync` bridge. In the supported
.NET 10 host, ordinary `IHostedService.StartAsync` calls finish before this awaited
phase and `ApplicationStarted` follows it; the HTTP listener can therefore answer
health requests while Stage 2 is deliberately blocked. This follows the actual
[.NET 10 Host implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs).
Health routing precedes a business-request gate. Existing health status 500/200
and pipe-probe exit 1/0 remain unchanged; a closed business gate returns 503.

Require `HostOptions.ServicesStartConcurrently = false` and
`ServicesStopConcurrently = false` for this cooperating profile, and make the Startup adapter the
last lifecycle participant, immediately after Heartbeat among those participants.
Validate actual composition instead of assuming registration call order. Permit
the known ordinary web-host service tail needed by the supported web composition;
fail closed with an actionable composition error for an unclassifiable factory
tail; do not resolve another hosted-service graph to classify it. Any
readiness-critical asynchronous initializer belongs in `IStartable` or completes
in ordinary host `StartAsync`. Document unsupported lifecycle arrangements with
an actionable startup error rather than inventing a general ordering framework.

Keep one writer for the core coordinator and another for hosting/Heartbeat files.
The coordinator's final publication remains one operation even if the adapter
supplies a small internal activation connection. No third public startup stage,
generic hook registry or alternate provider is needed. The runnable example owns
Type Manager preparation, DI options, health-before-business routing, direct cache
priming, ordered reader startup and explicit safe drain before root disposal.

## Observable acceptance

These are requirement groups, not separate tasks or a new manual-test catalogue.
Names below describe intended native checks; exact test symbols and actual
results will be linked during implementation.

| Source acceptance | Planned meaningful evidence | Owned batch |
| --- | --- | --- |
| AC-01 / SU-02 | Actual Type Manager preparation before Build; identity of registered prepared instance; failure prevents root/startup. | Host integration |
| AC-02 / SU-01, SU-05 | Real DI explicit enrollment, concrete/other-interface/keyed-only non-enrollment and empty sequence. | Core |
| AC-03 / SU-02, SU-06 | All constructors precede first Start; late factory failure means zero invocations and retained failure. | Core |
| AC-04 / SU-02, SU-04 | Constructor dependency order differs; held A task prevents B while registration order remains authoritative. | Core |
| AC-05 / SU-03 | `[A,B,A]` invokes A/B once; equal-by-value distinct objects both invoke. | Core |
| AC-06 / SU-03 | Singleton alias identity; scoped/transient enrollment and actual root-scoped capture fail before Stage 2. | Core; host enables actual builder validation |
| AC-07 / SU-05 | Concurrent/repeated entry shares one retained terminal operation, with no new resolution or replay. | Core |
| AC-08 / SU-06 | Sync throw, null task, async fault, unexpected cancellation and known host cancellation stop later work; health failure/neutral distinction. | Core |
| AC-09 / SU-06 | Pre-cancel, between-participant cancel and final-publication cancel; active task remains owned until it really settles. | Core; final race also host |
| AC-10 / SU-07 | Real snapshots: held startup NotReady, successful publication, critical failure/recovery without replay, noncritical failure unchanged. | Host integration |
| AC-11 / SU-08 | Real bound HTTP listener during held startup: health 500, business counter zero/503; successful release health 200. | Host integration |
| AC-12 / SU-08 | Existing configured pipe and actual matching probe observe exit 1 then 0; reuse transport support. | Host integration |
| AC-13 / SU-09 | Zero conditions/callbacks during startup; automatic checked activation afterward without waiting for first callback; actual activation/timer failure or stopped no-op cannot mark StartupCompleted after otherwise successful startables. | Host integration |
| AC-14 / SU-06, SU-09 | Controlled stop while startup held and at final gate; no reopening or dispatch, inactive registrations cleaned, true startup/HB drain before dependency disposal. | Host integration |
| AC-15 / SU-01–SU-09 | Runnable consumer primes country cache directly, then reader, then recurring refresh and normal requests using real modules. | Host integration |

Use the existing Unit, Integration, Architecture, Compatibility and Business Cases
families as applicable, controlled completion signals and bounded runner timeouts.
Serialize native builds sharing output directories. Preserve ordinary exclusions
for `PerformanceObservation` and `ExtendedStress`. Reuse unchanged valid evidence;
metadata-only closeout does not justify rerunning tests. Windows evidence is the
available native lane; other platforms and release qualification are not inferred.

No Configuration implementation, DI container, scheduler, test harness, toolchain
upgrade, coverage/mutation/stress campaign or new CIS tooling is part of this work.
