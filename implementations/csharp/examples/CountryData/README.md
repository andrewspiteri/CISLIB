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

The route opts into `AddCisWorkflows()` and runs a fresh `CountryLookup` pipeline
in the actual request scope: `normalize`, `find`, then `prepare_response`.
`CountryLookupPipeline.cs` contains the typed context and DI-activated steps.
An ordinary unknown identifier fails in `find`, invokes its `Error` before
`normalize.Error`, and leaves `prepare_response` unentered. Compensation clears
only the local draft; the published catalog remains unchanged. The
[pipeline guide](../../../../docs/modules/workflows/pipeline.md) covers reports,
owned versus borrowed scopes and automatic phase/run observations. Periodic
refresh keeps its existing bounded diagnostics and is not wrapped in this pipeline.

CISApplication prepares Type Manager and Configuration before building one real
provider. It composes logging, health, Heartbeat, Startup, the selected named REST
profile and the installed Metrics adapter. Program supplies application services,
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
