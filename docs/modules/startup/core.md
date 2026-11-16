---
title: Sequential application startup
type: document
status: draft
cis:
  stable_id: cislib.document.modules.startup.core
---

# Sequential application startup

`CIS.Startup.IStartable` declares `Task StartAsync(CancellationToken)`. Enroll only
ordinary unkeyed singleton registrations of that interface in the actual root
container. Implementing the interface or registering only another service type
does not enroll an object. No assembly scan or dependency sorting occurs.

```csharp
services.AddSingleton<CountryListService>();
services.AddSingleton<ICountryListProvider>(sp => sp.GetRequiredService<CountryListService>());
services.AddSingleton<IStartable>(sp => sp.GetRequiredService<CountryListService>());
services.AddSingleton<IStartable, DependentReader>();
```

The aliases explicitly share the intended singleton. Two independent type
registrations need not resolve to the same object. Keyed-only registrations do
not enroll a participant. Scoped or transient `IStartable` enrollment raises
`InvalidOperationException` before startup participant resolution. Configure
standard Microsoft DI `ValidateScopes` and `ValidateOnBuild` on the application's
real provider; the startup integration creates no temporary provider or custom
dependency validator. Microsoft DI validation has its own limits: in the pinned
.NET 10 runtime, an externally supplied `IStartable` instance followed by a
constructor registration can share a validation cache key and hide a scoped
capture, even with both validation options enabled. Prefer the factory aliases
shown above for container-created startup services and never capture a scoped
dependency in a singleton. CIS does not rewrite instance registrations, change
their disposal ownership, or claim to detect every hidden dependency graph. See
the [actual observed failure and source assessment](../../changes/CIS-0007/verification.md#standard-di-validation-boundary).

Prepare Type Manager with its existing asynchronous factory before constructing
the root provider, then register that prepared instance. This is bootstrap work,
not an `IStartable` or a source of automatically enrolled implementations.
Constructors and DI factories capture dependencies and local state. Required
asynchronous priming belongs in `StartAsync`; another constructor must not assume
that a dependency has already been initialized.

The coordinator first fully materializes every scheduled instance from the
actual root. If the last constructor fails, no startup method runs. It then
deduplicates by object reference, preserving first occurrence: `[A,B,A]` starts
`[A,B]`, while distinct equal-by-value instances each start. Constructor dependency
order can differ; startup order is the `IStartable` registration sequence.

One retained operation is published before resolution or user startup code.
Concurrent/repeated entry observes that same task, retaining success, failure or
cancellation without re-resolution, replay or retry. Each invocation is recorded
before entering user code and its actual returned task is awaited before the
next participant. Synchronous throws, null tasks, asynchronous faults and
unexplained cancellation exceptions are startup failures.

Participants receive one linked operation token derived from the first host
startup/stopping tokens and coordinator stopping. Later callers cannot replace
those tokens. Known requested cancellation is a neutral canceled outcome and
prevents further invocation or readiness publication. Canceling does not prove a
running task has ended; shutdown waits for its actual settlement. Do not await
startup/shutdown barriers from work whose own completion those barriers include.

When Healthcheck is installed, infrastructure creates one initially clear
critical startup condition before participant construction. A real failure flags
it using bounded diagnostics while preserving the original exception for the
startup caller. Logging records stage, participant position/type, outcome and
elapsed time, without participant values or raw exception messages. Logging or
health-reporting failure cannot replace the originating exception.

Only successful sequential initialization may confirm Heartbeat activation and
call `HealthcheckService.MarkStartupCompleted()`. Activation bookkeeping must
succeed; it does not await a first callback or aggregate healthy readiness.
Stopping competes with this final publication, closes health/dispatch admission,
requests cancellation outside the control lock and joins actual startup and
Heartbeat drains before dependency disposal. `StopAsync` observes the retained
startup fault/cancellation without replay; an unconfirmed Heartbeat drain still
fails shutdown. The coordinator never disposes startup participants or their
dependencies. Its own monitor is released only after the drain during container
disposal, so an observable failed application keeps its startup diagnostic.

The host bridge and runnable example supply automatic invocation and actual
probe/request ordering. This document describes the core contract, not a claim
that native tests or release qualification have completed.
