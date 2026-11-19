# CISLIB Workflows — Pipeline implementation specification

**Version:** 1.0  
**Date:** 18 November 2026  
**Public namespace:** `CIS.Workflows`  
**Audience:** implementation agents and application developers  
**Delivery:** C# first, targeting the repository's .NET 10 baseline  
**Status:** implementation handoff based on the accepted design in this conversation

This file contains the feature requirements, API contract, lifecycle decisions, instrumentation catalog, focused acceptance cases, implementation instructions, and repository skill-generation instructions. It does not claim that the proposed types or helpers already exist in CISLIB.

## Contents

1. [Outcome and scope](#1-outcome-and-scope)
2. [Namespace and integration boundaries](#2-namespace-and-integration-boundaries)
3. [Consumer API](#3-consumer-api)
4. [Builder and instance lifetime](#4-builder-and-instance-lifetime)
5. [Execution and reverse error handling](#5-execution-and-reverse-error-handling)
6. [Results, reports and outward completion](#6-results-reports-and-outward-completion)
7. [DI construction and ownership](#7-di-construction-and-ownership)
8. [Cancellation and cleanup](#8-cancellation-and-cleanup)
9. [Automatic logging](#9-automatic-logging)
10. [Automatic metrics](#10-automatic-metrics)
11. [Host integration and working example](#11-host-integration-and-working-example)
12. [Repository deliverables](#12-repository-deliverables)
13. [Generate the repository skill files](#13-generate-the-repository-skill-files)
14. [Focused acceptance](#14-focused-acceptance)
15. [CIS implementation workflow](#15-cis-implementation-workflow)
16. [Implementation-agent prompt](#16-implementation-agent-prompt)
17. [Sources and definition of done](#17-sources-and-definition-of-done)

## 1. Outcome and scope

Add a simple pipeline as the first flow-control facility in the **Workflows** family.

A developer declares an ordered list using `builder.Add<Step>()`, calls `Build()`, and awaits the built instance's `Execute()`. Each step has asynchronous `Execute` and `Error` methods. Forward execution continues only after success. A failure unwinds every step whose execution began, including the failing step, in the opposite order.

The pipeline instance retains its outcome and an inspectable report of all configured steps, the steps actually entered, forward failures, and error-handler failures. DI constructs the steps and supplies their dependencies. Logging and metrics are automatic for the pipeline and both step phases.

### 1.1 Required behaviour

| ID | Requirement |
| --- | --- |
| WF-01 | Publish this facility under `CIS.Workflows`, leaving room for later workflow modules. |
| WF-02 | Preserve explicit registration order; await one step at a time. |
| WF-03 | Stop forward execution on the first unsuccessful result, exception or observed execution cancellation. |
| WF-04 | Call `Error` on the failing/entered step and earlier entered steps, sequentially in reverse order. |
| WF-05 | Continue unwinding after an error handler fails; retain the original cause and secondary failures separately. |
| WF-06 | Provide `Add<TStep>()`, `Build()`, and an instance `Execute()`; each built instance represents one execution. |
| WF-07 | Keep one shared typed context and an ordered report with separate forward and error-handling statuses. |
| WF-08 | Construct steps through DI activation, resolve dependencies, retain instances through unwind, and respect ownership. |
| WF-09 | Instrument every accepted pipeline invocation and every entered step method using existing Logging and Metrics. |
| WF-10 | Supply actual compiling examples, consumer documentation, a repository usage skill and instruction routing. |

### 1.2 Deliberate limits

This delivery covers sequential in-process execution and application-defined compensation. It does not implement branching, parallel stages, retries, persistent workflow storage, restart recovery, distributed transactions, scheduling, a graph editor, or additional workflow modules.

`Error` is the application's compensation/cleanup method. It is not an automatic database rollback or a promise that every external effect can be reversed. The runtime does not restore context snapshots. Process termination and unrecoverable runtime failures are outside the in-process unwind guarantee.

Keep the runtime small: an ordered definition, execution-local state, an entered-step stack, and existing DI/logging/metrics integrations. Do not introduce a generalized workflow engine or redesign other modules' result contracts.

## 2. Namespace and integration boundaries

Place public pipeline types in **`CIS.Workflows`**. Use descriptive names such as `PipelineBuilder<TContext>`, `IPipeline<TContext>`, `IPipelineStep<TContext>`, `PipelineStepResult`, `PipelineFailure`, and `PipelineStepReport`.

A source folder such as `Workflows/Pipeline/` is appropriate; it does not require a further public `Pipelines` namespace. Future modules can use the Workflows namespace when separately specified. Do not create empty future types or speculative shared base classes.

Before implementation, inspect the actual checkout, root instructions, dependency policy, startup integration, existing Logging and Metrics contracts, and native test conventions. Preserve existing public compatibility. The namespace choice does not itself authorize a new package or dependency.

Keep ordering, failure meanings, report fields, metric meanings and lifecycle semantics common across future languages. C# uses native asynchronous and cancellation primitives. Keep DI provider types in C# composition helpers and OpenTelemetry/collector types behind the existing Metrics adapter. C# exception objects may be retained as diagnostic details; portable logic must not depend on them.

## 3. Consumer API

The following is the intended new C# contract, not evidence of an existing API. Settle any necessary mapping to established repository composition conventions in the first implementation outcome and update every example and skill to the implemented names.

### 3.1 Step contract

~~~csharp
namespace CIS.Workflows;

public interface IPipelineStep<TContext>
{
    Task<PipelineStepResult> Execute(
        TContext context,
        CancellationToken cancellationToken);

    Task Error(
        TContext context,
        PipelineFailure failure,
        CancellationToken cancellationToken);
}
~~~

`PipelineStepResult` exposes `Success`, `ErrorCode` and `ErrorMessage`. Provide simple success/failure factories, for example `Succeeded()` and `Failed(code, message)`. An expected business failure is an unsuccessful result. A successful result has no failure details; a failed result requires a nonempty stable error code. Validate invalid/null results as step contract failures, with unwind, instead of treating them as success.

`Error` receives the same context and original failure for every participating step. Its successful completion means the handler completed; it does not mean that all effects were reversed. A handler with no work returns a completed task. An exception from the handler is retained as its error-handling failure.

### 3.2 Builder and invocation

Obtain a DI-integrated builder through a small host-provided factory. The proposed factory is `IPipelineFactory.CreateBuilder<TContext>(name, context)`; it supplies the runtime dependencies without making the consumer resolve services manually.

~~~csharp
using CIS.Workflows;

// pipelines is supplied through DI.
// context is a fresh context for this invocation.
var builder = pipelines.CreateBuilder("CreateCustomer", context);

builder.Add<ValidateCustomerStep>();
builder.Add<CreateCustomerStep>();
builder.Add<CreateDocumentFolderStep>();
builder.Add<FinalizeCustomerStep>();

var pipeline = builder.Build();

await pipeline.Execute(cancellationToken);

if (!pipeline.Success)
{
    var originalFailure = pipeline.Error;
    var failedStep = pipeline.FailedStep;
    var allSteps = pipeline.Steps;
    var errorHandlingFailures = pipeline.ErrorHandlingFailures;
}
~~~

`Add<TStep>()` returns the builder for chaining. Require a concrete step implementing `IPipelineStep<TContext>`; do not impose a parameterless-constructor constraint.

The built instance exposes `Task Execute(CancellationToken cancellationToken = default)`, so `await pipeline.Execute()` is valid. Expected failure returns normally after cleanup; exceptional and cancellation completion are specified in section 6. Inspect the same instance in a catch block when execution throws.

The pipeline name is stable application configuration, for example `CreateCustomer`; it is not a name generated from the current request. A step name defaults to its registered type's stable display name. If an explicit alias is needed, permit a small optional alias argument without changing `Add<Step>()` usage.

## 4. Builder and instance lifetime

- `Add<TStep>()` appends one registration. Duplicate step types are allowed and remain distinct by registration position.
- `Build()` validates the definition and snapshots registrations, names and options. It allocates no execution scope and constructs no steps.
- Reject an empty definition, incompatible/abstract step types, invalid names and invalid options with clear configuration/argument errors. Do not start business work during validation.
- Later builder changes cannot change an existing pipeline. Builders are configuration objects and need not support concurrent mutation.
- A built pipeline starts in `NotStarted`. Its first `Execute` call claims execution atomically.
- Reject concurrent or subsequent calls on the same instance with a usage error, without altering the accepted run, its report or its telemetry.
- A new run requires a new built instance and a fresh mutable context. Reusing a builder must not be assumed to clone its context.
- Independent pipeline instances may run concurrently; they share no execution stack, mutable report or step instance.

Use O(number of configured steps) execution/report state. Do not create a global history, run registry, unbounded cache of executions, or retention service.

## 5. Execution and reverse error handling

After claiming the invocation, obtain the execution scope and construct all steps. Then, in registration order:

1. Observe execution cancellation before admitting the next step.
2. Mark the registration as entered immediately before invoking `Execute`.
3. Retain it on the unwind stack and await its `Execute`.
4. Record that forward invocation's result and duration.
5. Continue only after success and the cancellation checkpoint.

The runtime invokes each configured registration at most once per run. It does not retry.

On a returned failure, ordinary unexpected exception or observed cancellation, stop forward admission. Once the current forward invocation has terminated, enter `Unwinding` and pop entered registrations in reverse order. Await each `Error` once, passing the original failure and the cleanup token. Record a handler failure and continue to the next registration.

| Registration | Forward outcome | Unwind position |
| --- | --- | --- |
| A | Succeeded | Third |
| B | Succeeded | Second |
| C | Failed after entering `Execute` | First |
| D | Never entered | Never called |

A construction failure does not make a step eligible for `Error`. An unentered step never receives `Error`, even if its instance was already constructed.

On complete forward success, call no `Error` handlers. Perform owned-resource disposal on every terminal path. An error-handler or disposal failure never initiates a second unwind. Once disposal begins, do not call business handlers again.

Business compensation and resource disposal are separate: `Error` runs on unsuccessful execution; disposal also runs after success. Constructors must limit themselves to dependency setup. Irreversible application work belongs in `Execute`, where it has an observable lifecycle.

## 6. Results, reports and outward completion

### 6.1 Pipeline state

Use these overall states: `NotStarted`, `Running`, `Unwinding`, `Succeeded`, `Failed`, `Cancelled`.

Keep the pipeline nonterminal while activation, forward work, error handling or owned-resource disposal is still in progress. Publish its terminal state only after those operations finish or fail according to the policy below.

| Property | Required meaning |
| --- | --- |
| `Name`, `ExecutionId` | Stable definition name and execution identifier for logs/report correlation. |
| `Status` | Overall lifecycle state. |
| `Success` | True only for terminal `Succeeded`. |
| `Context` | The supplied context; no automatic cloning, rollback or disposal of caller-owned values. |
| `Error` | Original failure/cancellation, including phase and originating registration when known. |
| `FailedStep` | Derived registration whose `ExecutionStatus` is `Failed`; otherwise null. A cancelled step is identified by `Error` and its `Cancelled` step report. |
| `Steps` | Ordered read-only reports for every configured registration. |
| `ExecutedSteps` | Derived view of registrations whose `Execute` was entered, including the failing/cancelled step. |
| `ErrorHandlingFailures` | Failed or cancelled error-handler outcomes, in observed unwind order. |
| `LifecycleFailures` | Construction/disposal diagnostics with their phases, independent of forward-step status. |
| `Duration` | Nullable elapsed duration; final once terminal. C# may use `TimeSpan`; portable/exported durations mean seconds. |

A construction failure is available through `Error` and the lifecycle diagnostics; that does not mark its step as executed. A lifecycle diagnostic may identify the same primary cause; do not count it as a second incident.

### 6.2 Per-registration report

Retain position, step name, `ExecutionStatus`, `ErrorStatus`, `ExecutionError`, `ErrorHandlingError`, `ExecutionDuration`, and `ErrorDuration`.

- Forward statuses: `NotStarted`, `Running`, `Succeeded`, `Failed`, `Cancelled`.
- Error statuses: `NotCalled`, `Running`, `Succeeded`, `Failed`, `Cancelled`.
- Duration is absent when that method was never entered; do not fabricate zero-duration executions.
- Successful forward execution remains successful if its later error handler fails.
- Report collections expose snapshots/read-only views, not live service instances or writable engine state.
- Concurrent inspection must be safe. A final consistent report is guaranteed after the awaited call returns, faults or is cancelled.
- Release internal step/provider references after disposal; retain the report and context. Applications must not place execution-owned disposable services in the context expecting them to remain usable.

`PipelineFailure` is immutable and carries a code, message, phase (`Construction`, `Execute`, `Error`, `Disposal`), optional registration identity, and a finite kind such as reported failure, exception, cancellation or cleanup timeout. Preserve the original exception for C# diagnostics and rethrowing when applicable. Do not let cleanup overwrite the original cause.

### 6.3 Completion contract

| Situation | Final report | Behaviour of `Execute()` after cleanup |
| --- | --- | --- |
| Every step succeeds; disposal succeeds | `Succeeded` | Return normally. |
| Step returns an expected failure | `Failed`; reported cause retained | Unwind, dispose and return normally. |
| Step throws an unexpected exception or violates its result contract | `Failed`; original execution cause retained | Unwind, dispose and rethrow the original exception with its stack preserved. |
| Caller cancellation is observed | `Cancelled`; cancellation origin retained | Unwind entered steps, dispose and propagate cancellation. |
| Construction fails | `Failed`; construction phase and registration retained; all steps unentered | Dispose successfully constructed instances/owned scope and propagate the construction exception. |
| Error handler fails or is cancelled | Original `Failed`/`Cancelled` outcome plus secondary diagnostics | Continue unwinding; preserve the original completion behaviour. |
| Disposal fails after otherwise successful execution | `Failed`; disposal phase; forward steps remain `Succeeded` | Attempt remaining owned disposal, then propagate the first disposal failure. |
| Disposal fails after an existing failure/cancellation | Original outcome plus lifecycle diagnostics | Preserve original completion behaviour and primary cause. |

A cancelled pipeline with cleanup failures remains `Cancelled`; those failures remain visible. Do not classify it as successful compensation. Do not flatten every exception into a business result or rethrow a cleanup exception in place of the originating failure.

**Narrow C# cancellation-exception rule:** when a primary exception is an `OperationCanceledException` but the accepted pipeline outcome is `Failed`, propagate a non-cancellation `PipelineExecutionException` with the original exception as its inner cause. Preserve that original exception in the report. Apply this rule consistently to construction, forward execution and disposal; it is the only exception here to rethrowing the original exception directly. This keeps an unrelated cancellation exception from being exposed as an accepted caller-cancellation outcome. Error-handler exceptions remain collected secondary failures. Reuse an existing compatible execution-exception type if the repository already defines one, and document its actual name.

## 7. DI construction and ownership

### 7.1 One explicit activation policy

`Add<TStep>()` means **construct a fresh instance for this ordered registration through DI activation**, resolving constructor dependencies from the execution provider. It does not resolve a pre-existing singleton registration of the step itself.

Use the existing C# DI activation facility, such as framework `ActivatorUtilities` where already available under the dependency policy. This facility resolves constructor arguments through an `IServiceProvider` [S1]. Do not implement constructor discovery/selection manually, mutate an already-built provider, create a second application provider, or require duplicate `AddTransient<Step>()` registrations.

This policy gives repeated occurrences of the same step type independent instances. Dependencies still honour their registered singleton, scoped or transient lifetimes. Normal service registrations supply those dependencies.

Activate all steps in registration order inside `Execute`, before any forward invocation. Check cancellation between activations; constructors are synchronous. If activation fails, stop preparation and dispose all successfully returned step instances and any owned scope. No step's `Error` runs.

Retain the exact instance used by each forward invocation for its error handler.

### 7.2 Scope modes

Support two explicit C# composition paths, documented with real examples:

| Mode | Ownership |
| --- | --- |
| Default execution scope | The runtime creates one scope at execution entry and disposes it before completion. All steps use that scope. |
| Explicit caller scope | A C# adapter receives the caller's scoped provider. It borrows that provider and never disposes the caller's scope or container. |

Do not infer ownership from an arbitrary provider or claim that a newly created scope inherits request-scoped instances. The explicit caller-scope path enables use of an existing request transaction or other request-scoped dependency.

The caller must keep a borrowed scope alive until `Execute` settles. A built-but-unused pipeline owns no scope or constructed instances.

### 7.3 Resource cleanup

Activator-created step instances are owned by the pipeline; their DI-resolved dependencies remain owned by DI. This distinction matters because an activated object outside container ownership is not automatically tracked for disposal [S1][S2].

After forward execution/unwind:

1. Dispose successfully constructed step instances in reverse construction order, including constructed steps never entered.
2. Prefer `IAsyncDisposable` when implemented; otherwise use `IDisposable`. Never call both on the same instance.
3. Keep the execution scope alive while step disposal runs.
4. Dispose the owned scope once; never dispose a borrowed scope.
5. Continue attempting remaining runtime-owned step disposals after an individual failure. Record and preserve failures under section 6; the container owns disposal of its own services.

Steps must not manually dispose injected dependencies owned by DI. A constructor that throws before returning an instance must clean up any private resources it acquired; the runtime cannot dispose an object it never received.

## 8. Cancellation and cleanup

Use cooperative cancellation throughout. A cancellation request is not proof that the running operation has terminated; .NET cancellation requires the operation to observe and respond to the token [S3].

### 8.1 Forward cancellation

- Claim the invocation, then check a pre-cancelled token before allocating a scope or activating steps. The instance becomes `Cancelled` and is consumed; no step is entered.
- Check before each activation and forward invocation, and after each successfully completed forward invocation, including the last.
- Pass the execution token to step work. Await the current invocation's real completion before starting error handling.
- After the final successful forward invocation and its cancellation checkpoint, commit the forward-success decision before starting disposal. Cancellation observed before this decision triggers unwind. Cancellation arriving after it does not initiate unwind; a disposal failure can still make the final overall status `Failed`. Publication of the terminal report remains after owned cleanup.
- Process a returned failure or unrelated thrown exception before a subsequent cancellation checkpoint; cancellation must not relabel an already-observed failure.
- Treat a thrown cancellation exception as caller cancellation only when it corresponds to this execution's requested cancellation. An unrelated cancellation exception is an execution fault.
- Do not introduce an implicit forward-execution timeout or detach an uncooperative step. Callers may supply a token with their own deadline.

### 8.2 Independent cleanup cancellation

Create a cleanup token independent of the caller's execution token. The selected v1 default is a **30-second cooperative budget for the whole unwind**, configured as `ErrorHandlingTimeout`. Snapshot it at build time through the existing options/configuration conventions. Permit an explicit infinite budget; reject zero/negative finite values. This is an implementation default selected to complete the contract, not a previously specified user value.

Start that budget when unwinding begins. Pass the same cleanup token to all error handlers. Its expiry requests cancellation; it does not force termination or authorize abandoning the handler.

Always invoke every eligible `Error` method once. If the cleanup budget has already expired, invoke the remaining handlers with the cancelled cleanup token and record their actual outcomes. Classify a handler cancellation exception as `Cancelled` only when it is attributable to the cleanup token's requested cancellation; an unrelated cancellation exception is an error-handler fault. Retain either secondary cause and continue unwinding. A handler that cancels is recorded as incomplete cleanup; a handler that finishes successfully remains successful.

Never run `Execute` and `Error` for the same step concurrently. Never dispose services while their method is still running. If a handler ignores cancellation, the pipeline remains active/unwinding until it settles. Normal asynchronous disposal is awaited; this budget does not create a forceful disposal timeout.

## 9. Automatic logging

The runner owns lifecycle logs; step authors need no logging wrapper or attribute. Use the existing CISLIB Logging facade, filters, correlation context and writer lifecycle.

| Event | Default level and contents |
| --- | --- |
| Pipeline accepted/started | Information; pipeline identity and configured step count. |
| Forward method starts | Debug; registration and phase. |
| Forward method succeeds | Information; outcome and duration. |
| Expected unsuccessful result | Warning; code, safe message, registration and duration. |
| Unexpected forward exception | Error; exception diagnostics and duration. |
| Error handler starts | Debug; registration and original failure identity. |
| Error handler completes | Information; outcome and duration. |
| Error handler fails | Error; secondary cause and duration. |
| Caller cancellation | Information; cancellation phase and registration when applicable. |
| Cleanup budget expiry/incomplete cleanup | Warning; distinguish it from caller cancellation. |
| Construction/disposal failure | Error; lifecycle phase and affected registration if known. |
| Pipeline terminal summary | Information for success/cancellation, Warning for expected failure, Error for unexpected/lifecycle/cleanup faults. Include final outcome, duration and secondary-failure count. |

Include stable pipeline name, execution ID, current correlation ID when present, registration position/name, phase, outcome, duration and error code where relevant. Preserve existing correlation naming; do not introduce a second incompatible header convention.

Emit one terminal summary, with references/counts instead of repeating every full exception. Start logs may be filtered at Debug; completed-step records still provide default per-step visibility. Log levels remain centrally configurable.

Do not automatically serialize the context, request body, credentials or application data. Step authors may add deliberate business-specific logging through existing facilities. Logging availability, filtering or ordinary recording failure must not alter workflow behaviour or prevent remaining handlers from being attempted.

## 10. Automatic metrics

Follow the current authoritative CISLIB Metrics contract and installed metrics skill. The v1 reference establishes CISLIB-owned instruments, monotonic timing, bounded state, shared provider ownership, centrally authoritative identity, and `/_metrics` export. Verify actual implemented APIs before coding.

The **pipeline runner** owns all instruments below. Reuse module-lifetime handles; no meter, instrument, callback registration or event-history queue is created per run. Preserve existing no-op behaviour when collection is disabled. Measure durations independently of export availability so the execution report remains accurate.

### 10.1 Catalog and boundaries

| Logical name | Kind / unit | Exported family or sample | Dimensions beyond central identity | Boundary and intended use |
| --- | --- | --- | --- | --- |
| `cis.pipeline.execution.duration` | Histogram/timer / seconds | `cis_pipeline_execution_duration_seconds_{bucket,sum,count}` | `pipeline`, `outcome` | One accepted `Execute`, from entry through preparation, forward work, unwind and owned cleanup. Overall count, outcome rates and latency. |
| `cis.pipeline.active.executions` | Up/down counter / count | `cis_pipeline_active_executions` | `pipeline` | Add +1 for an accepted invocation; -1 once in its final exit path. Includes preparation, unwind and owned cleanup. Current concurrency. |
| `cis.pipeline.step.duration` | Histogram/timer / seconds | `cis_pipeline_step_duration_seconds_{bucket,sum,count}` | `pipeline`, `step`, `phase`, `outcome` | One actual entered `Execute` or `Error` invocation, through its awaited completion. Counts, latency and failures by step/phase. |

Braced suffixes above describe three exported child families; they are not literal metric names. Verify actual translated names in one exposition check.

All samples, including generated histogram and overflow samples, retain centrally supplied `application` and `environment`. Reuse the existing environment contract; do not set those reserved labels at call sites.

`phase` is `execute` or `error`. `outcome` is `success`, `error` or `cancelled`. Reported business failure and unexpected exception both use `error`; detailed classification stays in logs/reports. Cleanup cancellation is `cancelled` on that handler observation, with its timeout cause retained in diagnostics. Use outcomes only where the runtime knows their origin.

### 10.2 Counting, duration and lifetime

Record one overall outcome after the final report's outcome is known. Never-started steps produce no duration sample. Construction/disposal faults affect the overall outcome without inventing a failed forward-step measurement. Rejected repeated/concurrent calls are not accepted invocations and create no second run observation.

A successful forward step later compensated still has a successful forward observation. Its error handler has a separate phase observation. Several cleanup failures do not create several overall pipeline failures.

Use the duration histograms' counts for completed-operation counts, with rates computed by the backend. Histograms retain counts and sums in addition to their distribution [S4]. Do not add duplicate completed counters or calculate rates in a background task.

Use monotonic elapsed time and seconds. Publish matching durations to the report and metrics; terminal recording occurs once. Counter/histogram state is cumulative for the existing provider lifetime, not reset at each build, run or scrape. Active work is balanced on every terminal path.

Use the current Metrics duration profile. The supplied v1 reference's finite boundaries, in seconds, are:

~~~text
0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25,
0.5, 0.75, 1, 2.5, 5, 7.5, 10
~~~

Applications with longer operations may select an appropriate fixed profile through the existing Metrics configuration. Preserve the configured bucket/stream limits; do not create a separate pipeline aggregation policy.

### 10.3 Names, cardinality and runtime isolation

Pipeline and step labels are bounded configured aliases. Distinguish repeated step registrations in reports/logs by position; metric observations may intentionally aggregate identical step aliases within a pipeline.

Exclude execution IDs, correlation IDs, user/customer IDs, URLs, context values, arbitrary error codes/messages and exception text from metric labels. Do not generate per-request instrument names.

Reserve the three instruments and required streams through the existing Metrics catalog. Calculate normal-workload capacity: for P named pipelines and R admitted pipeline/step-alias pairs, the declared domains allow up to 3P overall-duration combinations, P active combinations and 6R step-duration combinations. Allocate an adequate profile inside the existing total policy. Do not silently accept ordinary workload overflow or automatically increase limits.

Runtime recording failures/overflow follow existing Metrics diagnostics and no-op/overflow rules; they must not fail business work, change readiness or interrupt unwinding. No independent exporter, provider, retry queue or memory allocator is part of this module. The runner does not duplicate REST transport metrics emitted by work inside a step.

### 10.4 Query example

After confirming the exported names, completed pipelines per second by outcome can be queried as:

~~~promql
sum by (application, environment, pipeline, outcome) (
  rate(cis_pipeline_execution_duration_seconds_count[5m])
)
~~~

Use application/environment selectors in deployed dashboards. A step-failure query must filter `phase="execute"`; error-handler failure analysis uses `phase="error"`. Do not interpret missing or overflow-filtered telemetry as zero work.

## 11. Host integration and working example

Integrate builder/runtime factory registration and automatic instrumentation with the existing `CISApplication` startup composition. Keep registrations idempotent; use the already-owned provider and module services. Business dependencies remain normal application registrations.

If the startup facade is not yet implemented on the target branch, use the existing supported host extension and leave one documented integration point; do not expand this task into rebuilding Startup or invent a fictional compiling facade. Preserve readiness and shutdown ordering. Callers/host must await active executions before disposing their dependencies; do not add a background execution scheduler.

Extend the consolidated example application when available. Add one compact pipeline demonstration with:

- DI-injected application services and a typed context.
- `Add<Step>()`, `Build()`, and awaited `Execute()`.
- A success case and a controlled expected-failure case showing failed-step-first unwind.
- Report inspection, automatic logs and a matching metrics observation.
- A caller-scope example in the guide where an existing scoped dependency must be shared.

Use deterministic in-memory collaborators for failure demonstrations and tests. Do not require live external services or a new standalone application when the consolidated example can host the demonstration. Reuse current application examples and remove only duplicates introduced or superseded by this change.

Publish snippets extracted from, or checked against, compiling example code. Do not leave `TODO` bodies or proposed helper names in the final consumer guide/skill.

## 12. Repository deliverables

Map these proposed locations to the repository's current conventions once. Record the final mapping and fix every reference; do not reorganize unrelated modules.

| Deliverable | Proposed location / obligation |
| --- | --- |
| Supplied feature input | Preserve this file through the existing CIS feature-intake workflow. |
| Portable contract and metrics catalog | `contracts/workflows/pipeline/README.md`, with entries in the central metrics catalog. |
| C# implementation | Appropriate existing project, under `Workflows/Pipeline/`; public namespace `CIS.Workflows`. |
| Native verification | Existing affected C# test projects and integration fixtures. |
| Consumer guide | `docs/modules/workflows/pipeline.md`. |
| Working example | Extend the established consolidated example; document its real path. |
| Repository usage skill | `.github/skills/cislib-workflows/SKILL.md`. |
| Skill UI metadata | `.github/skills/cislib-workflows/agents/openai.yaml`, when supported by the repository's skill format. |
| Routing | One concise root `AGENTS.md` entry, outside managed CIS blocks. |

One discoverable `cislib-workflows` skill initially covers the implemented pipeline. Later workflow modules can extend it when specified and implemented. Do not generate multiple overlapping pipeline/workflow skills or copy this full specification into every instruction file.

## 13. Generate the repository skill files

These instructions create **repository-owned usage guidance during implementation**. They do not request installation of a personal ChatGPT skill. Commit repository skill changes with the module using the normal CISLIB workflow.

### 13.1 Generation sequence

1. Inspect the existing skill inventory and exact-name frontmatter. Reuse/update an existing `cislib-workflows` skill if present; preserve unrelated instructions.
2. Complete the public API and one compiling consumer example first. Read their actual names, registration path, lifetime rules and report types.
3. Use the repository's supported skill scaffold/initializer if one exists. Otherwise create the small files below directly in its recognized skill directory. Do not initialize a personal-skills checkout for a repository deliverable.
4. Write a concise `SKILL.md` with YAML `name` and `description`, direct links to canonical documentation and actionable instructions. Keep detailed semantics in the contract/guide.
5. Generate the metadata file from the finished skill where supported. Use the installed skill tooling's metadata generator, if available; inspect its current help before invoking it.
6. Add the root routing entry. Keep it outside any managed `cis:repository-guidance` region.
7. Validate frontmatter, discoverability, paths and supported metadata with the current repository/CIS skill tools. Inspect installed command help; do not assume historic CLI syntax is current.
8. Compile the referenced example. Check that a small consumption task can be completed using the skill and its linked example without this conversation.
9. Save the skill changes with the implementation. Report the real paths and validation results; do not claim a personal skill was installed.

No extra script, README, installation manual or copied specification is required inside the skill directory. Add a supporting resource only when it provides a concrete reusable capability unavailable in the canonical guide.

### 13.2 Starting content for SKILL.md

Adapt the links below to the final repository paths. This is a starting template; replace proposed details with the implemented contract before marking the module complete.

~~~~markdown
---
name: cislib-workflows
description: Implement, use, review and diagnose CISLIB workflows. Use for CIS.Workflows pipelines built with Add<TStep>(), Build() and Execute(), DI-constructed steps, reverse Error handling, execution reports, cancellation, and automatic logging or metrics. Cover only workflow facilities implemented in the current repository.
---

# CISLIB Workflows

Read the repository instructions and the actual public API before changing code.
Use the [pipeline contract](../../../contracts/workflows/pipeline/README.md)
and [consumer guide](../../../docs/modules/workflows/pipeline.md).
Find the compiling consolidated example linked by that guide.
Treat examples in old planning documents as proposals until verified against code.

## Build and run a pipeline

1. Obtain the pipeline builder through the established DI/host integration.
   Supply a stable pipeline name and a fresh typed invocation context.
2. Add concrete step types in the required order using Add<TStep>().
   Declare application dependencies through step constructors.
   Follow the implemented activation and scope-ownership contract.
3. Build once and await Execute(). Each built instance represents one run.
   Never call Execute concurrently or again on the same instance.
4. Return the defined unsuccessful step result for an expected business failure.
   Pass cancellation through asynchronous work.
5. Implement Error for partial as well as completed work: the failing step is
   unwound first, then earlier entered steps in reverse order.
   Use the provided cleanup token and the recorded identifiers/state.
6. Keep business compensation separate from disposal. Do not dispose DI-owned
   dependencies or assume the runtime restores context snapshots.
7. Inspect Status, Success, the original Error and the ordered Steps report.
   Check error-handler and lifecycle failures separately. The report remains
   available after exceptional or cancelled completion.
8. Keep a caller-owned scope alive until the awaited operation settles.
   Never assume a new scope shares request-scoped transaction instances.

## Preserve automatic instrumentation

Use the runtime's existing per-pipeline and per-phase instrumentation.
Add application metrics only for business facts it cannot infer.
Read the installed CISLIB Metrics skill before changing instrumentation.
Keep application/environment centrally authoritative and names bounded.
Put execution IDs, correlation IDs and detailed errors in logs/reports,
not metric labels. Preserve original outcomes when telemetry is disabled
or recording fails.

## Change and verify

Use the current CIS feature/task workflow and existing native tests.
Verify the changed ordering, unwind, DI ownership, report or cancellation
boundary with focused cases. Reuse existing observability fixtures.
Keep the compiling example, consumer guide and this skill synchronized.
Do not add retries, persistence, parallel stages or future workflow modules
unless separately requested.
~~~~

### 13.3 agents/openai.yaml

Use this metadata when supported, adjusting the text only to match the finished skill. Quote string values. The description is intentionally short; the default prompt explicitly names the skill.

~~~yaml
interface:
  display_name: "CISLIB Workflows"
  short_description: "Build and diagnose CISLIB execution pipelines"
  default_prompt: "Use $cislib-workflows to build and execute a CIS.Workflows pipeline with DI, reverse error handling and automatic telemetry."
~~~

Do not add invented tool dependencies, icons or branding. If the repository skill loader does not support this metadata, document that fact and retain the supported `SKILL.md` format.

### 13.4 Root AGENTS.md routing

Proposed entry, with final paths substituted:

~~~~markdown
## Workflows and pipeline execution

For CIS.Workflows pipeline implementation, application use, review or
diagnostics, read .github/skills/cislib-workflows/SKILL.md and follow its
links to the current contract, consumer guide and compiling example.

Preserve sequential Execute, failed-step-first reverse Error handling,
single-execution reports, DI ownership and cancellation semantics.
Reuse automatic Logging/Metrics; read the metrics skill before changing
their contracts. Keep examples and skill instructions aligned with the
implemented API. Extend this routing only when another workflow module
is actually implemented.
~~~~

### 13.5 Small skill-use check

Using only the generated skill and linked repository files, have a fresh consumer task define three DI-dependent steps, make the middle step return an expected failure, run the pipeline and inspect its report. Check the produced code through the existing example/test build. The guide must lead to the failing step's `Error`, then the first step's `Error`, with the last step unentered.

This is one focused usability check, not a new skill evaluation framework or an additional human approval gate.

## 14. Focused acceptance

Use existing native tests and fixtures. Each row is an acceptance obligation, not a mandate for a new project or one test per permutation.

| ID | Acceptance and evidence |
| --- | --- |
| A-01 | Ordered asynchronous success: a controlled await proves the next step cannot start early; all succeed and no Error runs. |
| A-02 | Expected failure at first/middle/last positions stops admission and unwinds the failing step plus earlier entered registrations in exact reverse order. |
| A-03 | Unexpected forward exception and invalid result trigger unwind, retain the origin and propagate according to section 6, including fault propagation for unrelated cancellation exceptions. |
| A-04 | Multiple failing/cancelled Error handlers do not prevent remaining handlers being invoked; primary and secondary causes stay separate. |
| A-05 | Same context and correct instance reach both phases; reports preserve separate statuses, registration identity and actual/absent durations. |
| A-06 | Build snapshots the definition, rejects an empty list, distinguishes duplicate types and isolates independent runs. |
| A-07 | Atomic single-use admission rejects a concurrent/second call without changing the accepted execution or double-counting telemetry. |
| A-08 | DI activates all steps before forward work. A later activation failure leaves all unentered, identifies construction and releases owned resources. |
| A-09 | Fresh instances per registration; dependencies respect lifetimes; owned and borrowed scope modes keep services valid through unwind and dispose only their owned objects once. |
| A-10 | Disposal failure is a lifecycle failure, preserves forward statuses and any earlier cause, and does not cause a second unwind. |
| A-11 | Pre-cancellation, between-step cancellation, current-step cancellation and unrelated cancellation exceptions follow section 8. Use controlled tasks; prove no concurrent forward/error execution. |
| A-12 | Caller cancellation does not pre-cancel cleanup. Budget expiry still invokes remaining handlers and reports actual outcomes; no handler is abandoned or disposed while running. Use a short test configuration/controllable clock rather than waiting for the production default. |
| A-13 | Automatic structured logs cover success, expected failure, exception, cancellation, construction, disposal and handler failure without serializing context by default. |
| A-14 | One overall observation and one per entered phase; no samples for unentered methods; active counts balance through unwind/cleanup; phase outcomes agree with reports. |
| A-15 | One targeted real exposition check verifies names, seconds, mandatory identity, bounded labels and reused handles. Existing disabled/failure/limit fixtures prove telemetry cannot change pipeline behaviour. |
| A-16 | CIS.Workflows public API, host integration, consolidated example, guide, skill and routing agree; example compiles and repository skill validation/use check passes. |

Use parameterized cases where they reduce duplication. Combine representative DI/report/telemetry checks where clear. Reuse the Metrics module's infrastructure; do not test the OpenTelemetry SDK's internal correctness again.

Do not add a custom orchestration harness, broad benchmark/mutation campaign, exhaustive arbitrary-step permutations or large evidence bundle. Broaden validation only for a concrete uncovered risk or an existing required gate. Keep the final evidence concise and reuse unchanged valid results.

## 15. CIS implementation workflow

This is a new feature in the existing CISLIB product. Preserve the supplied document as input; do not replace the product-wide BRD or recreate the registered workspace.

1. Inspect actual repository instructions, current branch/state and installed CIS help.
2. Import the specification using the installed feature-intake/BRD route and preserve the accepted decisions above.
3. Derive the implementation plan with stories/tasks no larger than medium, each tied to relevant acceptance rows.
4. Use current CIS work ownership, accounting and native test/result reuse. Do not fabricate task IDs, test results or execution authority.
5. Proceed under the user's existing authorization. Do not create separate approval gates for ordinary planning, implementation, documentation, skill generation, focused checks or metadata refresh.
6. If a real current gate blocks an action, finish safe preparation, identify the exact rule and blocked action, and ask only for the necessary concrete decision.

Use these coherent outcomes as the starting decomposition; split only where actual repository complexity requires it:

| Outcome | Ownership and dependency |
| --- | --- |
| 1. Contract and execution | Workflows API, builder, runner, reports, failure/cancellation semantics and focused native tests. One owner controls shared contracts. |
| 2. DI, hosting and instrumentation | Activation/scope adapter, host wiring, logging/metrics catalog and focused integration checks. Starts once shared contracts are stable. |
| 3. Consumer enablement | Consolidated example, guide, repository skill, metadata and routing. May proceed alongside outcome 2 after contract stability; completion requires the real integration/example build. |

Assign nonoverlapping files before parallel edits. One owner controls shared solution/package/host changes and build outputs. Keep tests, documentation, review and completion evidence with the outcome they validate; do not add standalone coordination/evidence tasks.

After integration, resolve material remaining defects and stop optional testing when acceptance and current gates are covered. Report the actual implemented API, changed files, focused native results, final skill/example paths and any remaining limitation. Do not claim deployment, release qualification or installed personal skills from completion of this module.

## 16. Implementation-agent prompt

> Implement the CISLIB Pipeline module from this specification as the first facility in the public CIS.Workflows namespace. Read the actual checkout's instructions, dependency policy, Logging/Metrics APIs, startup composition, examples and installed CIS workflow first.
>
> Preserve this document as feature input, import it through the supported CIS route, derive a bounded plan and execute eligible tasks in parallel with clear file ownership. User authorization persists; do not invent repeated approval gates.
>
> Deliver ordered Add<TStep>(), Build() and single-use Execute(), typed context, accurate reports, DI construction and ownership, and reverse Error handling including the failing step. Keep execution, construction, cancellation, handler and disposal failures distinct. Honour the specified outward exception behaviour and independent cooperative cleanup budget.
>
> Reuse existing Logging and Metrics automatically at pipeline and step-phase boundaries. Preserve bounded dimensions, central identity, shared provider ownership and disabled-telemetry behaviour.
>
> Generate or update the repository cislib-workflows skill, supported agents metadata and AGENTS routing from the actual implemented API and compiling consolidated example. Use section 13 as the starting content, correct every link, validate with current tools, and complete the small usage check. Do not install a personal skill or add speculative workflow capabilities.
>
> Use existing native test fixtures and the focused acceptance matrix. Keep implementation, tests and evidence proportionate. Finish with a concise report of the implemented contracts, focused results, example/skill paths and real remaining limitations.

## 17. Sources and definition of done

### Source basis

The accepted user decisions in this conversation govern product scope. The current CISLIB repository instructions and accepted module contracts govern integration. This document was prepared without inspecting a current CISLIB implementation checkout; proposed paths and composition helper names must be reconciled with that checkout before use.

The existing **CISLIB Metrics implementation specification v1.0 and agent guide**, dated 17 November 2026, informed the instrumentation rules. A newer explicitly accepted implementation/catalog takes precedence where it changes integration details; report a material semantic conflict rather than silently weakening the requirement.

Primary platform references support the framework behaviour used here:

- **S1 — DI activation:** [ActivatorUtilities.CreateInstance][S1].
- **S2 — DI ownership and disposal:** [Microsoft dependency-injection guidelines][S2].
- **S3 — Cooperative cancellation:** [Microsoft cancellation in managed threads][S3].
- **S4 — Histogram counts and distributions:** [Prometheus histograms and summaries][S4].
- **S5 — Native instrumentation:** [.NET creating metrics][S5].

The platform documentation may display newer package versions. Use the APIs available under the repository's .NET 10 dependency policy; documentation links are not authorization to upgrade packages or add a new exporter.

### Definition of done

The module is complete when WF-01 through WF-10 and A-01 through A-16 are satisfied within scope; the real public API is under CIS.Workflows; owned resources and error handling obey the defined lifecycle; automatic logs/metrics match the report; the consolidated example compiles; and the generated repository skill/routing lead a future agent to the working usage.

Supply the real native validation results and note any remaining issue. A specification, a copied skill template, or source inspection alone is not evidence of implemented behaviour.

[S1]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.activatorutilities.createinstance?view=net-10.0-pp
[S2]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines
[S3]: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
[S4]: https://prometheus.io/docs/practices/histograms/
[S5]: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation

