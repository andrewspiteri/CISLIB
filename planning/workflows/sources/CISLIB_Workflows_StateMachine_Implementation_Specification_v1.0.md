# CISLIB Workflows — State machine implementation specification

**Version:** 1.0  
**Date:** 18 November 2026  
**Public namespace:** `CIS.Workflows`  
**Target:** C# first, under the repository's .NET 10 dependency policy  
**Status:** implementation handoff based on the accepted design

Implement an enum-based state machine using the same builder, DI, reporting and automatic instrumentation principles as Pipeline. This document defines the feature, its operational defaults, focused acceptance and the instructions for extending the existing repository workflow skill. It does not claim that the proposed API already exists in CISLIB.

## Contents

1. [Outcome and boundaries](#1-outcome-and-boundaries)
2. [Public contract and usage](#2-public-contract-and-usage)
3. [Definition validation](#3-definition-validation)
4. [Trigger admission and state commitment](#4-trigger-admission-and-state-commitment)
5. [Results and inspection](#5-results-and-inspection)
6. [Transition handlers and DI](#6-transition-handlers-and-di)
7. [Failure, cancellation and compensation](#7-failure-cancellation-and-compensation)
8. [Pipeline composition](#8-pipeline-composition)
9. [Automatic logging](#9-automatic-logging)
10. [Automatic metrics](#10-automatic-metrics)
11. [Hosting, examples and deliverables](#11-hosting-examples-and-deliverables)
12. [Extend the workflow skill](#12-extend-the-workflow-skill)
13. [Focused acceptance](#13-focused-acceptance)
14. [CIS implementation workflow](#14-cis-implementation-workflow)
15. [Implementation-agent prompt](#15-implementation-agent-prompt)
16. [References and completion](#16-references-and-completion)

## 1. Outcome and boundaries

The developer supplies a state enum, a trigger enum and explicit mappings for each state. The pair **current state + trigger** identifies at most one transition. A transition may change state directly or first run a DI-constructed asynchronous handler.

The builder freezes the definition. The built machine retains its current state and handles successive trigger calls. Each call returns its own immutable result. State changes only after the selected work and owned-resource cleanup succeed.

| ID | Requirement |
| --- | --- |
| SM-01 | Publish state-machine contracts under `CIS.Workflows`, alongside Pipeline. |
| SM-02 | Use enum states and triggers with explicit, unambiguous per-state mappings. |
| SM-03 | Support a builder, `Build(initialState)`, and asynchronous `Trigger(trigger)`. |
| SM-04 | Run at most one attempt at a time per instance; return `Busy` for overlapping/reentrant calls. |
| SM-05 | Commit the destination only after successful work and owned cleanup; otherwise retain the source. |
| SM-06 | Return an immutable per-call result and expose current state, permitted triggers and the last completed admitted attempt. |
| SM-07 | Construct the selected handler through DI for that attempt and retain dependencies through error handling/disposal. |
| SM-08 | Compensate only the current attempt's entered work; preserve original and secondary failures separately. |
| SM-09 | Provide automatic structured logs and bounded metrics through existing CISLIB modules. |
| SM-10 | Deliver compiling examples, focused native verification and an update to `cislib-workflows`. |

### Scope

V1 provides flat, explicit, in-process state transitions. Triggers are events raised by application code. Existing API handlers, message consumers or Heartbeat work may raise them.

V1 does not add guards, conditional-priority selection, entry/exit hooks, hierarchical/parallel states, timers, subscriptions, an internal trigger queue, automatic retries, persistent history, durable recovery or distributed locking. Application validation can return a failure from the selected handler.

State retention is in memory. Applications own persistence, external transactions and coordination between separate instances/processes representing the same business entity. Preserving an enum value does not automatically reverse external effects.

Use the accepted Pipeline and Metrics contracts for relevant lifecycle and telemetry conventions. Keep the new runner small: an immutable transition map, an instance execution gate, a state/report snapshot and per-attempt work. Do not build a generalized workflow engine or redesign product-wide result abstractions.

## 2. Public contract and usage

These are intended new C# signatures. Verify existing naming/integration conventions before implementation and synchronize the final API, examples and skill. Preserve the public `CIS.Workflows` namespace.

### 2.1 Consumer example

~~~csharp
public enum OrderState
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Cancelled
}

public enum OrderTrigger
{
    Submit,
    Approve,
    Reject,
    Revise,
    Cancel
}
~~~

Normal application composition obtains a builder through a DI-supplied factory, proposed as `IStateMachineFactory`:

~~~csharp
using CIS.Workflows;

var builder = machines.CreateBuilder<
    OrderState, OrderTrigger, OrderContext>(
        "OrderApproval", context);

builder.State(OrderState.Draft)
    .On<SubmitOrderTransition>(
        OrderTrigger.Submit, OrderState.Submitted)
    .On(OrderTrigger.Cancel, OrderState.Cancelled);

builder.State(OrderState.Submitted)
    .On(OrderTrigger.Approve, OrderState.Approved)
    .On(OrderTrigger.Reject, OrderState.Rejected)
    .On(OrderTrigger.Cancel, OrderState.Cancelled);

builder.State(OrderState.Rejected)
    .On(OrderTrigger.Revise, OrderState.Draft);

IStateMachine<OrderState, OrderTrigger> machine =
    builder.Build(OrderState.Draft);

var result = await machine.Trigger(
    OrderTrigger.Submit, cancellationToken);

if (result.Success)
{
    // result.CurrentState is Submitted.
}
else
{
    // Inspect result.Outcome, Error and cleanup diagnostics.
}
~~~

`StateMachineBuilder<TState,TTrigger,TContext>` binds the application's context and runtime integration. The built public interface needs only the state and trigger types; the context remains bound internally. Support language-native enum constraints in C#.

`On(trigger, target)` returns the state configuration for chaining. `On<THandler>(trigger, target)` attaches one concrete compatible handler. Neither form executes work during configuration or build.

### 2.2 Machine interface

~~~csharp
namespace CIS.Workflows;

public interface IStateMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    TState CurrentState { get; }
    bool IsExecuting { get; }

    IReadOnlyCollection<TTrigger> PermittedTriggers { get; }

    StateTransitionResult<TState, TTrigger>? LastTransition { get; }

    bool CanTrigger(TTrigger trigger);

    Task<StateTransitionResult<TState, TTrigger>> Trigger(
        TTrigger trigger,
        CancellationToken cancellationToken = default);
}
~~~

Expose interfaces for consumer mocking. Use established CISLIB testing helpers where available; this feature does not require a new testing framework/project.

A machine may receive many sequential triggers. Each built Pipeline instance still permits one execution; that Pipeline rule is not a state-machine lifetime restriction.

## 3. Definition validation

`Build(initialState)` validates and snapshots the mappings, names and options. The machine allocates no execution scope and constructs no handlers until a valid transition is attempted.

| Condition | Required behaviour |
| --- | --- |
| Ordinary enums | Accept declared, distinct values; numeric gaps/nonzero initial values are valid. |
| `[Flags]` on either enum | Reject: v1 values identify individual states/events, not combinations. |
| Aliases sharing an underlying value | Reject with a diagnostic identifying the aliases. |
| Undefined configured source/target/trigger or initial state | Configuration error. |
| Duplicate source/trigger pair | Reject, even if both registrations name the same target. |
| Declared state without outgoing mappings | Valid; its permitted-trigger set is empty. |
| Definition with no transitions | Valid when its initial state is declared; useful for a terminal-only instance. |
| Explicit self-transition | Valid; run its handler, if any, and return success with `StateChanged=false`. |
| Undefined trigger supplied at runtime | Return `Rejected` with `InvalidTrigger`; no handler construction. |
| Declared trigger without a mapping from current state | Return `Rejected` with `TriggerNotAllowed`. |
| Abstract/incompatible handler type | Configuration error; use DI construction, not a parameterless-constructor requirement. |

The same trigger may map differently from different source states. There is no implicit fallback, ignore rule, self-transition or first-registration-wins behaviour.

Later builder edits cannot alter an existing machine. Separate builds have independent state, gates and reports. A builder does not clone its context; provide a separate mutable context for independent machines.

State names, trigger names and machine aliases are stable definition metadata. Use a configured machine name such as `OrderApproval`; do not create one per customer, document or request. Keep enum/name handling within existing metadata and metrics bounds.

## 4. Trigger admission and state commitment

### 4.1 Admission rules

The term **admitted attempt** means a call that acquired the instance gate, including one later rejected for an invalid/unmapped trigger.

1. Begin the call's local duration measurement.
2. If the supplied token is already cancelled, return a `Cancelled` result without acquiring the gate, allocating services or changing `LastTransition`.
3. Try to acquire the gate immediately. If occupied, return `Busy` promptly. Do not wait, queue or execute this trigger later.
4. Capture the committed source state while owning the gate.
5. Observe cancellation, validate the trigger and resolve its mapping.
6. If no valid mapping exists, finalize a rejected result with no application work.
7. For a valid mapping, construct and execute only its selected handler, if present.
8. Finalize the result and publish the state/report snapshot before releasing the gate.

Reentrant calls from a handler into the same machine return `Busy`. The runtime does not hold a blocking thread lock across asynchronous work. Independent machine instances may execute concurrently.

A call that lost admission never modifies the admitted operation's report or state. No fairness or FIFO promise is made.

### 4.2 State commitment

During preparation, execution, error handling and owned cleanup, `CurrentState` remains the source.

After successful handler work, check execution cancellation once more, then select the success path and begin disposal. This is the **cancellation cutoff**. Cancellation arriving after that decision does not start compensation while services are being disposed.

After owned disposal succeeds, publish the target state and successful result atomically as one completed snapshot. The state assignment is an in-memory publication, not an asynchronous persistence callback.

A disposal failure prevents target-state commitment and produces `Failed`, although the handler's business work may already have succeeded. Do not invoke `Error` once disposal has begun.

For a transition without a handler, perform the final cancellation checkpoint and publish its target/result directly; no DI scope or handler telemetry is needed.

A self-transition follows the same rules. It is a successful configured operation, even though the state value is unchanged.

### 4.3 Safe inspection

Keep the transition definition immutable and expose thread-safe committed snapshots. `PermittedTriggers` is a read-only set/list for one observed committed state. `CanTrigger` checks only whether a mapping exists; it does not run validation work, predict handler success, perform I/O or reserve a transition.

The machine may be busy while `CanTrigger` is true. Every `Trigger` call rechecks admission and eligibility. Separate property reads may straddle a completed transition; the per-call result remains the authoritative snapshot for that operation.

## 5. Results and inspection

### 5.1 Result-based public completion

`Trigger` returns an immutable `StateTransitionResult<TState,TTrigger>` for ordinary operational outcomes:

| Outcome | Meaning | State effect |
| --- | --- | --- |
| `Succeeded` | Configured work and owned cleanup completed successfully. | Target committed; self-transition may leave value equal. |
| `Rejected` | Trigger undefined or unavailable from the captured state. | None. |
| `Busy` | Another call owns the instance gate. | This call changes nothing. |
| `Failed` | Activation, handler, result-contract or owned-disposal failure. | Source retained. |
| `Cancelled` | Execution cancellation was observed before its cutoff. | Source retained. |

**A `Cancelled` result completes the returned task normally.** It is returned only after entered work, required compensation and owned cleanup have settled. This preserves a stable result for every operational attempt.

Capture ordinary exceptions at application-operation boundaries: DI activation, handler methods, an awaited nested pipeline and owned disposal. Preserve diagnostic causes and log them; a programming defect inside a handler also produces `Failed`. The runtime cannot reliably distinguish that from another handler exception.

Definition/argument errors in configuration and defects in the state-machine implementation itself still throw. Do not convert broken engine invariants or process-fatal failures into an ordinary rejection.

This result-based boundary is specific to `Trigger`. The existing Pipeline outward exception/cancellation contract remains unchanged.

### 5.2 Required result data

| Field | Meaning |
| --- | --- |
| `MachineName`, `AttemptId` | Definition identity and per-call log/report identity. |
| `Success` | True only for `Succeeded`. |
| `Outcome` | One of the five outcomes above. |
| `Trigger` | Supplied trigger value, including an invalid enum value for diagnostics. |
| `SourceState` | Captured committed state for this attempt. |
| `TargetState` | Intended target when mapping resolution occurred; otherwise absent. |
| `CurrentState` | Committed state at finalization/observation for this report. |
| `StateChanged` | This attempt successfully committed a different state value. |
| `ErrorCode`, `ErrorMessage`, `Error` | Primary rejection/failure/cancellation information. |
| `Duration` | Actual monotonic elapsed duration; portable/exported units are seconds. |
| `HandlerReport` | Optional forward/error phase statuses, durations and failures. |
| `ErrorHandlingFailures` | Secondary failures/cancellation while compensating this attempt. |
| `LifecycleFailures` | Activation/disposal diagnostics, separately identified. |

Primary failure details include phase and finite kind (reported failure, exception, rejection, cancellation or cleanup timeout). C# may retain an exception as binding-specific diagnostic data. Public operational logic must not depend on .NET exception types or telemetry SDK objects.

Use stable library codes for `InvalidTrigger`, `TriggerNotAllowed`, `Busy`, cancellation, construction failure, invalid handler result and disposal failure; document the final prefix consistently. Preserve handler-supplied business failure codes. Do not use arbitrary error codes/messages as metric labels.

Do not include the mutable application context in an allegedly immutable result snapshot. Do not retain live handler instances or scoped providers through report fields.

### 5.3 LastTransition

`LastTransition` is initially null. It becomes the last completed **admitted** attempt's result, including a rejection, failure or cancellation after admission.

Busy calls and pre-cancelled calls return their own results without replacing it. Publish it with the completed state snapshot before releasing the gate.

For a Busy/pre-cancelled call, source/current are one thread-safe state observation; another admitted attempt may change the machine afterward. These values do not claim that the rejected call ran a transition.

Previously returned results never change. Retain only the current snapshot and last admitted result; do not create an unbounded execution history. Logging/persistence decisions belong to their existing owners.

## 6. Transition handlers and DI

### 6.1 Handler shape

A handler has the same two-phase shape as a pipeline step. The intended C# interface is:

~~~csharp
public interface IStateTransitionHandler<TState, TTrigger, TContext>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    Task<TransitionActionResult> Execute(
        TContext context,
        TransitionInfo<TState, TTrigger> transition,
        CancellationToken cancellationToken);

    Task Error(
        TContext context,
        TransitionInfo<TState, TTrigger> transition,
        StateTransitionFailure failure,
        CancellationToken cancellationToken);
}
~~~

`TransitionInfo` is immutable: source, intended target and trigger. `StateTransitionFailure` is immutable and retains the original cause. `TransitionActionResult` has `Success`, `ErrorCode` and `ErrorMessage`, with straightforward success/failure factories.

Reuse a compatible existing small result/report implementation where available, without initiating a shared result-contract redesign. Public names above describe this feature's intended contract and must be replaced consistently if an established equivalent is reused.

Expected business failure is an unsuccessful result. Null/invalid results are failed handler contracts, not successful transitions. A no-op `Error` returns a completed task.

### 6.2 Per-attempt activation

For a valid transition with a handler:

- Obtain one execution scope for this attempt.
- Construct a fresh selected handler through the existing DI activation facility; constructor dependencies resolve from that scope.
- Retain exactly that instance for its `Execute` and `Error`.
- Never construct every state's handlers at `Build`, and never retain attempt-scoped services between triggers.

As with Pipeline, generic handler registration means fresh activation for the registration, rather than sometimes resolving a shared singleton handler. Normal dependencies retain their configured lifetimes. Do not require a duplicate manual handler-registration list, mutate a built provider or create a second application container.

Use the established native DI activation path, such as `ActivatorUtilities` where supported by the existing dependency policy [S2]. Constructor work is synchronous setup; business side effects belong in `Execute`.

### 6.3 Scope ownership and disposal

The default is an attempt-owned scope, created when selected work begins and disposed before finalization.

Also support an explicit C# adapter path that borrows a caller-provided scope **for one trigger call**. Keep native provider types out of the portable machine interface. Publish the actual adapter name and a compiling example. Never infer ownership or retain a borrowed request provider for later calls.

The caller keeps a borrowed scope alive until that call settles. The machine never disposes the caller's scope/container.

The runtime owns DI-activated handler instances. Dispose the handler first, preferring asynchronous disposal when available, then dispose the owned scope. Keep dependencies alive through handler disposal. Do not dispose DI-owned dependencies individually or dispose the same object twice [S3].

Attempt all remaining runtime-owned cleanup after a disposal failure. Constructor failure invokes no `Error`, because no forward method was entered. The runtime cleans up the resources it actually received; a throwing constructor owns cleanup of private resources acquired before it returned.

The bound application context remains caller-owned. The machine owns no long-lived disposable services merely because its definition was built.

## 7. Failure, cancellation and compensation

### 7.1 Failure rules

If the handler returns failure, throws an ordinary exception, or acknowledges execution cancellation after entering `Execute`:

1. Await the actual termination of `Execute`.
2. Invoke that same handler's `Error` once, even if forward work only partially completed.
3. Pass the original immutable transition/failure and independent cleanup token.
4. Record an error-handler failure without replacing the original cause.
5. Dispose owned resources, produce the result and release the gate.

If the runtime's final checkpoint observes cancellation after the handler's `Execute` was entered, invoke its `Error` once even when the forward result was successful. Keep that forward phase recorded as successful; the outer attempt is cancelled and its compensation is recorded separately.

Unentered handlers receive no `Error`. Successful transitions receive no `Error`. Error-handler/disposal failures never start another compensation pass. Earlier successful state transitions are never unwound.

A failed or cancelled attempt retains the source enum, but the report must not imply that external effects were necessarily reversed. Keep cleanup failures visible. The engine performs no automatic retry, recovery-state transition or repair. Applications decide whether a subsequent trigger is appropriate after inspecting the result.

### 7.2 Cancellation

Pass the execution token into asynchronous work. Observe it before construction, before entering the handler and after successful work before the cancellation cutoff. If cancellation is observed after activation but before handler entry, dispose the handler without calling its `Error`.

Handle an observed unsuccessful result or unrelated exception before a subsequent cancellation checkpoint; cancellation must not relabel an already-observed failure.

Classify a cancellation exception as execution cancellation only when attributable to this attempt's requested execution cancellation. An unrelated cancellation exception is `Failed`. The result-based boundary preserves that distinction without rethrowing a cancellation-shaped task.

Cancellation is cooperative [S4]. Keep the gate and dependencies until the current method actually terminates. Never detach an uncooperative handler, run its compensation concurrently or admit another attempt while its work is still active.

No implicit forward timeout is introduced; callers may supply a token with their deadline.

### 7.3 Independent error-handling budget

Use the Pipeline convention: an independent cleanup token and a **30-second cooperative budget for this attempt's compensation**, configured through `ErrorHandlingTimeout`. Snapshot options at build time. Permit an explicit infinite budget; reject zero/negative finite values.

The execution token's cancellation must not immediately cancel cleanup. A cleanup cancellation exception is classified as such only when attributable to this cleanup token; an unrelated cancellation exception is an error-handler fault. Both remain secondary to the original failed/cancelled attempt.

The budget requests cancellation, not forced termination. Await compensation and asynchronous disposal; do not promise a hard 30-second completion bound.

### 7.4 Disposal failure

If forward work succeeds but owned disposal fails, return `Failed` with the source still current. Record that the handler's execution succeeded and disposal failed. Do not call `Error` on an already-disposed handler.

If disposal fails after an earlier failure/cancellation, retain the original outcome and attach lifecycle diagnostics. A `Cancelled` result with cleanup failures remains cancelled, with incomplete cleanup made explicit.

## 8. Pipeline composition

A transition handler may execute a fresh Pipeline for several application steps. Use the existing pipeline's DI/context integration and actual API.

The handler must inspect the pipeline's success result: normal return from `pipeline.Execute()` can represent an expected failed pipeline. Map that failure to an unsuccessful `TransitionActionResult`; never report transition success solely because the await returned.

If the pipeline throws or propagates cancellation, it first completes its own unwind/cleanup under its contract. The state-machine handler boundary captures the resulting exception or attributable cancellation into the trigger result.

Distinguish two composition cases:

- **Child failed or cancelled:** the child already attempted its own unwind. The outer handler must not repeat compensation of those same steps; it handles any additional work it owns. Retain or report relevant child cleanup diagnostics through the application's chosen mapping/logging.
- **Child succeeded, then the outer attempt fails or observes cancellation:** the successful child is not automatically unwound. Its execution scope has already ended. The outer handler must perform any required business compensation using retained identifiers/results, or explicitly accept the remaining effects. It must not call disposed child steps.

A no-op outer `Error` is appropriate only when no additional compensation is required. The state machine cannot infer reversibility from the child's success or failure alone.

When the handler and child need the same scoped dependency/transaction, use the existing explicit caller-scope integration. Await the child before releasing the scope.

Reuse existing lifecycle helpers where appropriate. Do not create a hidden generalized pipeline engine, emit duplicate measurements for the same boundary or make state-machine delivery depend on implementing unrelated Pipeline features.

## 9. Automatic logging

Use the existing CISLIB Logging facade and correlation context. The runtime records trigger attempts and selected handler phases without consumer wrappers.

| Event | Default |
| --- | --- |
| Attempt/handler phase starts | Debug. |
| Successful attempt or successful handler phase | Debug; configurable to Information for desired business visibility. |
| Busy or invalid/unmapped trigger | Debug with the distinct outcome/code. |
| Execution cancellation | Debug/Information according to existing cancellation policy; never an unexplained Error. |
| Expected handler failure | Warning with safe code/message. |
| Unexpected activation/handler/disposal failure | Error with original diagnostic cause. |
| Compensation failure or expired cleanup budget | Warning/Error with secondary cause and phase. |
| `CanTrigger`, `CurrentState`, `PermittedTriggers` reads | No per-read logs. |

Routine completion defaults to Debug to avoid flooding high-frequency event/Heartbeat-driven flows. Existing filtering, duplicate suppression and writer policy remain authoritative. Metrics continue to record when routine logs are filtered.

Each completion includes machine alias, attempt/correlation identity, source, trigger, intended target when known, final state, outcome, duration and phase/failure details where applicable. Include a secondary-failure count in the terminal summary rather than repeating every full exception.

Do not automatically serialize context/payloads or include secrets. Log `Busy` as a rejected admission and `TriggerNotAllowed` as a mapping rejection; neither is an executed failed handler.

Logging failures/filtering must not alter state commitment, outcomes or cleanup. Explicitly invoked child pipelines retain their own meaningful lifecycle logs.

## 10. Automatic metrics

Follow the current CISLIB Metrics contract and skill. Use the existing CISLIB facade, application-owned provider, aggregation, limits and `/_metrics` export. Verify actual APIs and names in the checkout.

Register the following stable instruments once per module/provider and reuse their handles across all instances. No per-entity meters, run histories, timers registry or separate exporter is required.

### 10.1 Metric catalog

| Logical name | Kind/unit and exported name | Dimensions beyond identity | Boundary and intended query |
| --- | --- | --- | --- |
| `cis.statemachine.trigger.duration` | Histogram/seconds; `cis_statemachine_trigger_duration_seconds_{bucket,sum,count}` | `machine`, `source`, `trigger`, `outcome` | Every actual public Trigger call through final outcome, including Busy, rejected and pre-cancelled calls. Attempt rates, outcomes and latency. |
| `cis.statemachine.active.attempts` | Up/down counter/count; `cis_statemachine_active_attempts` | `machine` | +1 after gate acquisition, -1 once on its final exit. Includes admitted validation/rejection and all work/cleanup; excludes Busy and pre-cancelled calls. |
| `cis.statemachine.handler.duration` | Histogram/seconds; `cis_statemachine_handler_duration_seconds_{bucket,sum,count}` | `machine`, `handler`, `phase`, `outcome` | Each entered handler Execute/Error through actual completion. Phase latency and compensation failures. |

Braced suffixes identify exported histogram child families, not literal instrument names.

All samples retain central `application` and `environment`, including histogram and overflow samples. Reuse the existing environment source and prevent reserved-label overrides.

Trigger outcomes map to `success`, `rejected`, `busy`, `error`, `cancelled`. Handler phases are `execute`/`error`; their outcomes are `success`/`error`/`cancelled`. Details such as business error codes and exception types stay in diagnostics unless already covered by an explicitly bounded shared contract.

### 10.2 Recording and cardinality

- Use monotonic elapsed time in seconds, independent of export availability, and record one valid terminal observation per owned boundary.
- Never-started methods generate no handler-duration observation. Construction/disposal faults affect the outer attempt without inventing a handler execution.
- A successful handler later compensated retains its successful forward observation; compensation has a separate phase observation.
- Keep active attempts incremented until cleanup and the final state/report decision finish; decrement in every admitted exit path.
- Derive completed counts and rates from histogram counts [S5]. Do not add duplicate completed counters or an application rate-sampling loop.
- Use cumulative provider-lifetime aggregates; do not reset on each machine, attempt or scrape.
- Reuse the Metrics fixed duration profile and its allowed overrides. The reference v1 finite seconds boundaries are 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10.

Use stable configured machine/handler aliases and declared enum names. Normalize an undefined runtime trigger to one fixed label, `invalid`, before recording; never stringify arbitrary underlying values into new labels. If an enum member would collide with that reserved label, use a documented noncolliding fixed representation.

For a gated call, capture source under the gate and retain that dimension through completion. Busy/pre-cancelled calls use their observed committed-state snapshot.

Budget for rejected combinations as well as configured transitions. A conservative trigger-duration capacity is:

`5 × sum over machine definitions [declared states × (declared triggers + 1 invalid category)]`.

This can exceed a generic histogram's default capacity even for a small definition. Declare an adequate profile within the existing total Metrics policy; do not silently accept ordinary workload overflow or expand limits automatically. Handler duration has up to six outcomes/phase combinations per admitted machine/handler pair; active attempts need one series per machine alias.

Exclude instance/attempt IDs, customer IDs, correlation IDs, raw payloads and arbitrary error messages from labels. Do not expose a current-state gauge for each business entity.

Runtime telemetry failures/overflow follow existing Metrics policy and cannot alter transition behaviour or readiness. Reuse SDK aggregation and existing no-op/overflow fixtures. Explicit child-pipeline measurements have a different operation boundary; use the outer trigger metric for state-machine attempt volume.

### 10.3 Example query

After verifying exported names:

~~~promql
sum by (application, environment, machine, outcome) (
  rate(cis_statemachine_trigger_duration_seconds_count[5m])
)
~~~

Apply appropriate identity selectors in actual dashboards. Distinguish `rejected` and `busy` from execution errors. Missing/stale/overflow-filtered observations are not evidence of zero work.

## 11. Hosting, examples and deliverables

Integrate the state-machine factory, runtime and instrumentation into the existing `CISApplication`/host composition. Keep registration idempotent and preserve existing provider/readiness/shutdown ownership. Use the current host extension if the facade is not on the target branch; do not rebuild Startup as part of this feature.

Extend the consolidated example application with one compact state-machine scenario demonstrating:

- Enum definitions and state-dependent trigger mappings.
- A direct transition and a DI-dependent transition handler.
- Successive triggers on one instance.
- An unmapped trigger, an expected work failure and report inspection.
- Automatic logs/metrics and a documented borrowed-scope usage example.
- Actual pipeline composition when the existing Pipeline API is available.

Use deterministic in-memory collaborators for failure demonstrations and tests. Do not require live external services or create another standalone sample application unnecessarily.

| Deliverable | Proposed destination |
| --- | --- |
| Supplied feature input | Preserve this file through the existing CIS feature intake. |
| Portable contract | `contracts/workflows/statemachine/README.md`. |
| C# implementation | Appropriate existing project under `Workflows/StateMachine/`; public namespace `CIS.Workflows`. |
| Consumer guide | `docs/modules/workflows/statemachine.md`. |
| Metrics catalog | Existing canonical catalog plus the module's three owned instruments. |
| Native tests/example | Existing affected projects and consolidated example. |
| Usage skill/routing | Update `.github/skills/cislib-workflows/` and the root `AGENTS.md` entry. |

Map these paths once to actual repository conventions and repair references. Preserve public compatibility and dependency policy; no external state-machine package is required by this contract.

## 12. Extend the workflow skill

Update the **existing `cislib-workflows` skill** so it covers Pipeline and State Machine. Preserve its pipeline guidance and distinguish the two lifetimes and completion contracts.

This is a repository documentation deliverable, not installation of a personal ChatGPT skill.

### 12.1 Generation instructions

1. Inspect the current skill inventory/frontmatter and canonical Workflows documentation.
2. Finish the state-machine API and a compiling example before publishing concrete usage instructions.
3. Update the existing skill in its recognized directory. If absent, create the combined repository skill from the accepted pipeline guidance and this section, reflecting only APIs actually implemented on the branch.
4. Add direct links to the state-machine contract/guide. Keep the skill concise; do not duplicate this full specification or add a second overlapping state-machine skill.
5. Update supported `agents/openai.yaml` metadata and root routing. Use the current repository's scaffold/generator/validator where available, after checking its help.
6. Validate naming, YAML, discoverability, links and example compilation. Keep changes outside managed CIS instruction blocks.
7. Perform one focused consumer usage check using the skill and linked repository files, then commit these repository changes with the module.

Do not create personal-skills installation artifacts, a new evaluation framework or extra approval stages.

### 12.2 Frontmatter and routing within SKILL.md

Proposed updated frontmatter:

~~~yaml
---
name: cislib-workflows
description: Implement, use, review and diagnose CIS.Workflows pipelines and enum-based state machines. Use for builder definitions, sequential Execute/Error, state-dependent Trigger mappings, DI handlers, execution/transition reports, cancellation and automatic logging or metrics. Follow only workflow APIs implemented in the current repository.
---
~~~

At the start of the body, route the task to the appropriate canonical material:

| Task | Guidance |
| --- | --- |
| Ordered steps with reverse error handling | Existing Pipeline contract, guide and example. A built pipeline executes once. |
| Enum states and state-dependent events | State-machine contract, guide and example. A built machine accepts successive Trigger calls. |
| Instrumentation changes | Existing CISLIB Metrics skill and the owning module's metric catalog. |

### 12.3 State-machine section to add

Adapt links and names to the final implemented API:

~~~~markdown
## State machines

Read the [state-machine contract](../../../contracts/workflows/statemachine/README.md)
and [consumer guide](../../../docs/modules/workflows/statemachine.md).
Use the compiling consolidated example linked by that guide.

1. Define state and trigger enums with distinct values and explicit mappings.
   Use a stable machine name. Keep definitions separate from per-instance state.
2. Configure State(...).On(trigger, target), or attach a concrete DI handler with
   On<THandler>(). Build with an explicitly declared initial state.
3. Await Trigger for each event. A machine supports successive calls; overlapping
   and reentrant calls return Busy and are not queued.
4. Treat the returned immutable result as the outcome of that call. Inspect
   Success, Outcome, source, intended target, actual state and failure details.
   Operational failure/cancellation returns a result; Cancelled does not mean
   the returned task was abandoned or that its cleanup was skipped.
5. Keep application dependencies in handler constructors. Use the established
   per-attempt activation/scope path and keep any borrowed scope alive until
   the call finishes. Do not retain request-scoped services in a reusable machine.
6. Implement Error for partial work as well as completed work. The entered
   failing handler receives it. Use the provided independent cleanup token.
   Preserve original versus secondary failure details.
7. If a handler executes a Pipeline, build a fresh pipeline for that attempt,
   inspect its Success after awaiting, and avoid repeating an unwind it already
   attempted. If the child succeeded but the outer attempt later fails/cancels,
   supply any required compensation from retained results; a successful child
   is not automatically unwound. Preserve the Pipeline's own single-use and
   exception/cancellation contract.
8. Never set the machine's state speculatively. It commits after successful work
   and owned cleanup. Source-state retention does not prove external rollback.
9. Use CanTrigger/PermittedTriggers for mapping inspection only. Trigger rechecks
   eligibility and may still return Busy or a work failure.
10. Preserve automatic Logging/Metrics. Keep routine inspection quiet and names
    bounded. Use logs/reports for IDs and detailed errors.

Use the existing CIS/native test workflow. Keep this section, its links and
examples aligned with the actual implementation. Add guards, entry/exit hooks,
queues, persistence or other workflow facilities only when separately specified.
~~~~

### 12.4 Metadata and root routing

Update supported `agents/openai.yaml`:

~~~yaml
interface:
  display_name: "CISLIB Workflows"
  short_description: "Build CISLIB pipelines and enum state machines"
  default_prompt: "Use $cislib-workflows to define, execute and inspect a CIS.Workflows pipeline or state machine with DI and automatic telemetry."
~~~

Root `AGENTS.md` routing should direct workflow work to this shared skill and explicitly distinguish:

- Pipeline: one execution, sequential steps and reverse error handling.
- State Machine: repeated explicit triggers, per-state mappings, exclusive attempts and commit after successful work/cleanup.
- Both: DI ownership, accurate reports, cancellation and existing Logging/Metrics.

Preserve all unrelated instructions and the pipeline-specific content.

### 12.5 Focused skill-use check

From the updated skill and linked files alone, define Draft/Submitted/Approved states with Submit/Approve triggers. Use a DI handler on Submit. Demonstrate successful submission, rejected Approve from Draft on a separate instance, and a controlled handler failure retaining Draft. Inspect each returned result and use the existing build/test path.

Check that the generated consumer code does not reuse a single-use child pipeline or mistake a cancelled transition result for a successful state change. One such check is sufficient; no new harness is required.

## 13. Focused acceptance

Use existing native fixtures and controlled asynchronous collaborators. Rows are obligations, not a required test count or separate project per case.

| ID | Acceptance and focused evidence |
| --- | --- |
| A-01 | Enum/mapping validation: initial/target/source values, aliases/Flags, duplicate pairs, frozen definitions, terminal-only and explicit self-transition behaviour. |
| A-02 | Correct source/trigger selects exactly one mapping; direct and handler-based transitions commit only on success. |
| A-03 | Controlled handler/disposal proves source state stays visible until successful final publication; disposal failure leaves source. |
| A-04 | Successive calls use the new state; independent instances are isolated; old results remain immutable and no full history accumulates. |
| A-05 | Undefined/unmapped triggers return distinct rejection diagnostics without handler construction; CanTrigger/PermittedTriggers remain side-effect free. |
| A-06 | Concurrent and handler-reentrant calls return Busy promptly, are never queued, and cannot overwrite the admitted result/state. |
| A-07 | Expected failure, ordinary handler exception and invalid result invoke the entered handler's Error once and return the specified failed result. |
| A-08 | Compensation failure/cancellation remains secondary; previous successful transitions are not unwound. A failed child is not compensated twice, and a successful child followed by outer failure uses the declared outer compensation policy. |
| A-09 | Pre-cancellation, before-handler, during-work and final-checkpoint cancellation return stable cancelled reports; late cancellation after cutoff does not undo success. Unrelated cancellation exceptions are faults. |
| A-10 | No abandoned work: gate/dependencies remain held until the active method/compensation settles; separate cleanup token and configurable budget behave cooperatively. |
| A-11 | Per-attempt DI activation, same instance across phases, caller/owned scope lifetime and exactly-once disposal. Construction/disposal failures report their actual phase. |
| A-12 | Result fields, nullable target, phase reports, absent duration for unentered methods, Busy/pre-cancel snapshots and LastTransition publication follow sections 4–5. |
| A-13 | Automatic logs cover attempt/handler outcomes with correlation and safe diagnostics; routine queries do not log and default routine levels avoid flooding. |
| A-14 | Outer and handler observations occur once at their boundaries; active counts balance including admitted rejection; invalid enum labels stay bounded and no handler sample is invented. |
| A-15 | One actual exposition check verifies names/units/identity with existing Metrics fixtures; disabled/failing telemetry preserves behaviour. Example, updated shared skill, routing and focused usage check agree with compiling APIs. |

Parameterize related cases and reuse existing DI/observability fixtures. A small controlled concurrency test is required because gate/commit behaviour is part of the contract; a broad stress campaign is not.

Do not add a bespoke orchestration harness, exhaustive arbitrary-state combinations, mutation campaign, broad benchmark project or large evidence bundle. Expand verification only for a concrete uncovered risk or a required current gate. Report actual native results and reuse valid unchanged evidence.

## 14. CIS implementation workflow

Preserve this document as a feature input in the existing CISLIB product. Inspect current repository instructions, dependency policy, implementation APIs, branch state and installed CIS help before applying changes.

Import through the supported CIS feature/BRD route, derive stories/tasks no larger than medium, and attach the relevant acceptance rows. Do not recreate the workspace, replace the product-wide BRD, fabricate execution authority or claim tests from source inspection.

Use three coherent outcomes as the default:

| Outcome | Ownership and dependency |
| --- | --- |
| 1. Contract and runtime | Workflows API, enum definition/builder, gate, trigger selection, state commitment, result/report policy and focused native tests. One owner controls shared API files. |
| 2. Integration and instrumentation | Handler activation/scope/cleanup, host wiring, logs, metric catalog and targeted integration checks after API stability. |
| 3. Consumer enablement | Consolidated example, guide, shared skill/metadata and routing; may proceed alongside outcome 2 after API stability, with completion tied to the real example build. |

Assign nonoverlapping files for parallel work and one owner for shared host/solution/package changes. Keep documentation, tests, review and compact evidence with the outcome they verify.

User authorization persists. Do not add routine approval gates for derivation, implementation, skill updates, focused verification or safe metadata refresh. If a real current gate blocks an action, finish authorized preparation and identify its exact rule and concrete blocked step.

Reuse current accounting/results. Stop optional testing when acceptance and applicable gates are covered. Delivery of this module is not a claim of release qualification or deployment.

## 15. Implementation-agent prompt

> Implement the CISLIB State Machine feature from this specification under CIS.Workflows. Inspect the current checkout, instructions, dependency policy, existing Workflows/Logging/Metrics APIs, consolidated example and installed CIS workflow first.
>
> Preserve and import the supplied document as feature input, derive a bounded plan and execute eligible work in parallel with clear ownership. Do not invent repeated approval stages.
>
> Deliver enum state/trigger definitions, explicit builder mappings, Build(initialState), a reusable interface-based machine and asynchronous Trigger calls. Return stable per-call outcomes, reject concurrent/reentrant calls as Busy, and commit state only after successful work and owned cleanup.
>
> Use DI-constructed per-attempt handlers, original/secondary failure reporting, cooperative execution cancellation and independent compensation cancellation. Preserve Pipeline behaviour when composing a fresh child pipeline; do not retry or compensate child-owned work twice.
>
> Reuse automatic Logging/Metrics with bounded dimensions, central identity, no-op behaviour and correct measurement ownership. Keep routine inspection silent and routine events configurable.
>
> Extend the existing cislib-workflows repository skill, supported metadata and AGENTS routing while preserving Pipeline instructions. Generate concrete instructions from the implemented API and compiling consolidated example, validate them with current tooling and perform the small usage check.
>
> Use the focused native acceptance matrix and existing fixtures. Do not add guards, entry/exit hooks, queues, persistence, future modules or new test infrastructure. Finish with implemented API/paths, focused results and actual remaining limitations.

## 16. References and completion

### Source basis

The accepted user discussion on 18 November 2026 governs this feature. This specification was prepared without reviewing a current CISLIB source checkout. Proposed public helper names and repository paths must be reconciled with actual implementation conventions before publication.

Companion requirements:

- [CISLIB Workflows — Pipeline specification v1.0][P1], including its DI ownership, cleanup budget and outward execution contract.
- CISLIB Metrics implementation specification v1.0 and agent guide, dated 17 November 2026, plus any newer explicitly accepted repository catalog/skill.
- The current repository's CIS, startup, logging and dependency instructions.

Primary platform/design references support the underlying concepts, not permission to add dependencies:

- [S1: Stateless project documentation][S1] — state/trigger mappings, permitted-trigger inspection and asynchronous state-machine concepts. This contract does not require that package or its broader feature set.
- [S2: DI-assisted construction][S2].
- [S3: DI ownership and disposal][S3].
- [S4: Cooperative cancellation][S4].
- [S5: Histogram counts and distributions][S5].

### Definition of done

SM-01 through SM-10 and A-01 through A-15 are satisfied within scope. The public API lives under CIS.Workflows; actual transitions, results, DI lifetimes and logs/metrics agree; the consolidated example compiles; and the updated shared workflow skill guides future agents to the implemented usage.

Report native verification results and remaining limitations. A copied specification or skill template alone is not evidence of implemented behaviour.

[P1]: https://chatgpt.com/library?file_id=libfile_fde3_f-0aa2323767088191869136777f5dd83b_a96453786d9081918df59aa18e3ca617&account_id=personal
[S1]: https://github.com/dotnet-state-machine/stateless
[S2]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.activatorutilities.createinstance?view=net-10.0-pp
[S3]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines
[S4]: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
[S5]: https://prometheus.io/docs/practices/histograms/

