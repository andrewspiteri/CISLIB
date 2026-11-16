# CISLIB Startup and Dependency Injection Specification

**Revision:** 1.0  
**Date:** 16 November 2026  
**Status:** Implementation-agent handoff for the agreed startup behavior  
**Delivery order:** After Heartbeat  
**Target:** .NET 10, existing `CIS.Core` package and Microsoft dependency injection

## 1. Purpose, authority and scope

Provide a small application startup integration with exactly two stages:

**Stage 1 constructs every scheduled startup instance. Stage 2 awaits those instances' asynchronous startup methods, one at a time, in DI registration sequence. Only successful completion makes application readiness eligible.**

This document consolidates the user's startup decisions. It is not a claim that the module exists, tests have passed, or a release is qualified. The explicit implementation selections in section 9 resolve practical edge cases; they are not presented as separate historical user decisions.

Use the working branch's accepted Heartbeat implementation and current Type Manager, Healthcheck and Logging contracts. Repository references below were checked against `main` on 16 November 2026; that does not establish the state of another branch. Reconcile the actual implementation baseline before editing, without repeating already settled product questions.

**In scope:** `IStartable`, root-container registration/construction, ordered asynchronous initialization, existing Healthcheck readiness integration, the required Heartbeat lifecycle connection, and a runnable host example.

**Out of scope:** a replacement DI container, Configuration storage/mapping, a new scheduler, health system, watchdog, retry/compensation framework, general shutdown framework, or changes to unrelated modules. “DI configuration” means service registrations and lifetimes, not implementation of the future Configuration module.

## 2. Contract and enrollment — SU-01

The startup contract is:

```csharp
public interface IStartable
{
    Task StartAsync(CancellationToken cancellationToken);
}
```

A class is scheduled only when it implements this interface **and is registered in DI as `IStartable`**. Implementing the interface, or registering only the concrete class or another interface, does not enroll it. There is no assembly scan, attribute, priority, separate startup manifest, or per-service activation call.

Applications enable the CISLIB startup integration once at their composition root. After that, normal `IStartable` registrations are invoked automatically by that integration. Individual services do not call the coordinator or start themselves.

Use the existing compatible public interface identity if one already exists. Otherwise, place the new interface and coordinator under `CIS.Startup` within the existing `CIS.Core` assembly. Do not create a second incompatible `IStartable` contract.

### Registration examples

When the instance is exposed only for startup:

```csharp
services.AddSingleton<IStartable, FirstService>();
services.AddSingleton<IStartable, SecondService>();
```

When application code also consumes the same instance:

```csharp
services.AddSingleton<CountryListService>();
services.AddSingleton<ICountryListProvider>(
    sp => sp.GetRequiredService<CountryListService>());
services.AddSingleton<IStartable>(
    sp => sp.GetRequiredService<CountryListService>());

services.AddSingleton<IStartable, DependentService>();
```

These are registration examples, not claims that the example classes already exist. The `IStartable` registration positions determine startup order. Microsoft DI preserves registration order when resolving an `IEnumerable<T>`.[^di-registration]

Two independent type registrations must not be mistaken for aliases of one object. Use an explicit shared-instance mapping where identity must be shared. The supported enrollment mechanism is ordinary, unkeyed `IStartable` registration; a keyed-only registration does not enroll a service.

## 3. Stage 1: preparation, wiring and construction — SU-02

Stage 1 follows this sequence:

1. **Complete Type Manager preparation before constructing the container.** Use its existing asynchronous factory and register the completed instance. Do not turn Type Manager preparation into an `IStartable` operation or discover startables by scanning its catalogue.[^type-manager]
2. Complete the application's service registrations, including startup infrastructure, Healthcheck, Heartbeat and all `IStartable` entries.
3. Build the application's actual root provider once through its existing host/composition root.
4. Resolve and fully materialize the complete `IStartable` sequence from that provider, retaining those instances for Stage 2.

Type Manager preparation is the pre-container part of this first stage, not an additional service-startup stage. The host owns provider construction; a startup registration helper must not build a temporary provider. Microsoft explicitly warns against building an extra provider during service configuration.[^di-guidelines]

Building the provider alone is insufficient: singleton services are normally constructed on first resolution.[^di-lifetimes] Materializing the full startup sequence is therefore mandatory. An already supplied singleton instance is reused; its constructor is not invoked again.

**No `StartAsync` call may occur until every scheduled instance and its constructor dependencies have been resolved successfully.** A failure while constructing even the last scheduled service means zero startup methods are called.

This is not eager construction of the entire application. Unrelated services remain subject to their ordinary DI lifetimes and resolution behavior.

### Construction is not initialization

Constructors capture dependencies and establish local object state. They must not perform required asynchronous priming, start recurring callbacks, or invoke another service's startup method. DI factories follow the same rule.

Constructor order follows dependency resolution, not the startup registration sequence. If A depends on B, B may be constructed first even when A is registered first for startup. Stage 2 still starts A before B. If A needs B's initialized state, register B as `IStartable` before A; do not introduce automatic dependency sorting.

Likewise, a future configuration service can populate retained configuration objects during its startup, but another constructor must not assume those objects have already been populated. Required reads belong after the applicable initialization boundary.

## 4. Root lifetime and identity — SU-03

Startup participants use singleton registrations in the root container. Reject scoped or transient `IStartable` registrations as configuration errors before Stage 2; do not silently promote them, retain an artificial scope, or create replacement instances.

Singleton participants must not capture scoped dependencies from the root. A participant may create an explicit short-lived scope inside `StartAsync` for initialization work and dispose that scope after the work settles. Use standard DI scope validation rather than implementing a dependency-graph validator.[^di-lifetimes]

Deduplicate startup invocations by **object reference**, not type, name, overridden equality or hash code. Preserve the first occurrence's position: registrations resolving to `[A, B, A]` produce startup calls `[A, B]`. Two different objects of the same concrete type each receive their own invocation.

The application/DI owner retains disposal responsibility. The coordinator must not separately dispose the startable array or claim ownership of injected dependencies. Externally supplied instances retain their normal ownership rules.[^di-guidelines]

## 5. Stage 2: ordered asynchronous initialization — SU-04

After Stage 1 succeeds, process the retained sequence in registration order. For each unique instance, check startup cancellation/stopping, record that the instance is being invoked, call `StartAsync`, and await its actual completion before moving to the next instance.

Mark invocation **before entering user code**, including code that may throw synchronously before returning a task. At most once means at most one invocation, not one successful completion.

Do not use `Task.WhenAll`, parallel dispatch, `async void`, synchronous waits on tasks, or fire-and-forget initialization. An individual service may internally perform concurrent work, but its returned task must include all initialization required by subsequent services and readiness.

A service with ongoing work completes startup after its mandatory initialization and arrangement of that work. Its startup task must not represent the lifetime of an endless background loop.

### One operation per application lifetime — SU-05

Use one coordinator and one retained startup operation per root-container lifetime. Repeated or concurrent entry joins the same operation and observes its retained success, failure or cancellation. It must not resolve the collection again, construct replacements, rerun completed services or retry the failed service.

The operation is controlled by the host's startup/stopping cancellation, not by later callers replacing its token. There is no reset/restart API. A genuinely new application/container lifetime has a new startup operation and new invocation tracking; there is no process-global registry or persistent invocation database.

An empty scheduled sequence succeeds once Stage 1 and required infrastructure setup succeed. Healthcheck's other readiness conditions still apply.

### Failure and cancellation — SU-06

| Situation | Required result |
|---|---|
| Type Manager preparation or Stage 1 fails | Do not enter Stage 2; do not publish startup completion. |
| A startup method throws or returns a faulted task | Stop the sequence; do not call later instances; preserve the failed outcome. |
| Startup is cancelled before or during the sequence | Stop further invocations; keep readiness closed; preserve cancellation. |
| Shutdown wins before startup completion is published | Do not reopen readiness or enable recurring work. |
| A second startup request follows failure/cancellation | Observe the retained outcome; no retry or replacement instance. |

Pass the host startup cancellation token through to each participant. Check cancellation/stopping between invocations and at the final completion boundary, including when the last service ignores cancellation and returns successfully.

Cancellation is cooperative; requesting it does not prove the running method has stopped.[^cancellation] Observe its actual settlement before disposing dependencies it may still use. An unresponsive method leaves startup incomplete; the existing host/supervisor timeout policy owns escalation. Do not add task-abort machinery, implicit time limits, or an internal retry loop.

Report startup exceptions to the host and, when Healthcheck exists, through one application-owned critical condition monitor using its existing API. Establish that monitor during infrastructure setup, initially clear, and retain a reported failure while the failed application remains observable. Failures before health infrastructure exists are propagated and logged through the available bootstrap path; do not build another provider to report them. Expected shutdown cancellation is not automatically a new critical health fault. Preserve useful exception information for the caller; use existing bounded/sanitized diagnostics for health reporting.

## 6. Readiness and host integration — SU-07

Use the existing `HealthcheckService`, not another readiness engine. Its documented predicate is:[^health-usage]

```text
Ready = StartupCompleted && !IsStopping && HealthStatus == Healthy
```

Only the successful end of Stage 2 may call `MarkStartupCompleted()`. Individual startup services must not call it, and registering a marker service last is not a substitute for coordinator-owned completion.

| Application condition | Startup completed | Ready |
|---|---:|---:|
| Stage 1 or Stage 2 in progress | No | No |
| Construction/startup failed or cancelled | No | No |
| All startup work succeeded; aggregate health is Healthy | Yes | Yes |
| Startup succeeded; an active critical condition is Unhealthy | Yes | No |
| Startup succeeded; only noncritical conditions fail | Yes | Yes |
| Shutdown has begun | Retained fact | No |

A later critical failure can make readiness false without rerunning startup. Recovery may make readiness true again through normal Healthcheck behavior. A healthy aggregate before Stage 2 completes is insufficient.

### Probe availability is different from a successful probe — SU-08

Once reporting infrastructure can run, make the configured health transport reachable during potentially long Stage 2 initialization, returning **NotReady**. Do not promise a functioning endpoint before Type Manager/container construction has made that infrastructure possible.

Preserve the current transport contracts:[^health-transports]

| Probe | Before successful startup | After startup, with healthy aggregate |
|---|---:|---:|
| HTTP `/_health/ready` | 500 | 200 |
| Existing pipe probe, readiness view | Exit 1 | Exit 0 |

The earlier conversational HTTP 503 example does **not** change Healthcheck's established 200/500 mapping. Transport/protocol errors remain distinct from a valid NotReady response.

For HTTP, place the existing reserved health branch before the normal-request readiness gate and business middleware. Direct business requests must not execute application logic while startup is incomplete or stopping, even when a load balancer has not yet observed readiness. Use the application's existing request-gating convention; a business-request rejection status does not redefine the health-probe status.

Deliver a thin, explicit .NET host integration. Do not assume that putting a long initialization await into an arbitrary hosted-service `StartAsync` makes the HTTP server available: hosted-service ordering can delay server startup.[^hosting] The integration must demonstrate a reachable health probe while a startable is deliberately awaiting completion. It must not await `ApplicationStarted` from a lifecycle step that must itself complete before that event can occur.

The application's normal bootstrap enables this integration once. It coordinates the actual root provider, reporting listener, startup task and business gate; it does not invoke startables through a second host/DI path. Global host concurrency settings must not parallelize the internal `IStartable` sequence.

Do not add a new liveness classification, endpoint, or supervisor policy. Preserve Healthcheck's existing aggregate view and deployment-owned restart decisions.

## 7. Heartbeat and shutdown boundaries — SU-09

Preserve Heartbeat's application-owned lifecycle contract.[^heartbeat]

Construction and registrations made before startup completion remain inactive: no recurring condition or callback executes during either startup stage. A participant needing initial data must prime it directly during `StartAsync`; waiting for a blocked Heartbeat callback would create a startup dependency cycle.

At the successful completion boundary, the integration enables Heartbeat only after all required startup methods have completed. Required scheduler activation/bookkeeping must succeed before external readiness is published. Activation must not wait for a first successful callback and must not require a subscriber to call a Heartbeat start method.

This is completion of Stage 2, **not a third public startup stage**. Do not wait for Healthcheck's aggregate readiness to become true before enabling work that may report or restore health. Once activated, a runtime NotReady result does not by itself pause/restart Heartbeat.

On stopping, call the existing `BeginStopping()` hook, close startup/dispatch admission, request cancellation of active startup, and preserve Heartbeat's existing drain-before-dependency-disposal ordering. If stopping wins the race with final startup completion, neither completion nor activation may reopen the application.

If startup never completes, clean up inactive Heartbeat registrations without executing them. Active startup and Heartbeat work must settle before their dependencies are disposed. The host owns cleanup on partial startup and failure; no rollback transaction, reverse `IStoppable` sequence, or compensating startup operation is introduced here.

## 8. Implementation boundaries and diagnostics — SU-10

Use a direct implementation: the interface, a small coordinator, and the necessary DI/hosting integration. Reuse current contracts; do not create a general lifecycle framework or one adapter per hypothetical future host.

Keep source in the existing C# layout and `CIS.*` namespaces. Preserve the single `CIS.Core` assembly, pinned toolchain, approved Microsoft dependencies, `Microsoft.AspNetCore.App`, public compatibility and existing defaults. No third-party runtime dependency is authorized by this handoff.[^repo]

Use existing logging to record stage transitions, startup participant/order, completion/failure/cancellation and useful elapsed time. Do not dump configuration secrets, retain arbitrary object graphs, create per-service evidence archives, add dashboards, or introduce new environment options without a concrete requirement.

Do not automatically register every implementation discovered by Type Manager, build a dependency sorter, add runtime parallel startup, add hot registration/reload, or implement the future Configuration service as a dependency of this task.

## 9. Explicit implementation selections

These make the handoff executable without another product-question cycle. They are selections for this specification, not claims about previously implemented APIs.

| Selection | Baseline |
|---|---|
| Root startup lifetime | Singleton participants; reject scoped/transient enrollment instead of promoting it. |
| Repeated entry | Join one retained operation; preserve its terminal result. |
| Enrollment extent | Ordinary unkeyed `IStartable` registrations; no auto-discovery. |
| Empty startup sequence | Successful completion, still subject to infrastructure and health conditions. |
| New namespace when needed | `CIS.Startup`; reuse any already published compatible contract instead. |

Internal method names and the smallest host-adapter shape may follow repository conventions. Record material compatibility conflicts; do not silently change accepted public contracts or invent additional approval gates for ordinary implementation choices.

## 10. Acceptance criteria

Use native C# tests against the real implementation and Microsoft DI. These are behavior groups, **not one-task-per-row instructions**. Prefer controlled task-completion signals over timing sleeps; give asynchronous tests bounded test-runner timeouts.

| ID | Required evidence | Covers |
|---|---|---|
| AC-01 | Type Manager completes before root construction; preparation failure prevents startup. The registered manager is the prepared instance. | SU-02 |
| AC-02 | Explicit `IStartable` registration enrolls a service. Interface implementation or registration only under another type does not. An empty sequence completes correctly. | SU-01, SU-05 |
| AC-03 | All scheduled constructors finish before the first startup invocation. A late constructor/factory failure produces zero startup calls. | SU-02, SU-06 |
| AC-04 | Constructor dependency order may differ, but startup follows registration order. Holding A's asynchronous task prevents B from starting. | SU-02, SU-04 |
| AC-05 | `[A,B,A]` starts A and B once in first-occurrence order. Distinct instances of the same type, including equal-by-value objects, each start. | SU-03 |
| AC-06 | Normal interface resolution and startup use the same intended singleton/root. Non-singleton enrollment and invalid root-scoped capture fail before Stage 2. | SU-03 |
| AC-07 | Repeated and concurrent coordinator entry share one operation. Success, failure and cancellation cannot cause re-resolution or replay. | SU-05 |
| AC-08 | Synchronous throw, asynchronous fault and cancellation stop subsequent startup. Readiness stays false and failure is reported without automatic retry. | SU-06 |
| AC-09 | Pre-cancelled startup, cancellation between services, and cancellation before the final publication do not publish readiness. Active work is not mistaken for settled work. | SU-06 |
| AC-10 | Real Healthcheck snapshots remain NotReady throughout initialization; success marks completion once. Critical runtime failure/recovery changes readiness without startup replay; noncritical failure does not close readiness. | SU-07 |
| AC-11 | With a real HTTP listener and a deliberately blocked startable, `/_health/ready` returns 500 and business handling is gated. Successful release yields 200 when healthy. Health requests bypass the business gate. | SU-08 |
| AC-12 | The configured existing pipe transport/probe observes the same startup readiness transition, using its established 1/0 exit mapping. Reuse current transport fixtures. | SU-08 |
| AC-13 | No Heartbeat condition/callback executes before all startup methods complete. Activation is automatic afterward and does not wait for a first successful callback. Failure/cancellation before activation invokes no recurring work. | SU-09 |
| AC-14 | Stopping during initialization and at the final completion boundary cannot reopen readiness or dispatch. Existing Heartbeat drain and active startup settlement precede dependency disposal. | SU-06, SU-09 |
| AC-15 | A runnable consumer example primes a country-list-style cache directly, starts a dependent reader afterward, then permits recurring refresh and normal requests. It uses the actual modules, not duplicate example lifecycle implementations. | SU-01–SU-09 |

The end-to-end host test must prove actual listener ordering, not only an in-memory `Ready` Boolean. Reuse existing tests/fixtures for unchanged transport behavior rather than requalifying every Healthcheck case.

## 11. Instructions to the implementation agent

### Import and execute through CIS

Read the working checkout's `AGENTS.md`, native README commands, accepted decisions and CIS import instructions. Import this document as the supplied startup/DI requirements through the installed CIS workflow; do not leave it as an untracked prose plan.

Locate existing source/records first. Reuse or update a matching definition rather than creating duplicate product records. Resolve internal identifiers and actual CLI syntax from the installed CIS help/instructions; do not invent command names or copy an old completed task's execution authority.

Create the implementation plan, acceptance links, dependencies and file ownership through CIS. Use a legitimate work owner and the applicable approved finite allocation before governed execution. This document defines scope; it does not manufacture an approval, budget, or release authorization. Where an authority decision is required, consolidate it into the existing task authorization rather than repeated plan/design/test approvals.

### Suggested coherent work batches

| Batch | Owned outcome | Parallelization |
|---|---|---|
| 1 — Startup core | Contract, DI enrollment/lifetime handling, full construction barrier, ordered coordinator, failure/cancellation/once-only behavior, focused native tests and usage documentation. | Independent tests/review can proceed after interface and behavior boundaries are stable. |
| 2 — Application integration | Existing Type Manager/Healthcheck/Heartbeat connection, host/request gating, actual probe tests, shutdown boundary, runnable example and integration documentation. | Begin independent integration files after shared contracts stabilize; coordinate with the core owner. |

Use smaller tasks only if a batch exceeds the current CIS task-size rule. Do not create separate governance tasks for every acceptance row, test file, document or review; include these in the implementation outcome. Independent work may run in parallel, but the **runtime startup sequence remains sequential**.

Assign a single writer to each file and coordinate solution/project/output-directory changes and builds. No two agents should mutate the same project structure or run conflicting builds against shared outputs.

### Validation and progress

Use the existing MSTest projects and native .NET commands. Run the affected behavior selection during development and the required integration selection at closeout. Reuse valid CIS results for unchanged effective inputs; documentation, accounting or closeout edits alone must not trigger gratuitous native test reruns.

Do not build a Python harness, custom test framework, mutation campaign controller, or replacement evidence system. Broader release obligations remain governed separately; this feature's checks do not imply full release qualification.

Provide concise checkpoints when the core construction/order behavior works and when host integration works. Include implemented behavior, affected tests and results, remaining work, actual execution cost/artifact growth and any blocker. These are progress reports, not extra human approval gates.

If progress shifts from implementing startup into expanding CIS/tooling, tests repeatedly rerun without meaningful input changes, or evidence grows unexpectedly, stop that expansion, report the concrete cause and use the existing scoped correction route. Do not disguise a tooling issue as a new product requirement.

### Completion

Close out with the code change, runnable example, updated usage documentation, acceptance-to-test mapping and actual results. Identify any unverified environment or remaining gap explicitly. Preserve existing module behavior and unrelated work. Do not publish a package, merge the default branch, or claim release readiness under this handoff alone.

## 12. Sources and precedence

Current explicit startup decisions govern this module. Existing accepted contracts govern unchanged Type Manager, Healthcheck and Heartbeat behavior. This specification does not revive historical campaign plans or treat source-document aspirations as completed implementation.

[^repo]: CISLIB `AGENTS.md` and `README.md`, read from `main` on 16 November 2026. They establish the current package/runtime baseline, approved dependency policy, native MSTest workflow and CIS execution requirements. The implementation agent must read the actual working checkout, including `planning/decisions.md` and `.github/instructions/cis-import.instructions.md`.

[^type-manager]: CISLIB `planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md`, sections 1, 3 and 5: completed pre-container preparation, registration of the prepared instance, and no startup orchestration/type auto-registration by Type Manager.

[^health-usage]: CISLIB `docs/modules/healthcheck/usage.md`, read from `main` on 16 November 2026: `MarkStartupCompleted()`, `BeginStopping()` and the existing readiness predicate.

[^health-transports]: CISLIB `docs/modules/healthcheck/transports.md`, read from `main` on 16 November 2026: HTTP routes/statuses, health-branch ordering, actual listener availability and matching pipe-probe exit codes. Existing usage guides and accepted amendments take precedence over illustrative API names in older source definitions.

[^heartbeat]: `CIS_Heartbeat_Library_Specification_v0_2(1).md`, requirements HB-LIF-01–HB-LIF-07 and shutdown/ownership requirements. Use the accepted implementation and amendments in the working branch; do not reactivate the source attachment's historical task plan.

[^di-registration]: Microsoft, “Service registration (dependency injection)”, consulted 16 November 2026: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration

[^di-lifetimes]: Microsoft, “Service lifetimes”, consulted 16 November 2026: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes

[^di-guidelines]: Microsoft, “Dependency injection guidelines”, consulted 16 November 2026: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines

[^hosting]: Microsoft, “Background tasks with hosted services in ASP.NET Core”, consulted 16 November 2026: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0

[^cancellation]: Microsoft, “Cancellation in managed threads”, consulted 16 November 2026: https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads
