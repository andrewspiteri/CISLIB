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

When using [CIS.Startup](../startup/README.md), register `AddCisStartup()` immediately
after `AddCisHeartbeat()` among hosted services and keep concurrent host starting
and stopping disabled. Startup awaits its participants after the listener starts.
Its host adapter owns concrete Heartbeat activation and the original startup-token
cancellation cutoff; the coordinator has no Heartbeat dependency. The adapter's
confirmation runs inside the retained startup operation before readiness is
published. The final Startup lifecycle service closes readiness and joins startup/Heartbeat
drains before dependent stopping. The ordinary framework web-host service may
follow this cooperating pair. Standalone Heartbeat retains its final-service
ordering rule and `ApplicationStarted` activation behavior shown below.

[Configuration](../configuration/hosting.md) uses this same engine when the host
explicitly supplies a polling interval. It loads/reconciles directly before
dependent startup, and its subscriber returns the full refresh task. Configuration
owns provider-work cancellation because Heartbeat does not cancel callback tokens
at shutdown. `Startup.Hosting` closes that optional module before awaiting
Heartbeat and joins their actual work before dependency teardown.

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

Application services may depend on `IHeartbeatOwnerFactory` and retain an
`IHeartbeatOwner`, rather than depend on the concrete scheduler. The factory
registered by `AddCisHeartbeat` and `CISApplication` resolves the same
`HeartbeatRuntime`; each created owner forwards to that engine. The existing
concrete `CreateOwner(): HeartbeatOwner` API remains available. `CloseAsync`
and `DisposeAsync` retain the actual work barrier through either interface.

`CISApplication` offers its default aliases before the application callback, so
normal `Replace`, `RemoveAll` and explicit registrations remain meaningful.
Disabling a module removes its owned defaults, while explicit business-facing
replacements remain registered. A replacement owner factory does not replace
the hosted engine, its readiness or shutdown integration. `HeartbeatScope`
continues to protect an actual production DI scope with its concrete runtime.

Known caller-owned concrete instances retain instance aliases, preventing an
automatic factory alias from transferring disposal ownership to DI. In manual
hosting, register a borrowed `HeartbeatRuntime` before `AddCisHeartbeat`; do not
replace its concrete registration afterward while retaining an older factory
alias. Owned default aliases share the singleton and its idempotent cleanup.

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

Monitored registration uses the optional public
`CIS.Heartbeat.Healthcheck.IHeartbeatHealthcheckService` capability. The existing
`IHeartbeatService.Register(subscriber, options, monitoring, condition)` extension
dispatches to it; the original two-member `IHeartbeatService` contract is unchanged.
Services without that capability still support ordinary registration and reject
monitored registration clearly.

The production owner service retains atomic Core monitor binding, validation,
weak ownership and actual settlement. It requires suitable Core monitors or the
Core factory for typed policies. A public manual implementation, such as the
optional `CIS.CommonTesting` helper, can accept its recording monitors through
the same extension. This supported manual/recording pair is not a promise that
arbitrary foreign monitors bind into the production scheduler. Business factory
substitution does not replace the complete hosted module or its health policy.

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

## Frequent-callback diagnostics

`OnElapsedAsync`, `OnErrorAsync`, conditions and directly invoked helpers must be
safe at a 10 ms interval. Routine successes must not produce an Information,
Warning or Error stream. Keep optional per-execution detail at Debug/Trace and
avoid expensive formatting or payload capture while that level is disabled.

Report the first failure promptly, then bound repeated failure and recovery
summaries per registration using monotonic time and bounded suppressed counts.
Keep those budgets across recovery/failure flaps; do not key them by changing
messages, URLs or exception text, or use delay to throttle logs. Release reporting
state with its registration/owner. Sink-side `LogDuplicateSuppression` can provide
additional protection after capture, but does not remove producer work.

Expected shutdown cancellation produces no failure warning/error. Suppressed
diagnostics must never skip work, alter its result, omit health/metric facts,
delay cancellation or hide an unexpected fault from lifecycle accounting. A
business owner chooses the scope and measurable reporting budget; Heartbeat does
not globally suppress application logs.

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

See [CountryData](../../../implementations/csharp/examples/CountryData/README.md),
which primes directly and uses an hourly InitialDelay and Interval,
[all 70 acceptance mappings](../../../planning/heartbeat/acceptance.md) and
[actual verification record](../../changes/CIS-0006/verification.md).
Scoped native passes do not establish all-platform, coverage, mutation or release
qualification.

## Metrics

The optional Metrics integration observes started executions automatically. The
duration begins at the first actual condition/callback invocation and ends after
awaited error handling, cleanup and reporting. Active executions use that same
boundary; queued or fully suppressed unstarted work adds nothing. The existing
final result supplies the finite outcome label, with a separate `suppressed`
value for a started condition whose callback is gated off.

An admitted execution captures one binding, so later attachment cannot replay it
or create an unmatched active decrement. Recording failures do not become
subscriber errors or Heartbeat infrastructure failures. There are no subscriber,
registration or work-name labels. See the [runtime observation catalog](../../../contracts/metrics/runtime-services.md)
for exact names and outcomes; Metrics does not change scheduling or drain rules.

The host adapter also joins actual Metrics callback drain before dependent
lifecycle stopping, including application callbacks when automatic Core module
collection is disabled. This applies to the Heartbeat-only bridge and the
combined Startup bridge. An expired scrape/canceled host budget does not release
callback owners. Follow the [Metrics hosting order](../metrics/hosting.md) and
await real host stop before disposing protected dependencies.
