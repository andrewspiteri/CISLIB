---
title: Configuration preparation and hosting
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.hosting
---

# Configuration preparation and hosting

`CIS.Configuration` supplies retained concrete POCOs in the existing `CIS.Core`
package. Prepare initial values before constructing consumers, then activate
refresh through sequential Startup. The host supplies its bootstrap-capable
`IConfigurationStore`, fixed host-project name, polling interval when needed and
health criticality. No production backend or default interval is selected.

## Prepare before Build

The application supplies `store`, `refreshInterval` and `critical` below. Use
`CIS.Configuration` and `CIS.Configuration.Hosting` alongside existing namespaces:

```csharp
ITypeManager manager = await TypeManager.CreateAsync(
    new TypeManagerOptions(typeof(Program).Assembly));
await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(
    manager, new ConfigurationOptions(typeof(Program).Assembly.GetName().Name!, store)
    {
        PollingInterval = refreshInterval,
        IsHealthCritical = critical
    });
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddSingleton(manager);
builder.Services.AddCisConfiguration(configuration);
// Register concrete consumers, singleton IStartable aliases and dependent hosted services.
builder.Services.AddCisHeartbeat(); // Required when an interval is supplied.
builder.Services.AddCisStartup();
WebApplication app = builder.Build();
```

Preparation queries the completed Type Manager, captures root/default data and
loads/validates both scopes. Failure returns no usable runtime. DI resolutions
perform no storage I/O and return the same concrete root across scopes.
Constructors receive initially loaded values; use `IConfigurationReader` for
consistent multi-property reads during updates.

Install Configuration before every other unkeyed `IStartable`. Existing concrete,
reader or runtime registrations, including keyed conflicts, fail before additions.
Use one prepared runtime, registration collection and root Build per host; separate
hosts prepare separate roots. POCO instance descriptors preserve user disposal
ownership. The runtime factory gives DI ownership of shared async disposal once
resolved. Keep the caller's `await using` scope too: Build/validation may fail
before that factory is resolved. Repeated disposal joins the same barrier.

## Activate and observe

The first Configuration Startup participant establishes observation and directly
reconciles the bootstrap gap before dependent start methods. It never waits for
an inactive Heartbeat callback or creates a periodic timer.

| Provider mode | Explicit host choice |
| --- | --- |
| `None` | Positive interval and real Heartbeat; activation reconciles directly, later polling awaits actual refresh settlement. |
| `Reliable` | Adapter guarantees invalidation/reconnect signals. Without an interval Heartbeat is optional; an interval explicitly adds reconciliation. |
| `RequiresReconciliation` | Watch plus positive interval and Heartbeat recover missed signals. |

First polling delay and interval both use the supplied interval. The runtime
strongly retains its actual Heartbeat owner/subscriber. The adapter owns physical
keys, transport, reconnect guarantees and storage consistency; scope reads are
independent observations, not a distributed transaction.

When Healthcheck is connected, explicitly set `IsHealthCritical`. Missing policy
or polling without Heartbeat fails before watch/monitor acquisition. The existing
condition monitor is flagged until activation succeeds, flags a failed latest
reconciliation and clears on recovery. Accepted old values remain available after
ordinary failure. Noncritical policy preserves the existing aggregate-readiness
rules; the coordinator separately owns Startup completion. Subscriber faults have
a separate diagnostic and do not invalidate accepted data.

`CaptureStatus()` copies current per-type validity/disabled state, revision,
timestamps and safe failure codes, plus observation/activation/stopping state.
It reads no provider and exposes no values, tokens, payloads or checksums.
`CaptureDiagnostics()` copies at most 32 recent bounded name-only warnings using
the existing `ConfigurationIssue` contract. Bootstrap warnings survive until
logging can connect. Diagnostic failure cannot change values or bypass settlement.

## Stop before dependency teardown

The optional concrete connection belongs to `Startup.Hosting`. Original
startup-token cancellation and application stopping close Configuration admission
and cancel owned provider work before Heartbeat cutoff/drain, including while a
later initializer ignores cancellation. The coordinator remains independent of
Configuration and Heartbeat.

`StopAsync()` has no caller-wait token: it joins activation/registration in flight,
watch acquisition/disposal, provider work, synchronous notifications, cancellation
callbacks and the actual polling owner. Hosting joins Configuration, Startup and
Heartbeat before dependent lifecycle stopping. A canceled host budget cannot
release dependencies early; cleanup errors surface after owned work settles.

The store and POCOs are not disposed or reset by Configuration. Providers and
handlers must complete cooperatively; do not wait for a barrier containing your
own callback. Await actual host stop before root/external dependency disposal.
See [retained reads/events](core.md), [mapping](mapping.md), [refresh](refresh.md),
the [runnable example](example.md) and the [actual verification record](../../changes/CIS-0008/verification.md).
