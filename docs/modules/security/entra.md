---
title: Entra workload identity integration
type: document
status: draft
change_id: CIS-0016
---

# Entra workload identity

The optional `CIS.Security.Entra` package connects an explicitly selected
`EntraWorkloadIdentity` profile to `Azure.Identity.WorkloadIdentityCredential`
and the native ASP.NET Core JWT bearer handler. It does not select a profile
merely by being installed. Core-only and certificate-only applications do not
need Azure dependencies. See the [common contract](README.md) for complete grants,
caller scope, host stages, audit and policy replacement.

The selected binding is Azure.Identity 1.21.0 and JWT bearer 10.0.12, on the
repository's existing .NET 10 baseline. Native registration uses
`AuthenticationBuilder.AddJwtBearer`; it preserves unrelated host authentication
configuration and does not add a Data Protection owner.

## Deployment values and trust

Collect these non-secret inputs from the intended deployment:

| Value | Source and validation |
| --- | --- |
| Tenant ID | The workload/API's Entra directory GUID. One tenant is accepted per receiving scheme. |
| Authority origin | The explicit trusted cloud authority; default `https://login.microsoftonline.com/`. The issuer is that origin plus the tenant ID and `/v2.0`. |
| API audience | The exact v2 token `aud` for the registered API, normally its application/client GUID. This is configured independently of the caller's scope URI. |
| Caller client ID | The application/client GUID of the workload identity. Receiver mappings match the validated `azp` exactly. |
| Caller object ID | The service-principal object GUID in that tenant, such as a user-assigned managed identity's `principalId`. Receiver mappings match validated `oid`; this is not the client ID or the application-registration object ID. |
| Application role | The API's explicit app-role value, such as `Country.Read`, assigned to the workload service principal. Delegated `scp` is rejected. |
| Federation subject | The exact configured `system:serviceaccount:<namespace>:<service-account>` relationship. Entra validates it against the projected assertion. |
| Projected assertion path | The AKS workload-identity injected `AZURE_FEDERATED_TOKEN_FILE` path, supplied explicitly to provider setup. The SDK reads and refreshes this source. |
| Scope and destinations | The trusted intended API resource `/.default` scope and exact nonempty HTTPS `AllowedOrigins`. Neither comes from an arbitrary request parameter. |

Configure the API registration's access-token version as **2** and include the
optional **`idtyp` access-token claim**. This adapter requires `idtyp=app`, `ver=2.0`,
the exact `tid`/issuer/audience, and one valid `oid` and `azp`. A user/delegated
token does not become a workload because it has a matching role or application
identifier. Tokens containing `scp` are rejected. Signature, lifetime and signing
keys are checked by the actual native handler; decoding is not authentication.

Receiver mappings and grants come from its trusted startup policy. The caller's
local application/environment metadata, copied headers or configuration cannot
grant permission remotely. Mapping uses issuer plus object ID and also checks
the client ID. A valid roleless workload receives no implicit grant.

## Register an inbound-only API

These are registration fragments for the existing application host. Obtain the
values from its trusted Configuration setup; the GUIDs below are placeholders,
not a deployed tenant. Replace all placeholders consistently.

```csharp
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;

const string tenantId = "11111111-1111-1111-1111-111111111111";
const string callerObjectId = "22222222-2222-2222-2222-222222222222";
const string callerClientId = "33333333-3333-3333-3333-333333333333";
const string apiClientId = "44444444-4444-4444-4444-444444444444";
string issuer = "https://login.microsoftonline.com/" + tenantId + "/v2.0";

var policy = new SecurityOptions { PolicyVersion = "country-policy-v1" };
policy.Profiles.Add(new SecurityProfile("workload", SecurityProfileKind.EntraWorkloadIdentity,
    SecurityProfileRoles.Inbound, authenticationScheme: "country-jwt",
    identityMappings: [new SecurityIdentityMapping(new(issuer, callerObjectId),
        "country-worker", "Production", clientApplicationId: callerClientId)]));
policy.Capabilities.Add("country.read");
policy.Capabilities.Add("country.selection.change");
policy.Grants.Add(new CapabilityGrant("workload", "country.read", "country-api", "Production",
    subject: new(issuer, callerObjectId), requiredProviderRole: "Country.Read"));

services.AddCisSecurity(policy);
services.AddCisEntraSecurity("workload", _ => new EntraSecurityOptions
{
    TenantId = tenantId,
    Inbound = new EntraInboundOptions { Audience = apiClientId }
}, authenticationScheme: "country-jwt");
```

No outgoing client ID, assertion file, scope or workload credential is required
by this receiving role. The default clock skew is explicitly thirty seconds;
zero through 120 seconds are accepted. This inbound native tolerance is not a
grace period for sending an already expired access token.

The selected scheme has fixed native validation and sealed event ownership.
A final native options validator rejects later replacements of signature,
issuer, audience, lifetime, algorithm, token-handler, forwarding or event-type
settings. Mutable event delegate properties cannot fabricate success. The
projector requires private evidence attached to the actual validated ticket and
uses its immutable validated facts/expiry, not a caller-created principal or
an `ExpiresUtc` field alone. This is per-ticket evidence, not a principal cache.
An explicitly trusted native metadata manager can be supplied through
`EntraInboundOptions.MetadataManager`; it does not disable these checks.

For retained Configuration roots, use the same selected root and read gate:
`AddCisSecurity<TSettings>(settings => BuildPolicyFromPassiveSettings(settings))` and
`AddCisEntraSecurity<TSettings>("workload", settings => BuildEntraOptionsFromPassiveSettings(settings),
authenticationScheme: "country-jwt")`. `TSettings` is the application's existing
`IConfigurationObject`. These application-owned selector functions construct
detached setup options from passive scalar/list configuration under the existing
`IConfigurationReader.Read` gate after initial preparation. Do not persist
`SecurityOptions` or `EntraSecurityOptions` directly as mapped roots: their
get-only policy collections and native SDK interface/abstract properties are not
passive Configuration shapes. Keep native transports/metadata managers in trusted
composition; explicitly exclude computed setup properties from persisted shapes
with `JsonIgnore` if used. The non-generic factory also returns detached setup. Later changes
to trust, mappings, capabilities or grants require a replacement instance.

The facade installs explicit authentication/projection and authorization stages
when common security is selected. Direct native hosts install those stages in
the [documented order](README.md#explicit-host-stages-and-surface-selection) and
use the existing sequential Startup integration for the provider's `IStartable`.
There is one service provider. Operational endpoints retain their own access
rules; this profile does not install a blanket fallback authentication policy.

Endpoint registration uses the same grant evaluator as imperative checks:

```csharp
app.MapGet("/security/read", () => Results.Ok("permitted"))
    .RequireCisCapabilities("workload", new CapabilityTarget("country-api", "Production"),
        "country.read");

app.MapPost("/security/change", () => Results.Ok("protected effect"))
    .RequireCisCapabilities("workload", new CapabilityTarget("country-api", "Production"),
        "country.selection.change");
```

The mapped workload with `Country.Read` can reach the first operation. The second
returns native forbid (normally 403), and its body does not run. Resource-specific
operations must resolve and authorize the actual resource before executing it;
the complete target must not be assembled from dimensions of unrelated grants.

## Register an outgoing workload

The receiving API does not need this registration. An outgoing worker selects
its own outbound profile and a REST client in its existing host:

```csharp
using CIS;
using CIS.RestClient;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;

LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
var policy = new SecurityOptions { PolicyVersion = "worker-policy-v1" };
policy.Profiles.Add(new SecurityProfile("workload", SecurityProfileKind.EntraWorkloadIdentity,
    SecurityProfileRoles.Outbound,
    outboundIdentity: new SecurityLocalIdentityBinding(local.Application, local.Environment,
        new SecuritySubject(issuer, callerObjectId)),
    allowedOrigins: ["https://country-api.example.test"]));
services.AddCisSecurity(policy);
services.AddCisEntraSecurity("workload", _ => new EntraSecurityOptions
{
    TenantId = tenantId,
    Outbound = new EntraWorkloadOptions
    {
        ClientId = callerClientId,
        ServicePrincipalObjectId = callerObjectId,
        FederatedSubject = "system:serviceaccount:country-demo:country-worker",
        FederatedTokenFile = projectedAssertionPath,
        Scope = "api://" + apiClientId + "/.default"
    }
});
services.AddCisEntraRestClient(new RestClientOptions("country-api")
{
    BaseUrl = "https://country-api.example.test"
}, "workload");
```

The canonical local application/environment must be known and match its explicit
binding. `unknown` is not permission to activate a relationship. Declare inbound
and outbound together only when both roles are needed, then supply both sets of
inputs and the exact receiving scheme.

The wrapper derives a `RestSecurityBinding` from the actual root
`SecurityPolicySnapshot`; it does not trust a metadata-only transport binding.
An optional borrowed transport factory receives that exact binding and root.
Use `RestHttpTransport.CreateSecurityBound` with the root's exact `IMeterFactory`
and, when needed, `RestHttpServerTrust` for explicit private server roots.
Hostname/server-usage checks remain native. The application drains the client
host before disposing borrowed transports; no arbitrary handler or accept-any
certificate callback is exposed.

Resolve the keyed `IRestClient` normally and call its existing methods. The final
authentication hook obtains an SDK token inside the REST operation's existing
deadline/cancellation budget, after the final destination is known. It writes
the bearer credential only for the profile's HTTPS origins. Authentication
suppression/replacement, TRACE and destination escapes are rejected by the bound
REST path. No client secret or `DefaultAzureCredential` chain is used.

## Acquisition, status and stopping

Registration and constructors perform no credential acquisition or network
checks. Existing sequential Startup creates one reused
`WorkloadIdentityCredential` per selected outbound profile. The supported SDK
owns its cache, assertion reads and renewal. CIS has no separate token cache,
minting timer or refresh-on-denial loop. A known expired SDK result is rejected
before dispatch; its lifetime is not extended by the inbound clock-skew setting.

An outbound profile initially reports unavailable until acquisition establishes
a usable credential. `RequiredForReadiness` is explicit and defaults false. If
selected, the adapter warms up under Startup's token, owns one critical existing
Healthcheck condition and requires the existing Heartbeat owner factory. A known
provider failure leaves the host running and that condition unhealthy. It does
not become a permission denial or activate another credential profile.

For required outbound credentials, the existing Heartbeat callback checks whether
a usable SDK result has been established and whether its last observed expiry
has passed. When a check is needed, it awaits `GetTokenAsync` on the same reused
credential. The SDK may return its cached token or obtain a replacement; CIS
does not invalidate that cache or retain a second token cache. The callback can
recover from a temporary Startup failure or idle expiry while the facade is
rejecting business requests with 503. No business endpoint or host restart is
needed to make recovery possible.

Expiry of the **last observed token** does not by itself establish that the
provider cannot supply a usable replacement. The existing availability state is
retained while the check runs. A successful current SDK result establishes
Available; an actual known acquisition failure establishes Unavailable, or
Expired when the SDK actually returns an expired token. The reported expiry
remains the last observed credential's timestamp and can be past while a new
availability check is pending. Every outgoing operation still rejects an
actually expired SDK result before dispatch.

The Heartbeat observation cadence defaults to one second and remains configurable
from ten milliseconds through one minute. Recovery acquisitions are coalesced and
spaced by at least the larger of one second and that cadence, measured with the
monotonic clock. A 10ms observation interval therefore cannot create a 100-per-
second provider retry loop. Foreground acquisition and recovery use one serialized
SDK boundary, with each REST call's existing cancellation/deadline budget.
Optional profiles install no periodic recovery owner and do not affect aggregate
readiness. Existing `/_health` reports application health; it is not redefined as
an independent process-liveness endpoint. Deployment liveness can use a separately
configured TCP probe.

`ISecurityCredentialControl`, keyed by profile alias, supports a source recheck.
For an enabled Entra outbound role this joins a bounded SDK availability check;
concurrent requests join the same in-flight work. It does not promise a new
token, force an SDK renewal, clear its cache or replay an operation. A check
inside the minimum spacing may complete without another SDK call. A joining
caller's cancellation does not cancel another caller's shared acquisition or
complete its wait before actual settlement. After successful shared settlement,
each cancellable caller checks its own token; an original shared SDK or cleanup
fault remains primary. Receiving-only
profiles perform no outgoing check. A borrowed **typed**
`WorkloadIdentityCredential` factory is a trusted native integration seam, invoked
only at Startup. The supplying application owns that credential. Application
cutoff closes admission and cancels SDK work; disposal joins Startup, in-flight
recovery, all admitted SDK calls and the existing Heartbeat owner before retiring
the condition and owned resources. An SDK operation that ignores cancellation
must settle before that cleanup can complete. The reference
deployment uses the adapter's explicit real SDK construction; it never selects
an unrestricted `TokenCredential` or operator fallback.

The pinned stable SDK keeps its experimental Kubernetes proxy switch internal
and false by default; proxy environment configuration is consulted only when
that switch is enabled. This adapter constructs ordinary explicit workload
options and does not enable that preview path. See the pinned
[options source](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Core_1.53.0/sdk/core/Azure.Core/src/Identity/Credentials/WorkloadIdentityCredentialOptions.cs)
and [credential selection](https://github.com/Azure/azure-sdk-for-net/blob/Azure.Core_1.53.0/sdk/core/Azure.Core/src/Identity/Credentials/WorkloadIdentityCredential.cs).

On `ApplicationStopping`, admission closes and owned cancellation is signaled.
The adapter joins actual SDK work and its metadata callback before disposing
owned state. Caller cancellation does not certify settlement of an SDK operation
that has not returned. A cancellation-callback fault remains observable after
cleanup; borrowed SDK transports and credentials are not disposed by the adapter.

Known failures use REST's existing Authentication / `client.authentication_failed`
result, with typed `SecurityFailure` in its context. Known invalid provider codes
are bounded/allowlisted; raw SDK exception messages and token-endpoint responses
are not copied into results or logs. A token-endpoint rejection is not fabricated
as an HTTP response from the protected API. Native receiver authentication failures
normally challenge with 401; a valid caller without a complete grant normally
receives 403. Unexpected programming faults, caller cancellation, total deadlines
and host stopping preserve their existing distinct contracts. Neither 401 nor
403 triggers an automatic replay.

Native metadata discovery/key refresh remains handler-owned. Usable cached keys
or an SDK-cached valid token may survive a provider outage. Missing required
validation evidence still fails closed. Removing a role does not guarantee
immediate invalidation of an already issued token.

## AKS reference deployment

These steps are for an operator with separately authorized infrastructure; they
were not executed by this implementation guide.

1. Enable the AKS OIDC issuer and workload-identity feature. Record the cluster's
   exact OIDC issuer URL.
2. Create/select a dedicated Entra application or user-assigned managed identity.
   Record tenant ID, client ID and service-principal object ID separately.
3. Register the receiving API, its v2 audience/application ID URI and an
   application-only `Country.Read` app role. Assign that app role to the workload
   service principal against the API service principal. Include `idtyp` in access
   tokens. Do not substitute delegated OAuth scopes for app roles.
4. Create an explicit federated credential with the cluster issuer, exact
   `system:serviceaccount:country-demo:country-worker` subject and
   `api://AzureADTokenExchange` audience.
5. Deploy the dedicated service account/pod, with the workload-identity webhook
   enabled for that pod. Read only its projected assertion path; do not copy it
   into checked-in configuration or logs.

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: country-worker
  namespace: country-demo
  annotations:
    azure.workload.identity/client-id: "<workload-client-id>"
    azure.workload.identity/tenant-id: "<tenant-id>"
---
apiVersion: v1
kind: Pod
metadata:
  name: country-worker
  namespace: country-demo
  labels:
    azure.workload.identity/use: "true"
spec:
  serviceAccountName: country-worker
  containers:
    - name: worker
      image: "<your-built-countrydata-caller-image>"
```

The webhook supplies `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and
`AZURE_FEDERATED_TOKEN_FILE`; feed those explicit values into trusted provider
setup and verify they match the intended mapping. The pod image, API address and
application configuration are deployment values. This YAML does not provision
Entra registrations, grant assignments, federation or the protected API.

For AC-02, start the receiving API and the deployed caller using their actual
selected APIs/configuration. Record a real permitted read, a valid-but-ungranted
change, and the absence of the change effect. Remove the federated relationship
or use a wrong assertion mapping to observe acquisition failure. Exercise wrong
audience and expired/invalid credentials at the receiving boundary; preserve
actual protocol results. Restore the intended configuration through the same
authorized deployment process. Role/mapping removal requires replacing affected
policy snapshots and accounting for already issued token lifetimes. Delete only
the dedicated deployment resources/assignments/federation owned by this example.

The implementation's native signed-JWT tests validate real signature/issuer/
audience/lifetime/app-only checks and complete grants. SDK transport tests execute
the actual WorkloadIdentityCredential, projected-file read, SDK cache and final
REST hook over real local TLS. Their token endpoint is controlled test transport;
they are **not AKS/Entra federation evidence**. AC-02 remains explicitly unverified
until suitable authorized infrastructure supplies the deployment proof above.

Primary platform references: [AKS workload identity](https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview),
[application permissions](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow),
[deterministic/reused credentials](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices)
and [native JWT validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
