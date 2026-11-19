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
