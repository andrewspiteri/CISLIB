---
title: CISApplication composition and lifetime
type: document
status: draft
cis:
  stable_id: cislib.document.modules.application.core
---

# CISApplication

Use CIS.Hosting.CISApplication for one application host and root provider. The
facade composes the existing Type Manager, Configuration, Startup, Healthcheck,
Heartbeat and Logging implementations. Named REST profiles and supported installed
optional integrations use their existing registration and ownership contracts.
Construction collects setup; it does not prepare types/configuration, perform
requests, build the provider or start a listener.

~~~csharp
using CIS.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;

await using CISApplication app = new(args, builder =>
{
    builder.Services.AddSingleton<IStartable, ApplicationWorker>();
});
return await app.RunAsync();
~~~

ApplicationWorker is the application's ordinary IStartable implementation.
The parameterless, args-only and callback-only constructor forms are also
supported. Complete choices and registrations inside the callback.

For a web application, select the explicit factory and native route mapping:

~~~csharp
await using CISApplication app = CISApplication.CreateWeb(
    args,
    builder => builder.Services.AddSingleton<ApplicationService>(),
    web => web.MapGet("/value", (ApplicationService service) => service.Value));
return await app.RunAsync();
~~~

Use Microsoft.AspNetCore.Builder for MapGet and the ordinary application types
shown above. The final callback receives the actual WebApplication once after
Build and before startup; it does not receive another provider or management host.
Use builder.WebHost for native listener setup in this mode.

## Select existing options

The builder exposes Host and Services for ordinary Microsoft hosting/DI setup,
TypeManager, Configuration, Healthcheck and Heartbeat for their existing option
types, and RestClients for explicit named RestClientOptions. TypeManager's
selected host assembly supplies the Configuration scope. EnvironmentOptions
application/environment names remain telemetry identity and do not redirect
configuration storage.

A null Configuration choice uses initialized defaults through the explicit
one-argument ConfigurationOptions mode. It discovers/validates retained roots,
provides IConfigurationReader and acquires no external watcher or polling.
An external ConfigurationOptions profile must use the selected host assembly's
short name, an actual bootstrap-capable store and the existing interval/health
policy requirements. A real read failure fails preparation rather than selecting
defaults.

No REST selections create no REST owner. Adding a named profile creates the
ordinary keyed IRestClient; selecting retries still requires the existing
explicit policy/optional adapter. Registration never makes a request. No backend,
default client alias, default retry policy or configuration polling interval is
invented.

LoggingEnabled, HealthcheckEnabled and HeartbeatEnabled control automatic defaults.
EnableOptionalIntegrations controls the narrow prepared-catalog integration hook.
Supported installed adapters register through that hook before Build and attach
their existing operational web pipeline. Core alone supplies inert IMetrics,
without a provider or endpoint. Actual optional-package composition and exported
output have separate verification; this guide is not evidence of those passes.

In web mode, detailed reports require effective HealthcheckOptions.DetailsEnabled
and builder.AuthorizeHealthDetails, an application-owned
Func<HttpContext, ValueTask<bool>>. Omitted authorization denies access; HTTP
authorization does not authorize local pipe clients.

The health branch and business gate read the selected IHealthSnapshotSource.
Its default alias shares the concrete HealthcheckService. Replacing this
snapshot-only interface changes those observations; it does not replace hosted
health mutation, Startup, Heartbeat or their lifetime barriers. The native
ApplicationStarted/ApplicationStopping gate remains mandatory.

Lazy defaults are present before customization. Use normal Replace/RemoveAll and
logging ClearProviders deliberately; AddSingleton appends a registration and does
not replace all IEnumerable entries. Retain ordinary service lifetimes and explicit
IStartable enrollment. The facade never enrolls arbitrary discovered application
classes as singleton workers.

## Preparation and readiness

StartAsync completes real Type Manager preparation, initial Configuration,
final registration and one Build before required startup. Constructors of
configuration-consuming roots see initially loaded values. Stage 1 of the existing
coordinator constructs all scheduled startup roots and dependencies before invoking
any participant. Stage 2 then starts those roots sequentially, with Configuration
activation prepended before application participants. Same-reference aliases start
once. Initial Configuration loading happens before construction; its later
activation establishes refresh observation and reconciles the bootstrap gap.

The web listener may serve operational requests while a later initializer is
still held. Native routing and supported optional operational integration precede
the existing health branch and business gate. The readiness probe returns 500
before required startup and 200 after success; business requests return 503 while
gated. A failed/canceled startup cannot publish readiness. Health policy can close
the gate later through the existing Healthcheck snapshot.

Services exposes the actual built provider; it does not trigger Build or startup.
Failure retains the first failure actually observed on the facade's lifecycle
path. RunAsync starts, awaits ordinary host shutdown and joins stopping, returning
zero for normal/requested stopping and nonzero for an observed lifecycle failure.
It does not call Environment.Exit or add a background failure detector.

## Stop and dispose

Repeated/concurrent starts join the retained first startup operation. The first
startup cancellation token owns that operation; a later caller cannot replace it.
Stopping is terminal and cannot restart readiness.

StopAsync signals stopping and joins true owned work. Its cancellation token
can end only that caller's wait. REST/provider admission closes before waiting
for Heartbeat or Configuration, including callbacks awaiting REST. Actual
callback, response/body cleanup and observable drains precede dependency teardown.
Retain cooperative cancellation in application work.

Always await DisposeAsync, including after preparation, Build, route mapping or
startup failure. It joins the shared stop/disposal operations and cleans up
resources prepared before DI ownership. The facade captures the one actual native
provider when the configured factory returns it. If host resolution fails later
inside Build, asynchronous disposal still owns that provider and its already
created services; cleanup does not resolve additional services. When Build returns
a host, that host owns provider disposal instead. Validation failure before a
provider is returned retains the existing pre-container cleanup path. Repeated
alias disposal must be idempotent; native DI does not promise to call aliased
objects only once.
Externally supplied stores and instance resources keep their existing ownership.
A DI-created CoreLogRuntime joins its actual Completion after module/host stopping
and before root disposal, including a timed-out bounded logging stop. An instance
registration remains borrowed and must be stopped/drained by its external owner.
A failed stop wait does not authorize independent root disposal.

DisposeAsync errors propagate independently and cannot retroactively change a
returned RunAsync value. Read the current
[verification record](../../changes/CIS-0011/verification.md) for actual native
results and remaining platform/package/release limits. The failed-Build provider
capture follow-up is tracked separately in [CIS-0012](../../changes/CIS-0012/proposal.md);
prior results are not verification of that new diff. These examples and authored
tests alone establish no execution result.
