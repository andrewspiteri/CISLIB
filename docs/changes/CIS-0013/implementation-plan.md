---
title: Workflows Pipeline bounded implementation plan
type: delivery-plan
status: Approved
change_id: CIS-0013
---

# Approved bounded delivery

The [imported requirements and reconciliation](../../../planning/workflows/README.md)
are the basis for this plan. This is a new feature in the existing product. It
does not replace the BRD, recreate the workspace, or inherit a closed owner's
execution grant. Andrew Spiteri explicitly approved P01–P12 and the containing
allocation on 18 November 2026. CIS recorded that decision and derived four
canonical tasks in the [approved plan](plan.md); the finite allocation is recorded
in [work-allocation.json](work-allocation.json).

## Approved profile P01–P12

1. **P01 — Core and API.** CIS.Workflows public contracts and small C# hosting
   adapters in CIS.Core; no new dependency/package. Preserve Add<TStep>(), Build()
   and Task Execute(token=default), immutable success/failure factories and existing
   public compatibility. New PipelineExecutionException only wraps a Failed
   primary OCE; ordinary originating faults retain identity/stack.
2. **P02 — Definition/admission.** Snapshot order, aliases and options at Build;
   no activation/scope/work there. Reject empty/invalid definitions. Duplicate
   registrations are distinct. Publish the accepted completion before user code;
   one atomic single-use admission and no second telemetry on rejected calls.
3. **P03 — Context/reports.** Require a non-null class context. Keep one shared
   reference and O(N) execution/report state, independent across instances. Ordered
   immutable report snapshots contain every registration, separate phase outcomes,
   absent durations for unentered methods, primary/secondary/lifecycle diagnostics
   and CleanupBudgetExpired. Release services/providers after cleanup; retain the
   report/context. Final state is published only after actual owned settlement.
4. **P04 — DI ownership.** Fresh ActivatorUtilities activation for every registration
   before forward work. Default owned async scope; explicit CreateBuilderInScope
   C# adapter borrows a supplied scoped provider. Keep that provider out of portable
   runner logic. Reverse owned-step disposal continues after individual faults,
   prefers async disposal, then disposes an owned scope once. DI owns dependencies
   and its own disposal behavior; no replacement disposal engine.
5. **P05 — Primary completion.** First returned failure/exception is frozen before
   a later checkpoint can relabel it. Unwind every entered step, failing first,
   sequentially once; never invoke Error on an unentered construction. Expected
   failure returns normally after cleanup; faults rethrow after cleanup. Existing
   primary Failed/Cancelled survives secondary handler/disposal faults. Disposal
   after committed forward success can make the result Failed without a new unwind.
6. **P06 — Cancellation.** Caller token is the execution cancellation policy, with
   checks before scope, between activations/steps and after the last success.
   Exact requested execution-token attribution is evaluated when caught; foreign
   or not-yet-requested OCE stays a fault even if cancellation arrives later.
   Constructor/disposal exceptions remain phase faults; explicit preparation
   checkpoints may cancel before any forward method. Unwind uses its own captured
   30-second ErrorHandlingTimeout, explicit infinity supported, invalid finite
   values rejected. Expiry requests cancellation but awaits all actual handlers;
   invoke every remaining handler once even with an already-canceled cleanup token.
7. **P07 — Host lifetime.** Explicit idempotent AddCisWorkflows opt-in via the normal
   service callback, using one actual provider. Close workflow admission early.
   Validate the bridge among existing cooperating services after ordinary business
   services and before the exact native web-host tail; reverse Stop then drains
   after listener Stop and before ordinary business Stop teardown. Stopped and
   facade cleanup also join before provider disposal, including failed startup/Build.
   Use a local active count/drain completion, no run/task/history registry, worker
   queue or scheduler. No implicit execution timeout or host-token substitution;
   callers keep using their execution token. Preserve existing relative cutoffs
   and keep StartupCoordinator independent. Guarantee DI/provider-owned dependencies,
   not arbitrary user destruction: application owners tearing resources down on
   stopping signals must await their own executions first.
8. **P08 — Logs.** Default source levels/events, one terminal summary and no context
   serialization. Capture LogCalls=true per definition; false skips automatic
   events for callers with a scoped frequent-callback reporting policy. Fixed
   category/EventIds, existing correlation and central filtering, no per-run flush,
   global suppression or new pipeline. Reporting faults never affect work/unwind.
9. **P09 — Metrics.** Exactly 3 reusable module-lifetime handles through existing
   attachment/provider ownership. Overall duration/active spans include activation,
   unwind and all owned cleanup; phases cover only entered methods. Record the
   runner's measured seconds to both reports and ITimer.Record. Preserve the
   authoritative application/environment, standard 14 duration bounds, no-op/overflow
   semantics and capture-on-entry binding; no per-run meter or duplicate counter.
10. **P10 — Finite metadata/capacity.** Defaults of 16 admitted pipeline names and 64
    pipeline/metric-step pairs; transactional Build admission and validation before
    work, even with collection disabled. Report/log default names may describe
    types; unaliased metrics use `step`, explicit configured aliases get their own
    bounded pair. Names are printable, nonempty, ordinal and within existing 256
    UTF8-byte values; no hashing/truncation or IDs. Reserve48/16/384 combinations
    before collector activation, total 448; opt-out hosts keep old catalogs. Smaller
    stream overrides must be adequate or fail setup. Global limits stay unchanged.
11. **P11 — One real consumer.** Extend cached CountryData lookup with 3 explicit
    aliases and a fresh context. Preserve existing responses/readiness/aliases and
    no provider I/O on lookup. Ordinary missing-country failure demonstrates
    reverse Error with an unentered final step. Leave refresh/startup controls and
    prior tutorial removals intact. Use the explicit caller-scope path where needed.
12. **P12 — Guidance/evidence.** Publish the portable contract, central catalog,
    actual guide and one repository usage skill/metadata/routing only after API and
    example compile. Reuse native/package/collector fixtures and a fresh public
    three-step skill-use case. No personal installation, new harness, benchmarks,
    mutation campaign, new module, push, merge or release in this allocation.

## Four coherent medium tasks across three outcomes

The four scopes below were derived through CIS as WORK-001 through WORK-004,
respectively, after the containing authorization was recorded.

| Proposed leaf | Complete outcome including its tests/docs/review | Depends on | Seconds / logical attempts / included restoration seconds |
| --- | --- | --- | --- |
| Execution and DI | P01–P06: public contracts, immutable builder/definition, sequential runner, activation/scopes, unwind, reports, cancellation and reverse disposal. Portable contract and focused A-01–A-12 tests, API guards and original-fault identity. | None |14400 /5 /360 |
| Existing-host lifetime | P07: normal factory registration, native host/facade admission and actual drain, successful/failed host cleanup, borrowed ownership and preserved readiness/ordering. Held-work integration tests and host guide. | Execution and DI |7200 /3 /240 |
| Automatic instrumentation | P08–P10: log schema/opt-out, one captured metrics bundle, bounded metadata admission, conditional catalog reservations and one real exposition check. Disabled/throwing/limit/identity tests; A-13–A-15 and catalog docs. | Execution and DI |10800 /4 /300 |
| Consumer and repository skill | P11–P12: compact CountryData use, guide, skill/metadata/routing and package/public-use proof. Preserve HTTP behavior; a fresh middle-failure consumer is also the skill usability case. A-16 and affected regressions/docs/format checks. | All three integration prerequisites |10800 /4 /300 |

Outcome 1 is execution; outcome 2 comprises hosting and instrumentation; outcome 3
is consumer enablement. No separate coordination/evidence task. The runtime itself
always executes and unwinds sequentially.

Freeze shared API/lifecycle/observation signatures with the first owner. The host
and instrumentation leaves may then run in parallel on disjoint files. Consumer
source/guidance may be prepared after API stability; final integration depends on
actual host/instrumentation completion. The controller owns shared runtime/factory,
CISApplication/Startup hosting, solution/projects/locks and all native invocations.
Assign concrete file ownership before edits; keep shared-contract changes serial.

## Acceptance and bounded checks

- A-01–A-07: ordered awaits, failure at first/middle/last, false/invalid/null result,
  independent duplicate registrations/runs, atomic rejection and report snapshots.
- A-08–A-10: actual fresh activation, later construction fault, owned/borrowed scope
  identity, all returned steps disposed in reverse, held async cleanup and failures
  without re-unwind or primary replacement. A constructor owns resources it never
  returned; the runtime cannot dispose that missing instance.
- A-11–A-12: pre/between/current/final/late cancellation, foreign OCE, multiple
  handler faults, independent budget/infinity and expiry with remaining handlers.
  Use existing manual time and controlled held tasks; no wall-clock 30-second waits.
- Host: held accepted work through native shutdown, canceled host wait, failed
  startup/late Build, no provider/dependency disposal until true settlement. Verify
  listener, ordinary business teardown and existing REST/HB/Configuration order;
  reject incompatible registration order. Keep StartupCoordinator independent.
- A-13–A-15: structured safe logs, quiet mode, matching report/metric durations,
  phase counts, active balance, pre-attachment/no-op/failure isolation, metadata cap
  rejection, conditional old catalogs, and one targeted exporter/identity check.
- A-16: actual Core package consumption from outside friend assemblies, compiled
  CountryData use/HTTP regressions and skill-driven three-step expected failure.
  Use existing local feeds/caches/hash guards, no new app or general test harness.
- Build affected projects, ordinary focused MSTest/IDE path, changed-file formatter,
  strict docs, supported skill syntax/link/metadata checks and relevant API tests.
  Preserve per-owner result paths. If a CIS capture invocation fails, settle its
  actual work first and distinguish ordinary native results/currentness; do not
  repair tooling or rerun passing tests merely to refresh metadata.

Checkpoints: first report core ordering/unwind/cancellation/ownership passing;
second report actual host drain and real exposition working; finally report
compiled consumer/skill evidence and remaining limits. No Linux, unchanged-Main,
coverage/mutation/performance or release claim without corresponding execution.

## Approved containing allocation

One shared **12 active hours / 43,200 seconds and 16 logical attempts** includes all
controller/agents, already performed intake, implementation, native preparation,
tests, package/skill/format/docs validation, reviews and completion. It includes
**1,200 restoration seconds**, not an additional pool. Codex may organize tasks
and redistribute within totals without resetting actual work, failures or attempts.

Keep the existing physical **6,442,450,944-byte / 6 GiB** counted ceiling over
`.artifacts`, `.cis/local` and `implementations/csharp`, including the existing
**67,108,864-byte / 64 MiB** reserve. No second worktree/pool, new tool installation
or historical cleanup grant. Source/controller estimates remain explicit beside
automatically measured native cost. Earlier grants remain closed.

The explicit approval, outcome authority, canonical tasks and finite allocation
are now recorded. Ordinary task
organization and necessary code/test/docs/skill work then need no repeated approval
within the accepted profile. A material scope/contract or containing-limit change
requires its specific decision. No implementation authority is inferred from the
attachment or from silence after this proposal.

## Contained final-review correction

The four planned leaves delivered their coherent outcomes. Final review then found
that the section 9 cleanup-expiry warning waited until every handler finished.
An ignoring handler could therefore hide the expiry diagnostic indefinitely.
CIS does not permit Complete → InProgress, so delegated organization derived
WORK-005 for that same accepted instrumentation requirement, including its existing
held-handler regression, guidance and affected native/package checks.

The controller delegated 2,400 unused ordinary seconds and one attempt from
WORK-003 to WORK-005. WORK-003 now has 8,400 seconds / 3 attempts; WORK-005 has
2,400 seconds / 1 attempt. Existing protected per-task restoration allocations
were retained. WORK-005 has no separate protected reserve; the existing containing
1,200 seconds remain protected. Total time remains 43,200 seconds, attempts 16,
physical storage 6 GiB and protected storage 64 MiB. No prior cost, result, task
history or allocation was reset, and no new profile choice was introduced.
