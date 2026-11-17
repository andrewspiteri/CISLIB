---
title: Metrics API example and package consumption
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.example
---

# Metrics API example

The runnable [MetricApi application](../../../implementations/csharp/examples/metrics/MetricApi/Program.cs)
prepares the real Type Manager before building one WebApplication. Its first
`IStartable` primes the order service; the second verifies that priming completed.
The optional integration automatically observes existing module state and native
ASP.NET request duration. The order service creates one reusable application
counter and timer through the public `IMetrics` facade.

Run from `implementations/csharp`, using SDK **10.0.401**, .NET **10.0.12** and the
matching ASP.NET shared framework:

```powershell
$env:CORE_SERVICE_NAME = "Metrics.Api"
$env:CORE_ENVIRONMENT = "local"
dotnet run --project examples/metrics/MetricApi/MetricApi.csproj -- --urls http://127.0.0.1:5058
```

In another terminal:

```text
curl -X POST http://127.0.0.1:5058/orders/example-42
curl -H "Accept: text/plain; version=0.0.4" http://127.0.0.1:5058/_metrics
```

The order ID belongs to the response and never becomes a metric dimension.
Native route labels use `/orders/{id}`. Health and Metrics branches precede the
business readiness gate; install the application's management-access middleware
before those branches. Native completed-request statistics exclude management
requests, while native active requests remain host-wide.

## Composition and application observations

The example uses the existing root and sequential startup:

```csharp
builder.Services.AddSingleton(prepared);
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<OrderService>());
builder.Services.AddCisStartup();
builder.Services.AddCisMetrics();

WebApplication app = builder.Build();
app.UseRouting();
app.UseCisMetrics();
app.UseCisHealthcheck(health, healthOptions, _ => ValueTask.FromResult(false));
// Add the application's readiness/authorization/business middleware here.
```

The full source constructs the order service through DI and uses two explicit
factory aliases. `AddCisMetrics` also supports installation before the existing
Heartbeat/Startup adapters; it preserves their required lifecycle order. The
provider becomes active before ordinary `IStartable` execution. Its options are
captured for the host lifetime; live configuration changes do not replace it.

Create reusable handles once, then record the actual application boundary:

```csharp
ICounter completed = metrics.CreateCounter(new("app.orders.completed",
    allowedDimensionKeys: ["outcome"]));
ITimer duration = metrics.CreateTimer(new("app.orders.duration", MetricUnit.Seconds,
    allowedDimensionKeys: ["operation", "outcome"]));

using ITimingScope elapsed = duration.Start(new(("operation", "submit")));
// Perform the application's order operation.
completed.Add(1, new(("outcome", "success")));
elapsed.Stop(new(("outcome", "success")));
```

In C#, use `using ITimer = CIS.Metrics.ITimer;` where implicit System.Threading
imports also expose a timer interface. The full source records `error` on its
exception path before rethrowing. Disposal cannot retry a completed timer or infer
success. Histogram count already counts operations, so the counter represents the
separately named completed business quantity.

With the environment above and one accepted order, the corresponding sample
families include:

```text
app_orders_completed_total{application="Metrics.Api",environment="local",outcome="success"} 1
app_orders_duration_seconds_count{application="Metrics.Api",environment="local",operation="submit",outcome="success"} 1
```

The timer also exports `app_orders_duration_seconds_bucket` and
`app_orders_duration_seconds_sum`. Native request count comes from
`http_server_request_duration_seconds_count`; the example adds no transport
counter. Label order in an actual scrape is not significant. Useful PromQL is:

```promql
sum(rate(app_orders_completed_total{application="Metrics.Api",outcome="success"}[5m]))

histogram_quantile(0.95,
  sum by (le) (rate(app_orders_duration_seconds_bucket{application="Metrics.Api"}[5m])))

sum(rate(http_server_request_duration_seconds_count{application="Metrics.Api",http_route="/orders/{id}"}[5m]))
```

## Startup observation and package use

Run `dotnet run --project examples/metrics/MetricApi/MetricApi.csproj -- --self-test`
for the bounded example observation. It holds the actual first initializer while
the real listener serves Metrics with 200, readiness with 500 and business with
503 without an order side effect. Releasing the initializer lets the sequential
reader complete and the next order return 200. The resulting JSON reports the
observed sample names, one business count/duration, identity, selected runtime
and loaded assembly locations. `stopped=true` is printed only after actual host
stop, retained startup settlement and root/health disposal. Finally cleanup
releases the example's hold before awaiting those barriers.

The source example uses project references. A packaged application that uses the
actual Type Manager inventory imports the existing versioned Core SDK, which
supplies its exact matching Core runtime PackageReference:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <Sdk Name="CIS.Core" Version="0.1.0-dev.1" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
    <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CIS.Metrics.OpenTelemetry" Version="[0.1.0-dev.1]" />
  </ItemGroup>
</Project>
```

Do not duplicate the Core PackageReference supplied by that SDK. This is a local
development package version, with no publication claim. The optional package's
locked closure selects OpenTelemetry **1.21.0**, Prometheus exporter
**1.21.0-beta.1** and DiagnosticSource **11.0.0**. DiagnosticSource is a deployed
NuGet assembly while the application runs on the selected .NET **10.0.12** shared
framework; it does not require a .NET 11 runtime. Core's own approved package
closure remains Logging/DI abstractions **10.0.12**.

The [business story](../../../implementations/csharp/tests/CIS.Core.BusinessCases/Metrics/MetricsApiStories.cs)
executes the actual built source example. The
[package consumer case](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics/MetricsPackageConsumerTests.cs)
copies those same source files into an isolated SDK/PackageReference application,
builds and folder-publishes it from a local feed, and checks actual Core/optional
payloads and DiagnosticSource binding. Its Unicode application identity exceeds
256 UTF-8 bytes while remaining within EnvironmentOptions' existing 255-scalar
bound; authoritative identity is separate from ordinary measurement values.
Actual native observations and remaining qualification are recorded in
[CIS-0009](../../changes/CIS-0009/verification.md).
