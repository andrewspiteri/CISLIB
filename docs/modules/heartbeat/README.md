---
title: Heartbeat
type: document
status: draft
cis:
  stable_id: cislib.document.modules.heartbeat.readme
---

# Heartbeat

`CIS.Heartbeat` runs recurring asynchronous work in the existing `CIS.Core`
package. The subscriber instance is the handle. Construction and registration
before readiness are inactive; the hosting adapter enables work at
`ApplicationStarted`. Registration while running can dispatch promptly, so the
subscriber must be fully initialized before it is registered.

## Actual owners and hosting

```csharp
builder.Services.AddScoped<MyDependency>();
builder.Services.AddScoped<MyPoller>();
builder.Services.AddCisHeartbeat(new HeartbeatOptions { MaxConcurrent = 4 }); // last
IHost host = builder.Build();
HeartbeatRuntime? runtime = null;
HeartbeatScope? scope = null;
try
{
    runtime = host.Services.GetRequiredService<HeartbeatRuntime>();
    scope = new HeartbeatScope(
        runtime, host.Services.GetRequiredService<IServiceScopeFactory>());
    MyPoller poller = scope.Services.GetRequiredService<MyPoller>();
    scope.Service.Register(poller, new HeartbeatRegistrationOptions
    {
        Interval = TimeSpan.FromSeconds(1), Name = "Prices"
    }, poller.CanPollAsync);

    await host.StartAsync();
    // Application work; subscribers do not start Heartbeat themselves.
}
finally
{
    // Once constructed, the engine must prove its global barrier before the
    // cleanup block. An unsuccessful barrier leaves dependencies usable.
    if (runtime is not null) { await runtime.StopAsync(); }
    try { await host.StopAsync(CancellationToken.None); }
    finally
    {
        try
        {
            if (scope is not null) { await scope.DisposeAsync(); }
        }
        finally
        {
            if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
            else { host.Dispose(); }
        }
    }
}
```

Use `CIS.Heartbeat.Hosting`. A subscriber implements
`Task<bool> OnElapsedAsync(CancellationToken)` and `Task OnErrorAsync(Exception)`.
Its optional condition is `Func<CancellationToken, Task<bool>>`. Returned tasks
include every child operation and cleanup using protected dependencies; no
`async void` or detached scope-using work belongs to this contract.

Resolve subscribers and dependencies from `HeartbeatScope.Services` and register
through that scope's `Service`. The facade forwards to one application engine.
The wrapper closes/drains its owner before destructive DI scope disposal.
Plain DI scope disposal cannot supply this ordering; do not associate an object
from another scope with this facade. No replacement subscriber or unrelated
per-invocation scope is created.

For manually owned objects, retain `runtime.CreateOwner()` and use its `Service`.
Await owner `CloseAsync()` before disposing protected objects/dependencies.
All users of shared dependencies must honor the enclosing owner's barrier.
For a shorter-lived object within a protected scope, await its `UnregisterAsync`
before disposing it. Registration never transfers subscriber disposal to Heartbeat.

## Schedule and configuration

| Setting | Contract |
| --- | --- |
| `Interval` | Default 10 ms; positive and at most 4,294,967,294 ms (about 49.7 days). |
| `InitialDelay` | Default zero, within the same bound; origin is readiness for earlier registrations, otherwise first successful registration. |
| `Name` | Optional, nonunique, at most 128 valid Unicode scalar values. Object reference equality determines identity. |
| `MaxConcurrent` | 1–16 unfinished dispatched executions across the application, including awaits, handling and owned cleanup/reporting. |

The runtime resolves concurrency once: nonblank `CIS_HEARTBEAT_MAX_CONCURRENT`,
then explicit `HeartbeatOptions`, then the completion-port maximum returned by
`ThreadPool.GetMaxThreads`, clamped to 1–16. Blank input is absent; malformed or
out-of-range selected input raises `CoreConfigurationException` with
`Heartbeat.InvalidValue` or `Heartbeat.OutOfRange`. Parsing is invariant.
`EnvironmentOptions.Heartbeat` provides mutable setup; the runtime copies effective
settings. There is no `Current`, global initializer, reset or live resizing.

The next eligibility is full settlement plus the interval. The same instance
cannot overlap through queueing, condition, callback, error handling, cleanup or
owner-dependent reporting. Missed ticks do not accumulate. FIFO pending work reuses
individual slots. Monotonic scheduling makes no operating-system latency promise.

Valid duplicates, including individually draining registrations, succeed without
changing schedule, condition, owner or monitor; arguments still validate. To change
settings, await `UnregisterAsync(poller)` and register again. A canceled wait does
not complete unregistration or make early reuse valid.

Idle registrations use a weak-key graph, including capturing conditions and owner
associations. Keep the subscriber and actual owner alive if recurrence is wanted.
Accepted work acquires a strong target and cooperative owner hold before queueing.
Collected targets, ended owners and escaping subscriber disposal exceptions retire
safely. A managed reference cannot prevent independent destructive disposal.

## Results and monitoring

Callback true means success; callback false is a negative result without calling
`OnErrorAsync`. A false condition skips both callback and handler. Unexpected
escaping condition/callback exceptions invoke the usable subscriber's handler once
and await it. Handler success preserves the originating failure; handler faults
are secondary and nonrecursive. Known acknowledged cancellation differs from an
unexplained `OperationCanceledException`. The initial production execution token
is uncanceled; shutdown/wait tokens never automatically cancel running work.

Use `CIS.Heartbeat.Healthcheck` and `HeartbeatHealthcheckOptions` to select exactly
one existing suitable monitor, `ConsecutiveFailures` policy or `Availability`
policy. Typed policies need the actual `MonitorFactory`; availability also needs
`SuccessPublishesUsableData = true`. Healthcheck owns thresholds, counters, health
state and snapshots. Heartbeat reports dispatch, actual phases/results, neutral
suppression and separate settlement. It adds no status/history or watchdog API.
Existing external monitors remain caller-owned; binding-created monitors retire
with the registration. The audited Core binding remains available for retirement
without rooting the subscriber or owner. Infrastructure/reporting faults surface
through the runtime's bounded detached `InfrastructureFailure` awaitable.

## Shutdown and verification

Ordinary unregister/owner closure drains accepted queued/running work. Global
shutdown also suppresses unstarted work and separately gates a callback after a
running condition. Running work, handling, cleanup and posted wrappers must
actually end. Stop/dispose share one terminal drain; the engine cannot restart.

Install the adapter after dependent hosted services and use nonconcurrent stopping.
Required initialization ends before `ApplicationStarted`; stopping event handlers
must retain callback dependencies until the barrier in either shutdown ordering.
Always prove the Heartbeat barrier before root disposal. If that wait fails or is
canceled, keep protected dependencies usable; an enclosing cleanup `finally`
must not dispose them merely because the wait ended. Once the global barrier
succeeds, later host/scope cleanup errors may still run the remaining cleanup.
A canceled host budget cannot authorize teardown of live dependencies: the adapter
awaits the real barrier. Individual canceled waits affect that caller only. A stuck
task can keep graceful shutdown incomplete. Executions must not await their own
drain or form capacity-dependent wait cycles.

Business deadlines, originating request timestamps, expiry, retries and rollback
remain application policy; scheduling does not reset them.

See the [Polling example](../../../implementations/csharp/examples/heartbeat/Polling/README.md),
[all 70 acceptance mappings](../../../planning/heartbeat/acceptance.md) and
[actual verification record](../../changes/CIS-0006/verification.md).
Scoped native passes do not establish all-platform, coverage, mutation or release
qualification.
