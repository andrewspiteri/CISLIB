---
title: Startup hosting and country-list example
type: document
status: draft
cis:
  stable_id: cislib.document.modules.startup.usage
---

# Startup hosting and country-list example

`CIS.Startup` runs explicitly enrolled root singletons once, sequentially, through
the actual Generic Host. It belongs to the existing `CIS.Core` assembly. See the
[core contract](core.md) for enrollment, identity, cancellation and failure rules.

Prepare Type Manager before building the real provider. Register that exact
prepared `ITypeManager`; it supplies a type catalog and does not discover or
enroll startup services. Configure Microsoft's ordinary provider validation on
the same builder that constructs the application:

```csharp
ITypeManager prepared = await TypeManager.CreateAsync(
    new TypeManagerOptions(typeof(Program).Assembly));
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddSingleton(prepared);

builder.Services.AddSingleton<CountryCatalog>();
builder.Services.AddSingleton<CountryReader>();
builder.Services.AddSingleton<IStartable>(sp => sp.GetRequiredService<CountryCatalog>());
builder.Services.AddSingleton<IStartable>(sp => sp.GetRequiredService<CountryReader>());
// Register module instances and all other dependent hosted services first.
builder.Services.AddCisHeartbeat();
builder.Services.AddCisStartup();
WebApplication app = builder.Build();
```

The complete [example composition](../../../implementations/csharp/examples/startup/CountryList/Program.cs)
includes the Healthcheck service, availability monitor and Heartbeat owner needed
by these sample services. The factory aliases enroll the same concrete
singletons. They also avoid the mixed supplied-instance validation limitation
documented in the [core contract](core.md). Neither validation option proves the
safety of arbitrary service factories; singleton services must not capture scoped
dependencies.

`AddCisStartup` is in `CIS.Startup.Hosting`. Register it after dependent hosted
services and immediately after `AddCisHeartbeat` when Heartbeat is installed.
Only the framework web-host service appended by `WebApplicationBuilder.Build`
may follow it. Concurrent hosted starting or stopping is unsupported and fails
startup before participant invocation. This position makes the adapter's stopping
barrier precede dependent lifecycle teardown; an application must still await
actual host stop before disposing its root or external dependencies.

The bridge awaits initialization in `IHostedLifecycleService.StartedAsync`, after
all ordinary hosted-service starts have completed and before `ApplicationStarted`.
Kestrel can therefore answer health requests while a required initializer remains
incomplete. Readiness-critical initialization belongs in `IStartable`; another
hosted service's lifecycle callback must not rely on unfinished CIS startup.

When registered, the real `HealthcheckService` and `HeartbeatRuntime` are connected
automatically. Successful initialization confirms Heartbeat activation and then
publishes startup completion. This acknowledges scheduler activation, without
waiting for its first callback or aggregate healthy readiness. An unhealthy
application remains NotReady under the existing Healthcheck policy. An initializer
must prime required data directly, rather than await a Heartbeat callback that is
still intentionally disabled during startup.

Install the existing reserved health branch before every business side effect:

```csharp
app.UseCisHealthcheck(health, healthOptions, _ => ValueTask.FromResult(false));
app.Use(async (context, next) =>
{
    if (!health.CaptureSnapshot().Ready)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }
    await next(context);
});
```

The real `/_health/ready` probe returns HTTP 500 while NotReady and HTTP 200 when
Ready. The separate business gate returns HTTP 503 and skips business work while
closed. The example denies health details and installs no pipe transport; use the
existing [Healthcheck guide](../healthcheck/README.md) for optional reporting and
authorization. Reporting reads do not execute startup or refresh work.

## Running the example

From `implementations/csharp`, with SDK 10.0.401 and the .NET/ASP.NET Core 10.0.12
shared frameworks installed:

```powershell
dotnet run --project examples/startup/CountryList/CountryList.csproj -c Release -- --urls http://127.0.0.1:5087
```

Visit `/countries` and `/_health/ready`. Ctrl+C performs normal host shutdown.
The executable imports the existing Type Manager build targets to generate its
real inventory. The supported build shape is an untrimmed, framework-dependent
Windows x64 or Linux x64 host; see the [inventory build guide](../../../implementations/csharp/build/type-manager/README.md).

[CountryCatalog](../../../implementations/csharp/examples/startup/CountryList/CountryCatalog.cs)
awaits its small local asynchronous source, publishes usable country data, and
registers a refresh with its retained root Heartbeat owner. The later
`CountryReader` checks that data and the shared prepared TypeManager. Constructors
only capture dependencies. Every 15 seconds the sample refreshes through the same
source; replace it with application I/O as needed. Healthcheck's existing
availability policy preserves previously published usable data across failed
refreshes. The root provider owns the monitor and Heartbeat owner.

```powershell
dotnet run --project examples/startup/CountryList/CountryList.csproj -c Release -- --self-test
```

This bounded demonstration binds a real loopback listener while holding required
priming. It observes readiness 500, business 503, zero business executions and a
pending host-start task. Releasing priming completes the ordered reader and opens
readiness/business responses at 200 with three countries and the same TypeManager.
The first Heartbeat refresh is held separately, proving readiness does not await
it; the demonstration then releases and observes that actual callback. Only this
mode uses a 1 ms refresh interval. It uses signals and bounded observation waits,
without sleep-based ordering.

One JSON line reports `example`, `before`, `after`, `refresh` and `stopped` after
successful cleanup. `after.refreshCompleted` is false; `refresh.completed` is true
and its count is at least one. `stopped: true` appears only after actual host stop,
startup-task settlement and asynchronous root disposal have succeeded. Even on
assertion/timeout failure, the application releases its holds and awaits real
shutdown before disposing dependencies. A failed stop barrier prevents destructive
root disposal; an observation timeout alone never proves work has ended.

The [CIS-0007 verification record](../../changes/CIS-0007/verification.md) describes
actual scoped native evidence and outstanding qualification. These usage
instructions do not claim package publication or full platform/release coverage.
