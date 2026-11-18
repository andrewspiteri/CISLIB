---
title: RestClient application and package example
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.example
---

# RestClient application and package example

The actual [RestApi project](../../../implementations/csharp/examples/rest-client/RestApi/RestApi.csproj)
uses public keyed `IRestClient`, the optional retry registration and the existing
Metrics collector. Its [gateway](../../../implementations/csharp/examples/rest-client/RestApi/OrderGateway.cs)
keeps request/response DTOs in application code. The source story and isolated
package consumers passed on Windows with SDK 10.0.401 and runtime 10.0.12; see
[verification](../../changes/CIS-0010/verification.md) for their retained scope.

From `implementations/csharp`, run:

```powershell
$env:CORE_SERVICE_NAME = 'RestClient.Example'
$env:CORE_ENVIRONMENT = 'development'
dotnet run --project examples/rest-client/RestApi/RestApi.csproj -- --self-test
```

Self-test mode starts a real loopback origin before capturing the client's base
URL and credential origin. It prepares Type Manager once before DI, starts the
consumer's real Web host, exercises its gateway and scrapes `/_metrics`.
Both owned hosts stop and dispose before one JSON report is printed.

The five operations demonstrate:

- JSON POST with the default None policy and an awaited authentication callback.
- Explicit transient GET policy, with 503/503/200 and three owned dispatches.
- An HTTP 200 envelope mapped to `orders.declined`, with no successful value.
- HTTP 204 success with absent value.
- Caller cancellation during admitted preparation, with no primary dispatch.

The report observes five logical completions, balanced active work and at least
six native HTTP observations. Those native observations end at headers; their
count is not a packet count or a substitute for the result's owned dispatch count.
The more detailed runtime cases separately verify body/encoder cancellation and
actual shutdown cleanup.

Normal mode requires `REST_BASE_URL` for a backend implementing the demonstrated
order endpoints. `REST_ACCESS_TOKEN` supplies the bearer value; the example's
placeholder is for demonstration. No production backend is selected. The public
consumer routes are POST `/orders` and GET `/orders/{id}`; `/exercise` and the
local origin exist only in self-test mode. Keep deployment access policy on the
management endpoint as described in [Metrics hosting](../metrics/hosting.md).

The [package case](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient/RestClientPackageConsumerTests.cs)
packs the actual current Core, Resilience and Metrics assemblies into a local feed.
Two initially empty caches restore versioned packages without project references.
The first consumer uses Core's public None client without optional dependencies;
the second compiles the exact example sources against the versioned Core SDK and
the two optional PackageReferences. Each builds, folder-publishes and executes
its deployed output. Assertions compare package/current/deployed DLL hashes,
actual loaded paths, the pinned runtime and optional Polly/DiagnosticSource assets.
These checks do not establish Ubuntu, full coverage or full mutation qualification.
