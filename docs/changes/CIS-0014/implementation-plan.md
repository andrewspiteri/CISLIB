---
title: State Machine bounded implementation plan
type: delivery-plan
status: Active
change_id: CIS-0014
---

# Bounded State Machine delivery

The [requirements and reconciliation](../../../planning/workflows/statemachine/README.md)
bind the supplied 701-line source to Pipeline commit
`1e0595f28dd284b365aadae9a7549a0fdb75848a`. The user asked to move to this
module and then explicitly delegated approval of the required time while away.
Codex selects the finite allocation below under that standing direction. The
source's embedded implementation prompt is reference material, not authority.
No additional routine approval gate is introduced.

## Selected implementation profile P01–P12

1. **P01 — Existing Core/public boundary.** Add enum-based `CIS.Workflows`
   contracts to existing CIS.Core, with normal `CreateBuilder`, `State().On()`,
   `Build(initialState)` and reusable `Trigger`. Keep IStateMachine public and
   immutable result/failure construction publicly guarded so outsiders can mock
   it without friendship/reflection. Use a small State Machine action-result type;
   do not redesign Pipeline or product-wide results. Non-null class context.
2. **P02 — Definition.** Accept all eight ordinary C# integral enum underlyings,
   gaps and nonzero/negative/large unsigned values; reject Flags and aliases.
   Validate initial/source/target/trigger values and duplicate source/trigger pairs,
   snapshot options/maps/names at Build. Terminal-only definitions and explicit
   self-transitions are valid. No handler/scope activation during configuration.
3. **P03 — Exclusive attempts/snapshots.** Pre-cancelled calls return normal
   Cancelled results before instance admission; overlapping/reentrant calls return
   Busy immediately. Neither changes LastTransition. Admitted rejections do update
   it. A short gate never spans user awaits; publish committed state/result as one
   snapshot, clear busy and complete the public task before the host count exits.
   Property reads are quiet and mapping-only. Keep no attempt history/context in results.
4. **P04 — Commitment.** Execute one selected mapping; keep source visible through
   preparation/work/compensation/owned disposal. Select forward success at its final
   cancellation cutoff, then commit target only after owned cleanup succeeds.
   Disposal failure retains source with Failed and never initiates compensation.
   Direct transitions create no DI scope or handler observation.
5. **P05 — Activation/ownership.** Reuse framework activation and existing narrow
   PipelineServiceActivation primitive, fresh per selected handler/attempt, with
   normal dependency lifetimes. Own the handler, prefer async disposal, then owned
   scope; never close a borrowed scope. Provide a C# TriggerInScope adapter/capability
   for one call only, preserving mockable portable interfaces and never retaining
   request providers across triggers.
6. **P06 — Result/cancellation.** Operational activation/handler/result-contract/
   disposal failures return Failed; requested execution cancellation returns a
   normal Cancelled task after actual settlement. Exact requested token attribution
   applies at Execute/Error catches; constructor/disposal OCEs remain phase faults,
   as in existing ownership policy. Freeze first observed cause before later
   cancellation. Configuration/engine-invariant/fatal failures still throw.
   Compensation uses an independent 30-second cooperative budget, explicit infinity
   and native finite-range validation. Report CleanupBudgetExpired independently;
   emit expiry diagnostics at expiry and never detach active methods/disposal.
7. **P07 — Pipeline composition.** The one-handler runner remains direct, not a
   disguised Pipeline. A handler may await a fresh real child Pipeline in the same
   explicit scope, inspect Success and map reported failure. Preserve the child's
   throwing/cancellation contract. Do not repeat failed-child unwind; compensate
   successful-child effects only through application-owned retained results.
8. **P08 — Hosting.** Explicit AddCisStateMachines uses the existing workflow
   lifetime/count, actual-owner capture and one validated hosted bridge. Factor only
   common installation; no second provider/service registry/queue. Keep Pipeline-only
   markers/catalogs unchanged. Shared host count covers complete public Trigger calls,
   including Busy/pre-cancel reporting; active-attempt metrics count only instance
   gate ownership. Closed-host calls are lifetime misuse (InvalidOperationException),
   not a new operational outcome or implicit execution cancellation.
9. **P09 — Logging.** Capture LogCalls=true and an optional successful-event
   Information promotion; default routine success/phase/rejection/cancellation Debug.
   Reads do not log. Keep expected failure Warning, original exception diagnostics
   once at origin, secondary failures visible and terminal counts without duplicate
   stacks. Reuse central correlation/filter/writer behavior; no context serialization.
   Frequent callbacks may explicitly opt out and retain their bounded owner diagnostics.
10. **P10 — Three bounded metrics.** Capture one complete binding per valid call;
    separate admission callback increments active only after the machine gate.
    Reuse runner-measured seconds, always balance active, omit unavailable durations.
    Default finite registry: 16 machine aliases, 512 unique machine/source/trigger-label
    tuples (full declared enum domain plus invalid), and 64 machine/handler pairs.
    Build admits the union transactionally, without enum-type/instance/context history.
    Invalid runtime values use `invalid`; a declared member named `invalid` uses
    `enum:invalid`. Unaliased handler metric name is `handler`, not a CLR name.
    Printable configured/enum labels fit the current 256 UTF-8-byte bound.
    Reserve 2560/16/384 combinations (2960 total) with canonical ordinal key order.
    Pipeline+REST defaults become 33 streams/7824 combinations; Pipeline without REST
    27/6736. Global 96 definitions/128 streams/16384 combinations remain unchanged.
    Smaller explicit stream overrides must fail setup rather than overflow ordinary work.
11. **P11 — CountryData.** One request-local Pending -> Resolve(handler) -> Resolved
    -> Complete(direct) -> Completed machine wraps the existing fresh cached lookup
    Pipeline. Completed means lookup preparation, not HTTP delivery. Preserve 200/400/404,
    trimming/case aliases, readiness, zero lookup provider I/O and existing quiet refresh.
    Keep at most one detached child summary; no provider/session registry. App failure
    retains Pending. Expected package observations: Pipeline 5/12/3 unchanged; State
    Machine 8 calls/7 handler phases for the existing five-lookup story. Controlled
    Busy/reentrancy belongs in existing tests, never application flags/barriers.
12. **P12 — Consumer guidance/evidence.** Add portable contract, actual guide and
    catalog; extend the one Workflows skill/metadata/routing after API/example compile.
    A fresh public skill-use story covers Draft/Submitted/Approved, rejection, expected
    failure/source retention, normal cancellation, and fresh-child usage. Reuse ordinary
    MSTest, actual DI/Kestrel/collector and local-package fixtures. No new package,
    DI/configuration framework, guard/hooks/queue/retry/persistence/scheduler, harness,
    broad stress/benchmark/mutation campaign, push, merge, release or personal install.

## Four coherent leaves across three outcomes

| Scope | Complete outcome, including tests/docs/review | Dependency | Seconds / attempts / included restoration |
| --- | --- | --- | --- |
| execution | P01–P07: public API/results, enum map, exclusive direct runner, state publication, selected activation, failure/cancellation/compensation/disposal, nested Pipeline; A01–A12 and portable contract. | None | 19800 / 5 / 480 |
| hosting | P08: common existing-owner registration, per-call adapter/factory, native/facade/failed-build drain and held-work ownership/readiness tests plus hosting guidance. | execution | 7200 / 3 / 300 |
| instrumentation | P09–P10: logs, three handles, bounded enum/alias metadata, conditional catalog/attachment and one actual exposition; A13–A15, focused tests and metric docs. | execution | 10800 / 4 / 300 |
| consumer | P11–P12: CountryData, guide/shared skill/metadata/routing, public/mock/child scope/Busy usage and package build/publish proof with affected regressions. | execution, hosting, instrumentation | 12600 / 4 / 420 |

One owner freezes public contracts and internal seams. Hosting and instrumentation
may prepare disjoint files after that freeze; consumer source may follow API
stability, with final integration/verification dependent on the real prerequisites.
Runtime trigger work and each handler/compensation/disposal remain sequential.
The controller moved 1,800 unused ordinary consumer seconds to execution for the
already selected real child-Pipeline proof and conservative intake/review costs.
Shared totals, logical-attempt limits and every restoration reserve remain unchanged.
The controller owns shared hosting/Application/collector registration boundaries,
project/solution/lock files and every coordinated native invocation.

Checkpoints: core gate/state/failure/ownership passes; actual host+collector behavior;
compiled real consumer/shared-skill/public-package result. Every leaf includes its
own meaningful tests, documentation and review, not separate evidence-only work.

## One containing allocation

The user delegated required-time approval. Codex selects **14 active hours /
50,400 seconds and 16 logical attempts**, including **1,500 restoration seconds**.
All agents, already performed intake, code, native preparation/tests, docs/skill,
formatting, review and closeout share these totals. Task organization/unused
resource redistribution stay within them and never reset actual cost or failures.

Retain shared physical ceiling CISLIB-CIS-0001-STORAGE-2026-11-10:
**6,442,450,944 bytes**, with existing **67,108,864 protected bytes**, counted over
`.artifacts`, `.cis/local`, `implementations/csharp`. No new storage pool/worktree,
historical cleanup, old-owner reopening or speculative qualification is authorized.
Record measured native cost separately from conservative source/controller estimates.
