# CountryData

One normal CIS application, using the existing host, REST client and optional
Metrics/retry packages. From implementations/csharp:

    dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087

Use pinned SDK 10.0.401 and .NET/ASP.NET Core 10.0.12. Source builds use ordinary
project references and the real Type Manager inventory targets. Core remains
independent of the optional CIS.Metrics.OpenTelemetry and CIS.RestClient.Resilience
packages. The selected Prometheus exporter is the existing prerelease
1.21.0-beta.1; locked dependencies include DiagnosticSource 11.0.0 and Polly.Core
8.9.1. A package consumer uses their normal runtime assets and Core's versioned SDK.

    curl http://127.0.0.1:5087/country/MT
    curl http://127.0.0.1:5087/country/MLT
    curl http://127.0.0.1:5087/country/Malta

All return equivalent name/iso2/iso3 JSON; case variants work. Identifiers are
trimmed and matched exactly, without fuzzy matching. Unknown identifiers return
404 with code country.not_found; whitespace identifiers return 400 with code
country.invalid_identifier. Lookups and misses read the local snapshot without
requesting provider data.

The route explicitly selects `AddCisWorkflows()` and `AddCisStateMachines()`.
Each request builds a `CountryLookup` machine: Pending --Resolve--> Resolved
--Complete--> Completed. Resolve uses a fresh child Pipeline in the current
request scope: `normalize`, `find`, then `prepare_response`. Complete is direct;
Completed means lookup preparation, not confirmed HTTP delivery.
`CountryLookupStateMachine.cs` and `CountryLookupPipeline.cs` contain the actual
contexts, mappings and DI-activated work. An ordinary unknown identifier fails
in `find`, invokes its `Error` before `normalize.Error`, and leaves
`prepare_response` unentered. Resolve retains Pending; Complete is not called.
The outer handler never repeats the child's compensation and retains only one
detached child summary. The published catalog remains unchanged. See the
[Pipeline guide](../../../../docs/modules/workflows/pipeline.md) and
[State Machine guide](../../../../docs/modules/workflows/statemachine.md) for
results, scopes and automatic observations. Periodic refresh keeps its existing
bounded diagnostics and is not wrapped in either workflow.

CISApplication prepares Type Manager and Configuration before building one real
provider. It composes logging, health, Heartbeat, Startup, the selected named REST
profile and the installed Metrics adapter. The default CountryApplication supplies application services,
intentional client/retry choices, one native route and RunAsync.

The catalog primes directly from the live CountriesNow ISO endpoint before
readiness. It accepts only a valid provider envelope and complete nonempty dataset,
with valid codes and no duplicate/conflicting aliases. A complete replacement is
published atomically. No expected world-country count is hardcoded.

CountrySettings defaults are:

| Setting | Default |
| --- | --- |
| SourceUrl | https://countriesnow.space/api/v0.1/countries/iso |
| RefreshInterval | One hour |
| RequestTimeout | 30 seconds across attempts, waits, mapping and cleanup |
| RetryPolicyName | transient-http; null explicitly selects None |

The explicitly installed existing transient-http preset allows two GET retries
for statuses 408, 429, 502, 503 and 504. Transport errors, business rejection and
invalid data do not become retry candidates. There is no application retry loop. Both Heartbeat
InitialDelay and Interval use RefreshInterval; the initial delay starts at
readiness and subsequent intervals start after full previous settlement.

Without a store, CIS Configuration uses initialized object defaults with no
external updates, polling or watcher. Deliberate overrides use the existing
bootstrap ConfigurationOptions/store or default factories on the facade builder.
Host appsettings.json does not silently populate these objects. Settings are
captured consistently once for the catalog lifetime; changes require restart.
Configuration scope is CIS.CountryData even if telemetry application identity
is overridden.

Central Configuration is an explicit host-composition opt-in through
`CountryCentralConfiguration.Configure(builder, centralOptions, pollingInterval)`
inside the existing `CountryApplication.CreateService` callback. Only
`CountrySettings` is enrolled as `country.settings`, schema `"1"`,
`RestartRequired`; native bootstrap endpoints/trust/credentials remain independent.
The helper preserves defaults, legacy store, factories and composed validators.
URL, duration and installed tutorial retry choices are validated on the detached
complete candidate before staging. The default program remains unmanaged.
See the [operator recipe](../../../../docs/modules/management/countrydata-configuration.md)
for common plus application overrides, original-key publication/recovery,
PendingRestart/Rejected observations and restart/rollback steps. The public
[unit examples](../CountryData.UnitTests/Configuration/CountryCentralConfigurationExamples.cs)
live in the solution's existing example test project and perform no native I/O.

Initial load failure prevents readiness. Later failed/invalid refresh keeps the
last complete snapshot and last-success time; availability has no expiry.
Expected application stopping cancels owned REST work without an unexpected
upstream-error log. Ctrl+C requests normal shutdown; the existing owner/module
barriers join the actual callback and cleanup before dependencies are disposed.

Initial load and publication are logged once at Information during normal
startup. Routine successful callbacks emit no normal-level event. The first
expected failure is Warning; an unexpected exception is Error. Failure event
2101 and recovery event 2102 have separate monotonic one-minute budgets: after
their first eligible reports, at least one minute separates reports in each
category. Each summary includes its category's suppressed count. Rapid flaps
retain both budgets, UTC changes do not reset them, and no delay throttles work.
Reporting faults do not change refresh results, health handoff or original faults.

Program explicitly sets `LogCalls = false` on the countries REST profile to
avoid producing a separate automatic REST event on every callback. The option
defaults to true for other clients and is captured at registration. It does not
change REST outcomes, cancellation, metrics or unrelated application logging.
The one-hour refresh default remains unchanged; the diagnostic policy is also
safe for frequent callbacks, including deterministic 10 ms test sequences.

Operational endpoints precede the business gate:

- /_health/ready: existing 500 while NotReady, 200 while Ready.
- /_health: ordinary detached health report; detailed reporting is disabled by default.
- /_metrics: real optional adapter endpoint; absent when the adapter is not selected/enabled.

Business requests return 503 while readiness is closed. Scrapes and health reads
never bootstrap or fetch data. Native/module HTTP and lifecycle observations keep
the authoritative application/environment labels. The only new application
metrics are dimensionless observable snapshot state:

| Logical definition | Exported family | Meaning |
| --- | --- | --- |
| app.country_cache.entries | app_country_cache_entries | Distinct records, not aliases; real zero before first publication. |
| app.country_cache.last_success_timestamp, Seconds | app_country_cache_last_success_timestamp_seconds | Publication Unix seconds; omitted before success and unchanged by failed refresh. |

The verified exporter mapping and focused Windows observations are recorded in
CIS-0011's verification record. This authored guide alone is not a passing scrape,
live-provider smoke or release qualification claim. Callback observations perform
only bounded in-memory reads, and disabled metrics do not change catalog behavior.

Tests supply controlled local HTTP responses and timing through existing
configuration/DI boundaries. The application has no self-test/failure flags,
barriers, reports, hardcoded live data or alternative HTTP implementation.

## Explicit Audit observation

The default application does not select Audit delivery or register the optional
[CountryCatalogAuditObservation](Audit/CountryCatalogAuditObservation.cs) helper.
An application can explicitly select the existing Audit/Messaging integration
described in the [Audit consumer guide](../../../../docs/modules/audit/README.md),
then register this scoped helper:

```csharp
builder.Services.AddCountryCatalogAuditObservation();
```

After the catalog is ready, invoke it explicitly from an owned application scope:

```csharp
await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
CountryCatalogAuditObservation observation =
    scope.ServiceProvider.GetRequiredService<CountryCatalogAuditObservation>();
AuditSubmissionResult result = await observation.SubmitAsync("MT", cancellationToken);
// Inspect result.Outcome and retain its actual available receipt/completion.
```

The helper resolves the country from the actual current catalog and captures
`country.catalog.observed` version 1, category `business`, outcome `succeeded`.
Its explicit details contain only `name` (one through 512 Unicode scalars, no
controls or malformed Unicode), uppercase `iso2` (two ASCII letters) and `iso3`
(three ASCII letters). Its target is the local application/environment's `country`
resource with the canonical ISO2 identifier. The scoped capture supplies the
actual trusted caller; without one the actor remains unknown. A missing country
fails before capture/submission. No lookup provider request is added.

This describes an observation of an existing in-memory snapshot. It makes no
business change and cannot make the earlier catalog publication atomic with SQL.
The result preserves `OutboxCommitted`, `NotSubmitted` or `Unknown`; none proves
broker confirmation, receiver processing or archive storage. The helper submits
once and never retries Unknown. Required transactional business-change Audit is
demonstrated separately in the existing real-provider fixture.

The [example unit tests](../CountryData.UnitTests/Audit/CountryAuditObservationExamples.cs)
reuse the actual catalog with existing CommonTesting REST/Heartbeat substitutes,
public `ConfigureCisAudit` trusted capture and bounded `ScriptedAuditWriter`.
They exercise public contracts without a database, broker or friend constructor.
Scripted submission/completion facts prove caller behavior, not native durability.
From `implementations/csharp`, run the existing example test project with
`--filter FullyQualifiedName~CountryAuditObservationExamples` for focused checks.

## Selected security service and caller

The default `--security=none` retains the behavior above. Explicit
`--security=local|certificate|entra` protects lookups and adds one bounded
selected-country value. Use `--role=caller` on the same binary for the real REST
caller. The server authorizes canonical cached country resources: selecting MT
is permitted and selecting TTO is denied. The native integration test verifies
that both selection and revision stay unchanged after denial. No selection
history or durability is promised. The caller runs one normal operation per
invocation; its default is a harmless read of MT.

After setting the caller identity and `$securityConfig` as described in the setup
guide, run these explicit operations from `implementations/csharp`:

```powershell
dotnet run --no-launch-profile --project examples/CountryData -- --role=caller --security=local "--security-config=$securityConfig" --operation=read --country=MT
dotnet run --no-launch-profile --project examples/CountryData -- --role=caller --security=local "--security-config=$securityConfig" --operation=select --country=MT
dotnet run --no-launch-profile --project examples/CountryData -- --role=caller --security=local "--security-config=$securityConfig" --operation=select --country=TTO
dotnet run --no-launch-profile --project examples/CountryData -- --role=caller --security=local "--security-config=$securityConfig" --operation=inspect
```

The denied selection exits with status 1. Compare the allowed selection's value/
revision with the final inspection. Output contains only the safe detached
business result, actual HTTP status and safe error code.

The [security consumer guide](../../../../docs/modules/security/consumer.md)
describes the exact policy/configuration inputs, endpoints and custody. Follow
the [local setup guide](../../../../docs/modules/security/local-development.md)
for copyable service/caller commands using explicit central/native identities and
an absolute `--security-config` path. Its public JSON bootstrap uses the existing
native host and CIS Configuration reader; credential/server PEM loads only in
sequential Startup. Native owners drain after the actual host and responses.
The separate operational listener retains `/_health`, `/_health/ready` and
`/_metrics`; Host headers cannot open a business bypass.

The selected example awaits authorization and separate operation-audit hooks.
An operation hook fault after selection does not roll back the committed value
or establish successful audit delivery. Production certificate and Entra inputs
are deliberate deployment configuration. Local tests do not establish AC-02 AKS,
AC-24 cert-manager, roadmap completion or release qualification.

## Testing examples

Start with the discoverable MSTest examples in
[CountryCatalogUnitExamples.cs](../CountryData.UnitTests/CountryCatalogUnitExamples.cs)
and [CountryLookupStepUnitExamples.cs](../CountryData.UnitTests/CountryLookupStepUnitExamples.cs).
They live in the dedicated `CountryData.UnitTests` project under the solution's
`examples` folder. That project references this compiled application
and uses public APIs, without starting the application or a web server.

From `implementations/csharp`, run:

```powershell
dotnet test examples/CountryData.UnitTests/CountryData.UnitTests.csproj -c Release
```

The small [test setup](../CountryData.UnitTests/CountryCatalogExample.cs)
reuses `CIS.CommonTesting`: `ScriptedRestClient`, `ManualHeartbeat`,
`ManualTimeProvider`, `RecordingHealthMonitorFactory` and
`RecordingLogger<CountryCatalog>`. `DisabledMetrics.Instance` supplies the public
Metrics no-op boundary. The only application-specific substitutes are an
initialized-settings reader and a test-owned application-stopping token.
Set `RetryPolicyName = null` when the test supplies no retry policies.

| Unit example | Expected result |
| --- | --- |
| Initial publication and aliases | Name, ISO2 and ISO3, including trimmed/case variants, return the same record; lookups and misses make no additional logical REST call. |
| Explicit manual refresh | Registration captures the configured delay/interval; advancing the clock alone makes no request, then an awaited `TriggerAsync` performs one refresh. |
| Successful replacement | The complete new dataset replaces the previous dataset, including removal of old aliases. |
| Failed/invalid refresh | HTTP/business failure, invalid codes, duplicate rows or an empty dataset retain the last good record and report refresh failure. |
| Initial failure | `StartAsync` fails without creating a refresh registration or publishing usable data. |
| Unexpected dependency fault | The original exception reaches the manual result and recorded Error event; the previous snapshot remains usable. |
| Held refresh and disposal | The old snapshot remains readable while work is held; catalog disposal joins it before withdrawing the recording monitor. |
| Individual lookup steps | Normalization trims input, missing input/countries produce stable failure codes, and response preparation/compensation updates only its own draft. |

These unit examples control logical results and execution explicitly. They do not
validate sockets, retry, codecs, provider-envelope mapping, the Heartbeat scheduler,
real health-policy evaluation or hosted shutdown. `EnqueueJsonValue` supplies a
logical success for the actual response type; it does not invoke
`CountryProviderMapper`. Use `EnqueueFailure` for logical business/HTTP failures.
The held example uses `finally` to release and await actual work even if an
assertion fails. There are no sleeps or live provider calls.

After the unit layer, use the existing
[CountryData integration tests](../../tests/CIS.Core.IntegrationTests/CountryData/CountryDataTests.cs)
for native hosting, HTTP/readiness and the real State Machine/child Pipeline;
the [package test](../../tests/CIS.Core.IntegrationTests/CountryData/CountryDataPackageConsumerTests.cs)
for local-package build/publish and exporter output; and the
[public consumer](../../tests/CIS.Core.IntegrationTests/CountryData/CountryDataPublicConsumerTests.cs)
for substitution and helper usage outside friend assemblies. This testing order
keeps application unit examples useful without substituting them for integration proof.

## Shared coordination recipes

The compiling [Coordination recipes](Coordination/CountryMaintenanceJobs.cs) are
explicit opt-in application code in this same example. The normal CountryData
service and caller select no database, coordination provider, jobs or scheduling
source. Installing the module or inspecting the catalog starts no work.

`AddCountryMaintenanceJobs` registers exactly these independent responsibilities
and freezes their passive catalog:

| Registered target | Handler | Category and mode |
| --- | --- | --- |
| `wallet-service / wallet.compaction` | [WalletCompactionJob](Coordination/WalletCompactionJob.cs) | `maintenance`, Continuous finite batches |
| `customer-service / customer.dormancy` | [CustomerDormancyJob](Coordination/CustomerDormancyJob.cs) | `lifecycle`, Scheduled external occurrences |

`CountryMaintenanceJobs.Settings()` disables the host and both jobs. The customer
settings describe Daily 02:00 in `Europe/Malta`, missed runs `Skip`, nonexistent
local times `Skip`, repeated times `OnceAtEarlierInstant`, and
`ExternalOccurrences` activation. This descriptor performs no recurrence
calculation. A compatible trusted source must already have admitted each run;
automatic recurrence additionally needs a later compatible evaluator.

An application selecting these recipes supplies its host-lived BCL data source
and existing `ILocalTransactionFactory`, installs the selected authority schema
explicitly, and registers the guard, trusted target resolver, application work
ports and Operations definitions. The `Maintenance` alias must select the same
authority database used by the original-context guard. Register a durable
`customer.dormancy` definition for `CustomerDormancyInput` /
`CustomerDormancyResult`, with explicit canonical input/result codecs, stable
scope-to-resource mapping and existing execute/lookup capabilities. The scheduled
Messaging payload codec in this example is a separate transport contract.

[SecurityJobScopeFactory](Coordination/SecurityJobScopeFactory.cs) wraps the actual
`SecurityHostContextScope`. Its trusted hosting supplier performs native workload
authentication for the selected receiving profile; serialized callers or ambient
HTTP principals cannot establish this authority. The ordinary scoped authorizer
still checks the complete resolved resource and job capabilities. The application
can register both definitions only when its current established authority permits
both targets. Sharing category text supplies no permission.

After supplying those explicit dependencies, the existing services callback can
select the known hosting integration:

```csharp
services.AddCountryMaintenanceJobs();
services.AddSingleton<IJobExecutionScopeFactory>(root =>
    new SecurityJobScopeFactory(root, "maintenance-native", AuthenticateMaintenanceWorkloadAsync));
services.AddCisCoordination(coordinationOptions);
services.AddCisJobs(CountryMaintenanceJobs.Settings(1,
    enabled: true, walletEnabled: true, customerEnabled: true), new JobHostingOptions
{
    ContinuousTargets =
    [
        new("wallet-service", "wallet.compaction", "partition-1", JobExecutionMode.Continuous)
    ]
});
```

The imports for that composition are `CIS.Examples.CountryData.Coordination`,
`CIS.Jobs`, `CIS.Jobs.Hosting` and `CIS.Coordination.Hosting`. The authentication
function and provider/business registrations belong to the application. Keep the
usual DI validation and [sequential hosting order](../../../../docs/modules/coordination/hosting.md).
A direct Generic Host consumer arranges the known bridges explicitly; the facade
arranges its cooperating descriptors.

The wallet handler begins one owner, validates the exact grant on that original
context, invokes one finite `IWalletCompactionWork` batch, then inspects actual
commit completion. The work implementation must atomically read/advance its
stable durable checkpoint with the batch writes. Committed maps to MoreWork or
Idle; Unknown remains Indeterminate, and no automatic business replay is added.
Checkpoint and operation identities must survive generation changes.
This wallet recipe uses direct checkpointed writes in the original context.
If the application instead adds RM-03 to wallet compaction, claim its journal
entry before taking the grant guard, following the customer recipe's lock order.
Do not invoke Operations from `IWalletCompactionWork` after the guard is held.

The customer handler retains the admitted original `MessageOperationReference`,
including its independently resolved scope and opaque key. Its actual Operations
handler validates the grant after the journal claim and before calling
`ICustomerDormancyWork`, following the common journal → grant → business/outgoing
lock order. That application port must write its protected changes and stage any
`IMessageOutbox` intent in the same supplied context, using stable outgoing IDs
bound to the original input/operation. It must not send directly or acknowledge
the scheduler occurrence. Prepared is committed by the owner before Completed;
Unknown returns Indeterminate. Explicit `RecoverAsync` performs a fresh authorized
original-result lookup; a missing result alone cannot establish rollback. Replayed
returns PreviouslyCompleted without running the business handler. The new read
owner is rolled back, and that read cleanup is never presented as the original
business commit. Committed outgoing intent remains valid after ownership ends.

Start testing these application boundaries with
[MaintenanceRecipeExamples](../CountryData.UnitTests/Coordination/MaintenanceRecipeExamples.cs)
in the existing solution `examples / CountryData.UnitTests` project. They reuse
public `ManualCoordinationLease`, `ScriptedLocalTransactionFactory`,
`ScriptedDurableOperationService`, `ScriptedCoordinationService` and
`ScriptedJobRunSource` helpers. Assertions cover correct handler selection,
original-context guard order, finite batch results, refused/unknown commits,
unchanged original identity, explicit recovery and controlled external dispatch.
Scripts perform no SQL, authentication, automatic renewal, durable journal claim
or outgoing delivery. The one clearly named application handler stub invokes the
callback only to test that recipe's guarded context interval; it supplies no journal
semantics. Manual generations in one test process are not independent application
instances or a database contention proof.

The separate [native consumer proof](../../../../docs/modules/coordination/consumers.md)
uses the existing isolated PostgreSQL, broker and public-package process fixtures
for actual original-context atomicity, increasing generations, committed outgoing
intent, and genuinely separate process-instance crash takeover. Keep those actual
results and platform limits distinct from these application unit examples.
