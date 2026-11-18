---
title: CountryData readiness and availability
type: document
status: unknown
cis:
  stable_id: cislib.document.modules.healthcheck.country.list
---

# CountryData readiness and availability

The canonical [CountryData application](../../../implementations/csharp/examples/CountryData/README.md)
uses CISApplication with existing Healthcheck, Startup, Heartbeat, REST and optional
Metrics integration. This stable guide path replaces the retired tutorial, without
preserving its self-test schema.

From implementations/csharp with SDK 10.0.401 and NET/ASP.NET Core 10.0.12:

```powershell
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

CountryCatalog directly awaits the complete initial REST refresh in IStartable.
Its critical availability monitor records usable publication. The same catalog
then registers through its retained Heartbeat owner with both InitialDelay and
Interval defaulting to one hour. The existing binding reports subsequent outcomes;
the callback does not manufacture a second health publication. Failed refresh
retains usable data without expiry; failed initial loading never opens readiness.

Management branches precede the business readiness gate:

- /_health/ready returns 500 while NotReady and 200 while Ready.
- /country/MT and other business requests return 503 before readiness.
- /_health reads recorded aggregate facts without running startup or refresh.
- Detailed reporting is disabled by default. With details enabled, the facade denies access unless builder.AuthorizeHealthDetails supplies an explicit native authorization predicate.

See [transports](transports.md) for access, bounds, HEAD/method and local-identity
rules. Country lookups, scrapes and reports never fetch provider data. Startup
completion follows actual participant settlement and Heartbeat activation.
Shutdown closes admission, cancels owned REST work and joins actual callback/body/
handler/reporting work before monitors and dependencies are disposed. An expired
wait or requested cancellation alone does not establish that barrier.

## Local pipe

Pipe reporting remains explicit deployment policy:

```powershell
$env:CIS_HEALTHCHECK_PIPE_ENABLED = 'true'
$env:CIS_HEALTHCHECK_PIPE_NAME = 'country-data-health'
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

From another terminal in implementations/csharp:

```powershell
dotnet run --project tools/CisHealthProbe -- --pipe country-data-health --view readiness --timeout-ms 1000
```

The matching probe returns 0 for complete valid Ready, 1 for valid NotReady and 2
for argument/endpoint/protocol failure. Use the intended same local identity and
reachable writable endpoint location. Details require a separate opt-in; HTTP
authorization does not protect IPC.

## Match the pipe probe to the package

Build the development probe against the same exact Core candidate as the app;
there is no separately published probe distribution. Normal package consumers
use Core's versioned SDK plus this application's optional packages. Do not add a
duplicate Core PackageReference or reuse a stale tutorial probe. Genuine package
tests compare candidate/package/deployed bytes; deploy the actual matching tool.

Existing HealthcheckHttpHost, protocol/client and matching probe fixtures retain
distinct transport/failure/ownership boundaries. Historical
[CIS-0004 package/platform results](../../changes/CIS-0004/verification.md) and
[CIS-0007 startup results](../../changes/CIS-0007/verification.md) retain their
original inputs and scope, not CountryData or new deployment qualification.
