---
title: Metrics hosting and callback ownership
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.hosting
---

# Metrics hosting and callback ownership

The optional `CIS.Metrics.OpenTelemetry` integration uses the application's real
host and one provider. Register callback-owner hosted services first, Metrics
next, then the existing Heartbeat/Startup bridges when used:

```csharp
using CIS.Metrics.OpenTelemetry;
using CIS.Heartbeat.Hosting;
using CIS.Startup.Hosting;

builder.Services.AddHostedService<CallbackOwner>();
builder.Services.AddCisMetrics();
builder.Services.AddCisHeartbeat();
builder.Services.AddCisStartup();
```

Keep normal DI validation enabled. Disable concurrent hosted stopping; Startup
also requires sequential hosted starting. Metrics checks final ordinary
`IHostedService` descriptors before activation, without resolving another hosted
service graph. Without a Core bridge, Metrics must follow all ordinary hosted
callback owners, with only the framework's exact web-host tail allowed afterward.
Keyed-only hosted registrations do not enroll with Generic Host.

With Heartbeat/Startup, the existing final-tail guards and their callback barrier
also protect owners registered between Metrics and the bridge. A late
`AddCisMetrics` moves only its newly added exact hosted descriptors before the
preexisting bridge; it does not reorder an application's other services or make
an unsupported foreign tail valid. Prefer the explicit order above.

Metrics activates during `StartingAsync`. Startup attaches its coordinator's
complete automatic binding before its real `StartedAsync` initialization task.
The listener can therefore answer a scrape while initialization holds business
readiness closed. Install the reserved branch after routing and before health,
readiness and business middleware:

```csharp
app.UseRouting();
app.UseCisMetrics();
app.UseCisHealthcheck(health, healthOptions);
// Existing business readiness/access policy follows these management branches.
```

The host/deployment still owns management access policy. Registering the provider
alone does not install or establish the HTTP policy; see the current endpoint
guide and [change verification](../../changes/CIS-0009/verification.md) for actual
integrated evidence.

## CISApplication composition

When the optional package is present in the prepared Type Manager catalog,
`CIS.Hosting.CISApplication` discovers its `MetricsApplicationIntegration` once.
The registrar uses the same `AddCisMetrics` installer and `UseCisMetrics` branch;
it creates no provider during construction or service registration. The facade
installs the operational branch after routing and before health/business
readiness, including while a sequential initializer is still running.

```csharp
using CIS.Hosting;
using CIS.Metrics.OpenTelemetry;

await using CISApplication application = CISApplication.CreateWeb(args, builder =>
{
    builder.ConfigureCisMetrics(options =>
    {
        options.ScrapeCacheDuration = TimeSpan.Zero;
        // Other existing MetricsOptions remain a restart-only profile.
    });
    builder.Services.AddSingleton<OrderService>();
}, web => web.MapGet("/orders", (OrderService orders) => orders.Read()));
return await application.RunAsync();
```

`ConfigureCisMetrics` copies its options immediately and retains no callback.
Equivalent repeated setup is idempotent; conflicting captured profiles fail.
An explicit existing `AddCisMetrics` profile is preserved rather than compared
against defaults. An explicit facade replacement without this package's profile
is also preserved, and receives no collector branch. Supplying both a collector
profile and a replacement facade retains the installer's existing conflict
checks.

`Enabled=false` uses Core's shared inert `IMetrics` without an exporter/provider
or reserved branch. `EnableOptionalIntegrations=false` disables automatic package
installation; it does not remove registrations that the application explicitly
installed. With no installed registrar in the prepared catalog, the facade keeps
Core's inert fallback. Core retains no dependency on the optional package.
Automatic integration uses prepared catalog membership, without another scan,
temporary provider or a second host.

Await the facade's real stop and asynchronous disposal before releasing external
callback owners. The focused source cases and their executed status belong to
[CIS-0011 verification](../../changes/CIS-0011/verification.md); this section adds
no platform or release qualification claim.

## Actual stop barrier

Application stopping closes collection admission. Startup joins actual Metrics
callbacks alongside its retained initialization, Configuration and Heartbeat
work before dependency `StoppingAsync` runs. The Heartbeat-only bridge also joins
Metrics callback drain. These barriers use the ordinary enabled `IMetrics`
binding even when `CollectCoreModules=false`: disabling automatic service
observations does not make application observable owners disposable early.

A callback remains active through execution and observation-list materialization.
An HTTP timeout or canceled host stop budget cannot prove it completed. Keep all
callback owners usable until awaited host stop succeeds, then dispose the actual
root asynchronously when supported. External owners remain caller-owned; an
enclosing failure cleanup must not dispose them merely because a wait failed.
A callback that never returns can keep graceful shutdown incomplete.

Automatic module attachment uses a separate internal alias available only when
enabled module collection is selected. The actual DI instances, borrowed logger
runtime and immutable Type Manager report are attached after provider activation;
the Configuration forwarder observes its existing prepared refresh owner.
For a supplied application-created Healthcheck source, the health pipeline's
one-time factory calls `CIS.Metrics.Hosting.MetricsHostAttachment` after provider
activation and before listener startup. The optional-alias lookup belongs to
Core Metrics hosting; the Healthcheck transport remains DI-free and preserves
external source ownership. No module constructs a second provider, repeats
bootstrap or changes readiness.

See the [Core API](core.md), [collection policy](collection.md) and
[runtime observation boundaries](../../../contracts/metrics/runtime-services.md).
Authored integration cases are not execution evidence; retained native results
and remaining platform/release obligations belong to the change verification.
