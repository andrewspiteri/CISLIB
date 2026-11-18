---
title: Startup hosting and CountryData
type: document
status: draft
cis:
  stable_id: cislib.document.modules.startup.usage
---

# Startup hosting and CountryData

CIS.Startup runs explicitly enrolled root singletons once, sequentially, through
the actual Generic Host in CIS.Core. See the [core contract](core.md) for identity,
enrollment, cancellation and failure. The canonical
[CountryData](../../../implementations/csharp/examples/CountryData/README.md)
uses [CISApplication](../application/core.md) for the existing module composition.

Application enrollment stays ordinary DI:

```csharp
builder.Services.AddSingleton<CountryCatalog>();
builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<CountryCatalog>());
```

CountryCatalog is the real app service; its complete
[Program](../../../implementations/csharp/examples/CountryData/Program.cs) also
selects the keyed REST profile and explicit retry policy it requires. The facade
prepares Type Manager and Configuration, builds one validated provider, installs
native routes and starts the host. Configuration activation precedes application
participants. All enrolled roots are constructed before sequential StartAsync
calls; reference aliases start once. Discovery itself does not enroll services.

Manual composition still uses AddCisStartup in CIS.Startup.Hosting. Register
dependent hosted services first and put Startup immediately after AddCisHeartbeat
when installed. Only the known framework web-host service may follow the final
lifecycle bridge. Concurrent hosted starting/stopping is unsupported. ValidateScopes
and ValidateOnBuild apply to the actual provider; arbitrary factories must still
respect singleton/scoped ownership.

Initialization is awaited in IHostedLifecycleService.StartedAsync, after ordinary
hosted-service starts and before ApplicationStarted. Kestrel can answer health
requests during a held initializer. Other lifecycle callbacks must not depend on
unfinished CIS startup. Load required data directly in IStartable rather than
awaiting a Heartbeat callback still gated by startup.

Hosting owns concrete Heartbeat activation through the retained completion
callback, then checks stopping/cancellation before publishing Healthcheck startup
completion. Failure stays observable without rerunning participants. Readiness
does not wait for the first scheduled callback or require every independent
monitor healthy before startup can settle.

The original host-start token closes new Heartbeat owners/registrations and
Configuration admission while initialization is held. Stopping closes admission
before joining actual startup, provider/watch/notification work, Heartbeat
callbacks/error handling/cleanup and Metrics callback materialization. Existing
REST hosting closes its operations before those joins. Canceled stop waits do
not permit early dependency disposal. The coordinator remains independent of
concrete module and collector implementations.

Management branches precede the business gate: readiness returns 500 while
NotReady and 200 while Ready; business returns 503 while closed. Reporting never
runs initialization/refresh. See [Healthcheck](../healthcheck/README.md) and
[Metrics hosting](../metrics/hosting.md) for transport and callback-owner rules.

From implementations/csharp with the pinned SDK/frameworks:

```powershell
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

Visit /country/MT, /country/MLT or /country/Malta after readiness. Initial loading
uses the real provider. The initialized catalog has an hourly first delay and
completion-based interval. Later failure preserves usable data. Ctrl+C follows
normal awaited shutdown; no embedded holds, observation report or self-test mode
exists. Controlled lifecycle checks stay in dedicated tests.

[CIS-0007 verification](../../changes/CIS-0007/verification.md) retains its original
scoped observations and limits. Current results must identify their actual
inputs; these instructions claim no package publication or broader qualification.
