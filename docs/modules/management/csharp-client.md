---
title: Management C sharp client
type: document
status: Draft
---

# C# management client

`CIS.Management` supplies the twelve typed v1 APIs in the existing Core package.
It borrows the application's selected keyed REST client. Reading applications
does not register this process or start Heartbeat. The optional reporting
integration is a separate selection.
See [optional registration and inventory](registration.md) for its explicit
startup, outage, Jobs projection and actual shutdown contracts.

Configure the management endpoint and its dedicated native REST profile before
installing the typed port:

```csharp
using CIS.Management;
using CIS.Management.Hosting;
using CIS.RestClient;
using CIS.RestClient.Hosting;

var management = new ManagementClientOptions("management")
{
    BaseUrl = "https://management.example/tenant/control"
};
var rest = new RestClientOptions("management");
rest.ConfigureCisManagementRestClient(management);
services.AddCisSecurityBoundRestClient(rest, selectedBindingFactory,
    selectedAuthenticationFactory);
services.AddCisManagementClient(management);
```

The two factories belong to the selected actual RM-01 provider; they have the
existing [security-bound REST](../security/transport.md) signatures. Use the
matching certificate adapter registration for certificate transport. Preparing
the typed profile supplies no credential or production fallback. Ordinary
`AddCisRestClient` is also available when the application's actual authentication
integration supplies its existing callback or transport explicitly.

`AddCisManagementClient` verifies that the matching native profile has already
been prepared and registered. It exposes a keyed `IManagementClient` under the
same alias and preserves the first deliberately registered default alias. It
installs no hosted service or startup participant and constructs no provider.
Options are captured before activation. Explicit `BaseUrl` precedes
`CIS_MANAGEMENT_URL`; missing or invalid selected configuration is an error.

The endpoint must be absolute HTTPS with no user information, query or fragment.
API paths retain the configured prefix: the example addresses
`/tenant/control/management/v1/...`. Ambiguous dot/escaped-separator base segments
are rejected. IDs and original keys are validated before route construction;
the complete keys `.` and `..` are excluded to prevent dot-segment collapse.
Cursors remain opaque inputs to the same typed endpoint.

The codec performs finite encoding and strict/additive response validation
inside the existing REST operation. Default total budget is thirty seconds;
per-call `ManagementCallOptions.Timeout` overrides it. The existing owner covers
authentication, retries, decoding and actual response cleanup. Caller
cancellation remains cancellation; timeout remains its native result category.
Uncooperative callbacks and cleanup still have to settle. No timeout races and
abandons their work.

`ManagementCallResult<T>.Exchange` is the original `RestResult<T>`, including
actual status, headers, safe security evidence, retry facts and cleanup duration.
`Problem` is a safely decoded final `application/problem+json` value when the
complete bounded body permits it. A stale retry attempt, mismatched status or
truncated diagnostic prefix cannot supply final typed facts. Error capture is
64 KiB so valid Unicode metadata and recorded results fit. Native error-message
normalization remains bounded independently of the complete typed problem.

Ordinary responses default to 1 MiB and inventory pages to 8 MiB. Inventory
request defaults remain 4 MiB with 32 modules, 64 category tuples, 1,024 job
definitions and 4,096 total resource observations. Each accepted page carries
complete items; arrays and bodies are never silently truncated. Reported
application/service versions retain the native 512-Unicode-scalar bound.

No retry is selected by default. A read policy must be the actual registered
native `RestRetryPolicy` instance, use at most two retries and the conservative
408/429/502/503/504 set. Register its existing optional executor normally; the
management client introduces no retry owner. Authentication/authorization and
business/decoding failures are not transient retries. Mutation/report/close
methods explicitly select None even if a read policy was configured.

Call `ReplaceMetadataAsync` with the original caller-selected key, strong ETag
and complete `ManagementMetadataCommand`. Its result keeps the recorded business
decision and original transaction completion separate from HTTP success. On an
uncertain exchange, retain that key and use `GetMetadataOperationAsync` under
the current actual credentials. NotFound does not prove rollback or authorize
a new key. No SDK method automatically reruns the mutation. Replay/Found retain
the original committed result even when the new read owner's cleanup differs.

The metadata carrier contains exactly one original branch: `Result` is the typed
recorded value, while `Rejection` preserves native RM-03 `Code` and `Message`.
A native rejected operation has no typed metadata snapshot, revision or decision
time to recover. The SDK leaves those facts absent rather than inventing them.
The opposite branch can be omitted or JSON null; both present or both absent is
invalid. `KnownBusinessDecision` recognizes the rejection branch directly.

`ScriptedManagementClient` in the existing `CIS.CommonTesting` package supplies
finite ordered typed results, original faults and controlled pending work. Its
safe bounded records contain operation kind and IDs/keys, without request
payloads, credentials, ETags or caller contexts. Exhaustion, kind/type mismatch
and script capacity fail explicitly; pre-cancellation consumes nothing. Held
noncooperative work remains held across Clear/cancellation until released.
These scripts establish no SQL, native identity or broker proof.

The caller retains ownership of a standalone borrowed `IRestClient`; stop and
dispose it through its existing lifetime. For a frequent dedicated reporting
profile, select `LogCalls = false` on that profile before native registration.
Other clients retain automatic call logging, metrics and their existing trace
forwarding choices. See the [portable contract](../../../contracts/management/README.md)
for cross-language semantics and the change verification record for actual
executions and remaining deployment limits.
