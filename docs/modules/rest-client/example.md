---
title: RestClient in CountryData
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.example
---

# RestClient in CountryData

[CountryData](../../../implementations/csharp/examples/CountryData/README.md) is
the single runnable example. Its [Program](../../../implementations/csharp/examples/CountryData/Program.cs)
selects public keyed IRestClient and the explicit transient GET preset while
CISApplication composes the existing owners and host lifetime:

```csharp
var countries = new RestClientOptions("countries")
{
    ResponseMapper = new CountryProviderMapper(),
    RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name)
};
countries.Operations.Add("refresh");
builder.RestClients.Add(countries);
builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
```

Use CIS.RestClient and CIS.RestClient.Resilience. CountryProviderMapper is the
application's actual provider mapper; its DTOs are separate from the public
Country contract. CountryCatalog uses the countries keyed client to prime directly
in IStartable and refresh through the same operation in Heartbeat. The overall
request timeout defaults to 30 seconds across attempts, waits and body/mapping
work. Actual cleanup must still settle; there is no sample-owned retry loop or
detached timed-out request.

The provider's error:true envelope fails the logical result even at HTTP 200.
Null, empty and invalid datasets cannot replace the snapshot. Business rejection
and invalid data do not become transient transport-retry candidates. Failed
refresh leaves usable data and its publication time intact. Country lookups,
misses, health reports and scrapes perform no remote request.

From implementations/csharp with the pinned SDK/frameworks:

```powershell
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

GET /country/MT, /country/MLT and /country/Malta return equivalent country data.
The normal application uses the live provider. Controlled origins/cancellation
belong in tests; there is no order workflow or embedded self-test origin/report.

Core-only None remains available without an optional retry or Metrics package.
The small [Core-only package consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient/RestClientPackageConsumerTests.cs)
retains that genuine public boundary. CountryData's package route uses the
versioned Core SDK plus exact optional Resilience and Metrics references, actual
build/folder-publish execution and payload checks. See the canonical guide and
[retry contract](resilience.md). Earlier
[CIS-0010 results](../../changes/CIS-0010/verification.md) retain their historical
inputs, not new CountryData verification or release qualification.
