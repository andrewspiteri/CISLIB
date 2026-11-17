---
title: Configuration implementation proposal
type: planning-reference
status: Draft
---

# Configuration implementation proposal

Andrew Spiteri approved this four-task plan and P01–P08 profile with one shared
finite allocation on 17 November 2026. CIS-0008 records the actual outcome,
concrete task contracts and allocation; this planning file grants no independent
authority or native result. The controller owns
source import/reconciliation and the actual change, authorization and allocation
records. The attachment's U requirements and P recommendations remain distinct;
its embedded work-package instructions do not grant a budget.

Deliver the module in the existing `CIS.Core` assembly under `CIS.Configuration`,
using the existing Type Manager, Startup, Heartbeat, Healthcheck and native
workflow. Preserve completed owners, actual costs/attempts, accepted baselines,
EnvironmentOptions behavior and useful reusable evidence. Do not restart Type
Manager's historical plan or copy the specification's six ownership packages into
six automatic tasks.

## Contract decisions to close once

The proposed outcome decision should select P01–P08 together before freezing
consumer contracts or persisted keys. The following dispositions are recommended,
not represented as historical approvals:

| Proposal | Recommended disposition |
| --- | --- |
| P01 | Explicit async preparation after Type Manager and before the one container Build. Stage 1 constructors receive populated retained POCOs. Stage 2 activates/reconciles refresh; it does not perform the initial load as a second automatic mode. |
| P02 | Runtime identity is the actual CLR Type. Persist full type name and assembly name/culture/token, omitting version. Reject collisions and the reserved CommonConfiguration host identity. Preserve any actually adopted earlier key scheme; no fallback guessing or implicit migration. |
| P03 | Presence-aware structured JSON overlays: defaults, common, application; ordinal declared property names; nested member merge; whole collection/dictionary replacement; explicit null only where permitted. Omission, confirmed deletion and failed reads remain different. Reject duplicate/ambiguous keys and invalid known values; bound unknown-name diagnostics. |
| P04 | One private entry gate shared by prepared commits and a short synchronous read helper. Raw property reads have no transparent atomicity guarantee. Passive nonthrowing setters, detached replacement children and consumer read-only use are contractual. |
| P05 | One typed ConfigurationChanged event with CLR type/local revision, an explicit per-instance add/remove helper and optional base class. Direct interface implementations use the documented helper accessors. Detect unsupported event wiring without backing-field reflection, proxies or compulsory inheritance. |
| P06 | Separate source tokens/payloads from SHA-256 of deterministic validated effective values. Retain newly read source state even when masked by an application override. Fix the supported scalar/date/enum/collection representation once. |
| P07 | Initial population establishes a baseline without an event. Failed refresh preserves accepted values/checksum/revision. Accepted commits notify after releasing the gate, in revision order, isolating synchronous subscriber exceptions; post-commit cancellation cannot discard notification. |
| P08 | Discover through prepared Type Manager; validate every candidate's supported passive shape. Use a usable parameterless constructor or explicit exact-type factory without DI/storage work. Capture defaults once, including applicable child defaults, rather than regenerating them during reload. |

Do not publish another `IConfiguration`, `ConfigurationManager` or diagnostic type
that collides with current APIs. Existing `CoreConfigurationException`,
`ConfigurationIssue` and `ConfigurationIssueCodes` are public in
`CIS.Configuration` even though their files reside under Environment/Resolution.
Preserve their identities and behavior. Names such as `ConfigurationRuntime`,
`IConfigurationObject`, `IConfigurationReader`, record/read-result contracts and
the event helper are proposed roles; W1 checks and freezes the actual names.

No production database, broker, filesystem backend, polling interval, stale-age
policy or new environment variable is selected. The host supplies bootstrap
identity, credentials and an adapter without depending on the configuration being
loaded. Polling requires an explicit positive interval within Heartbeat's existing
duration bounds. Notification capability must state its invalidation, deletion
and reconnect/reconciliation guarantees; a controlled provider is not a deployed
production adapter.

## Actual integration and proposed lifetime connection

These existing APIs are callable now; the attachment's earlier availability
qualification is stale for this checkout:

- [TypeManager](../../implementations/csharp/src/CIS.Core/TypeManagement/TypeManager.cs)
  prepares asynchronously with `TypeManagerOptions(hostAssembly)` and the real
  inventory. `ITypeManager.GetImplementations<IConfigurationObject>()` supplies
  direct/inherited candidates, including shapes Configuration must reject or
  explicitly construct. Configuration performs no assembly scan.
- [Startup](../../docs/modules/startup/README.md) materializes all root startables
  before sequential initialization. `AddCisStartup()` is the existing awaited
  StartedAsync bridge; the coordinator has no concrete Heartbeat dependency.
- [Heartbeat](../../docs/modules/heartbeat/README.md) supplies the shared runtime,
  `CreateOwner()`, owner-bound `Service.Register`, and actual owner/global drains.
  Registration during Stage 2 is inert until Startup confirms activation.
- [Healthcheck](../../docs/modules/healthcheck/README.md) owns readiness and monitor
  policy. Configuration never calls application-wide MarkStartupCompleted.

The proposed supported path is:

1. Await actual Type Manager preparation. Await Configuration preparation using
   that same manager, explicit fixed host identity and host-supplied store.
2. Register the prepared runtime/reader and each retained concrete POCO before
   consumers, rejecting conflicting registrations. Build one real root provider;
   DI resolution does no I/O or extra provider construction.
3. Enroll Configuration's ordinary IStartable before dependent initializers.
   Activate notifications by subscribe-and-reconcile, processing invalidations
   arriving during reconciliation. Polling performs a direct startup reconcile
   and registers retained subscribers with the existing shared Heartbeat owner.
   It cannot await a Heartbeat callback while Startup deliberately keeps HB idle.
4. Register ordinary dependent hosted services before the cooperating Heartbeat
   and Startup adapters, retaining their supported nonconcurrent ordering. Keep
   the actual health branch before the business readiness gate in Web consumers.
5. Add a narrow optional Configuration lifetime connection in Startup.Hosting.
   This is proposed W4 work, not an already installed API: hosting closes
   Configuration admission and cancels its owned provider-work token before
   Heartbeat cutoff/drains and before waiting on the coordinator completion gate.
   It then joins actual Configuration, startup and Heartbeat settlement before
   dependent teardown. The coordinator remains independent of these concrete
   module lifetimes; no closing-participant registry is introduced.

That last ordering is necessary for actual APIs. Heartbeat BeginStopping closes
admission and drains; it does not automatically cancel its execution token.
Normal Host.StopAsync runs lifecycle StoppingAsync before ApplicationStopping.
Deferring Configuration cancellation until a later service's stopping callback
could leave Startup waiting on an HB provider read whose cancellation cannot yet
run. The narrow hosted connection closes this cycle and also drains notification
work not counted by HB. A polling callback awaits its real refresh/event work;
it must not escape HB capacity/lifetime accounting by detaching that work.

Notification callbacks signal bounded per-entry pending state. One serialized
retrieval/validation/commit/notification path serves polling, notifications and
reconnects. Configuration owns those tracked attempts and subscription handles;
it never disposes host-owned providers or unexpectedly disposes/reset live POCOs.
Stopping wins commit admission under entry coordination, cancels pre-commit work,
and includes notification for an already accepted commit in its actual drain.
Neither a canceled wait nor a timeout proves arbitrary provider/handler code has
settled. No timer replacement, forced task termination or retry campaign is added.

## Four coherent implementation owners

Every task includes focused native tests, documentation, review and completion
checks. These are proposed ownership boundaries, not existing executable records.
The controller serializes shared builds and owns shared project/solution/package
edits and public-contract changes requested after W1.

| Owner | Scope and non-overlapping files | Meaningful exit condition |
| --- | --- | --- |
| W1 — Contracts and retained roots | Proposed `Configuration/Contracts/*`, `Configuration/Objects/*` and corresponding contract/ownership tests and guide sections. Reconcile public names/P decisions; define internal type/default/prepared-value seams, private gate ownership, eligibility, exact-type construction/default capture, retained registration metadata, synchronized reader and event helper. Existing diagnostic files remain preserved. | One discovered retained root is safe to expose; same-instance/host isolation, default capture, supported direct-interface/base event wiring and read ownership have native tests. Unsupported types, event shapes and name/key conflicts fail usefully. No full mapper/store/scheduler is hidden in W1. |
| W2 — Effective-value pipeline | Proposed `Configuration/Mapping/*`, mapping-specific unit/business tests and mapping guide. Consume W1 seams; implement presence merge, conversion/validation, deterministic effective representation/checksum and prepared in-place commits. | Native cases prove explicit defaults/null, nested/collection semantics, deletion and stable captured defaults. Hidden common changes appear when an override is removed. A synchronized reader cannot observe a deliberately slowed partial copy. Invalid candidates never reach a live commit. |
| W3 — Storage and refresh coordination | Proposed `Configuration/Storage/*` implementation and `Configuration/Refresh/*`, provider/refresh tests and lifetime guide. Public provider/result contracts are fixed in W1; W3 implements cached Found/Missing/Unchanged/Failure bookkeeping, conditional invalid-state handling, watch invalidation/reconnect and serialized bounded pending work, cancellation and actual drain. | Controlled native races prove no stale local overwrite, no lost pending transition/watch gap, source-state retention under masked changes, ordered after-commit events, subscriber isolation and pre/post-commit cancellation. Failure preserves accepted values and does not become deletion. Provider fixtures are identified honestly. |
| W4 — Bootstrap and real integration | Proposed `Configuration/Hosting/*`, preparation/registration composition, the narrow optional `Startup/Hosting` lifetime connection, existing Console/Web source/package consumers and affected integration/business/compatibility/architecture tests and usage docs. Queue all project/solution/lock/package changes through the controller. | Actual pre-Build preparation gives populated constructor values and root identity across scopes. Actual Startup/HB/Healthcheck and provider-subscription paths demonstrate activation reconciliation, polling, truthful probes, early Configuration cancellation and combined real drains before dependencies. Existing package consumer/build inventory integration remains real. Backend availability and all unexecuted qualification are explicit. |

W2 and W3 may run in parallel after W1 freezes shared contracts, default/entry
representations and the prepared-commit seam. They do not edit each other's files
or silently change shared signatures; queue adjustments through the controller.
W4 can prepare test/consumer composition while W2/W3 run, but final integration
depends on their accepted implementations. No owner launches builds sharing output
directories concurrently. Parallel execution is arranged by the controller,
without claiming that CIS automatically schedules it.

Demonstrate the three source-requested outcomes early within these owners: one
retained object across scopes/reload; a masked common update becoming visible
after override removal without a false earlier event; and a coordinated reader
never observing a partial update. They are useful checkpoints, not extra tasks or
approval cycles. No separate QA sweep or infrastructure campaign is proposed.

## Proposed shared finite allocation

The proposed ceiling is **six active hours (21,600 seconds), eight logical
attempts, and the existing shared physical 6 GiB limit**. This is one pool for all
writers, native processes, tests, documentation, review, diagnostics and cleanup.
Parallel workers consume its aggregate active time; each worker does not receive
six hours or a separate storage allowance.

| Proposed owner ceiling | Active seconds, including restoration | Logical attempts | Included restoration seconds |
| --- | ---: | ---: | ---: |
| W1 | 4,200 | 2 | 100 |
| W2 | 6,000 | 2 | 150 |
| W3 | 6,000 | 2 | 150 |
| W4 | 5,400 | 2 | 200 |
| Shared total | 21,600 | 8 | 600 |

The **600-second restoration reserve and 64 MiB physical restoration reserve are
included** in those shared ceilings. They are not extra capacity. The physical
6 GiB ceiling retains its existing scope/accounting basis; no per-owner copy or
replacement allowance is implied. Execution still requires actual outcome/task
authority and allocated remaining capacity. Any redistribution must use the
supported canonical update without increasing shared totals. Preserve historical
used attempts, cost, active reservations and restoration records; this proposal
does not reopen completed owners or authorize deleting unknown artifacts.

## Verification within implementation

Preserve all six existing library categories: unit, integration, mutation,
architecture, backward compatibility and business behavior. W1–W3 run meaningful
focused cases as their source stabilizes. W4 integrates affected regression and
actual existing Console/Web source and package-consumer lanes at the candidate
boundary, with targeted mutation of new shipping behavior using the existing
tooling where applicable within the shared allocation. Source/package/platform
claims name the actual executed profile and bytes; historic Type Manager passes
do not establish the changed Configuration consumer passed.

Reuse ordinary MSTest projects and native commands. Actual Type Manager inventory
and unused-library discovery belong in meaningful integration, rather than a fake
catalog being described as production discovery. Controlled store/watch seams
exercise the module contract; absent backend selection remains an explicit
external adapter dependency. Use actual host stopping with an HB provider read
held until owned cancellation, plus notification-only held work, to prove the
proposed hosted connection avoids premature disposal and cancellation deadlock.

Preserve full-metadata/frozen consumer compatibility, public namespace/dependency
and EnvironmentOptions checks. Do not narrow compatibility evidence to an
unrelated health-only selection. Serialize pack/build/restore and affected tests;
retain ordinary PerformanceObservation/ExtendedStress exclusions. Any needed
targeted mutation uses the [existing native guide](../../implementations/csharp/verification/mutation/README.md),
not a new runner, mutation engine or report archive. Keep Windows/Ubuntu lane
observations distinct, and identify any candidate/platform gate not executed
before a resource limit rather than inventing a pass or silently expanding scope.

Full release coverage/mutation/platform/performance/publication obligations remain
those in [accepted decisions](../decisions.md). A targeted candidate pass does not
close their complete source scope, thresholds or actual platform requirements.
No new runtime dependency, test harness, stale-age policy, production backend or
global configuration/lifecycle framework is authorized by this plan.

The completion record should state selected P decisions, U/CFG-A coverage, exact
changed source, actual native results and valid reuse, package/integration status,
finite accounting and any precise external adapter or qualification gap. Stop new
execution for an actual authority/resource limit, concrete unresolved contract or
repeated failure without a new diagnosis; preserve the reserve for orderly
diagnostics/restoration. Administrative edits alone do not justify rerunning
unchanged tests. No implementation or completion is claimed by this proposal.
