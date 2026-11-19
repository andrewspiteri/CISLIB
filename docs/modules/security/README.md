---
title: Application identity and security
type: document
status: draft
cis:
  stable_id: cislib.document.modules.security.core
---

# Application identity and security

`CIS.Security` contains the shared identity, grant, failure, audit and status
contracts in `CIS.Core`. `CIS.Security.Hosting` integrates those contracts with
native ASP.NET Core authentication and authorization. The optional Entra and
certificate adapters own their actual credential/trust implementation; selecting
Core services does not choose an authentication provider or contact one.

The [portable v1 contract](../../../contracts/security/README.md) and
[RM-01 input](../../../planning/security/README.md) define the accepted behavior.
Implementation evidence belongs to the current change's verification record.
Local policy/projection fixtures do not prove native JWT/TLS validation, AKS
federation or cert-manager issuance/renewal.

## Select a complete startup policy

`SecurityOptions` contains selected `SecurityProfile` entries, registered
capabilities and complete `CapabilityGrant` entries. Each profile declares its
inbound, outbound or both roles. An inbound profile names one native scheme and
trusted issuer-qualified identity mappings. An outbound profile declares the
exact canonical local application/environment-to-credential relationship and
nonempty HTTPS origins. Missing unused outbound inputs do not invalidate a
receiving-only integration.

Outgoing activation rejects canonical `unknown` application/environment values or
a mismatched local mapping. Configure the existing environment contract before
its first use; security does not add a reset or reinterpret `EnvironmentKind` as
a development setting.

Call `services.AddCisSecurity(options)` for already detached setup. It captures
the policy immediately, with default profile capacity 16. A deferred factory
overload captures during ordinary DI construction after initial Configuration
preparation. For a retained Configuration root, use the reader-backed overload:

```csharp
services.AddCisSecurity<ApplicationSecuritySettings>(settings => MapSecurityPolicy(settings),
    profileCapacity: 16);
```

Here `ApplicationSecuritySettings` is the application's existing retained
`IConfigurationObject`, using the supported passive, publicly settable
configuration shape. The application-owned `MapSecurityPolicy` function builds
`SecurityOptions` and immutable profile/grant objects from those loaded DTOs;
it performs no I/O. Immutable runtime models are not a new storage format or an
extension to Configuration's mapper. The generic overload resolves the loaded root and `IConfigurationReader`, then
copies and validates the entire immutable snapshot **inside** `Read`. The plain
deferred factory must return detached setup; do not return a mutable retained
root and expect a later copy to be atomic.

The public immutable `SecurityPolicySnapshot` exposes the effective profiles,
capabilities, grants and `PolicyVersion`. Provider startup can inject it and call
`GetProfile(alias)` without copying registration-time mutable roots. No provider
constructor or registration performs credential I/O. Initial credential loading
and warmup belong to the existing sequential startup owner.

The common validator is enrolled as a singleton `IStartable` before application
participants. The facade already supplies sequential Startup. Direct native
hosts install `AddCisStartup` in its documented position after their module
bridges, retain DI scope/build validation and disable concurrent hosted start/stop.
Read the [Startup guide](../startup/core.md) before composing those lifetimes.

An explicit non-secret `PolicyVersion` is retained. When omitted, C# generates
a SHA-256 version from its deterministic non-secret policy representation.
Neither a configuration event nor a leaf reload mutates this snapshot. Restart
or replace affected instances to adopt trust, mapping, grant or profile changes.

Default limits are 16 profiles, 256 registered capabilities and 4,096 grants.
`profileCapacity`, `MaximumCapabilities` and `MaximumGrants` allow deliberately
larger finite configuration, up to 4,096, 65,536 and 1,048,576 respectively; the host's existing metrics/resource ceilings must
still fit. A mapping/credential has at most 256 role identifiers, a profile at most
4,096 identity mappings and 64 destination origins, and one check at most 256
required capabilities. Oversized/malformed input is rejected, never truncated
into a different subject. Application/environment keep the existing 255-Unicode-
scalar bounds and ordinal case-sensitive comparison.

## Integrate the selected native scheme

The platform adapter registers its named native handler and its scoped projector:

```csharp
services.AddCisSecurityProjection<MyIdentityProjector>("selected-profile");
```

This names an application/provider implementation of `ISecurityIdentityProjector`;
it is not a built-in authentication scheme. `ProjectAsync` receives the actual
selected-scheme `AuthenticateResult` only after native authentication succeeds.
The result's ticket scheme must match the configured scheme exactly. The
projector checks provider-specific restrictions and returns bounded
`AuthenticatedSecurityIdentity` facts or a known `SecurityProjectionResult.Rejected`.
Core then applies the exact issuer/subject/actor and expected client-application
mapping before establishing caller scope. Unrelated principals are never merged.

`SecurityProfileKind.Native` is the explicit extension point for an application-
owned native integration, including independently validated human actors. It
does not deliver another authentication engine or loosen the three workload
reference profiles. Workload profile kinds reject projected human/delegation
identities. Entra maps require the expected client-application identifier and
an actual application role for each granted capability.

An application certificate does not prove a human or a replica. Reported instance
metadata is kept separately from independently verified evidence. Header values
and constructed/deserialized caller objects never replace credential identity.
Core stores no unrestricted claims dictionary or HTTP context in `CallerContext`.

## Explicit host stages and surface selection

For native hosts, install routing and trusted proxy handling, then call
`UseCisSecurityAuthentication()` before operational branches. Call
`UseCisSecurityAuthorization()` after the existing business readiness gate and
before endpoint execution. The facade invokes these two stages when security was
explicitly registered. Existing optional operational integrations run between
projection and Healthcheck/readiness. Both stages invoke native middleware
explicitly; their order does not depend on minimal hosting's implicit insertion.

Common registration uses native authentication Core services, encoders, routing
and authorization. It adds no Data Protection key ring or new hosted background
owner. JWT/certificate adapters add their schemes through the existing native
`AuthenticationBuilder`; they do not need the full authentication convenience
registration that implicitly installs Data Protection services.

Endpoint capability metadata explicitly selects its profile. Operational surfaces
without that metadata select no profile unless the application configures
`AddCisSecurityWeb(options => options.SelectProfile = ...)`. This platform selector
must use trusted actual listener/binding data, such as the configured local port,
never the request's Host header. A configured selector returning null establishes
no caller, and a selector/endpoint profile mismatch denies. Merely registering one
profile does not establish caller authority for every operational surface.
The selector delegate is captured once for the pipeline; close it over immutable
trusted binding policy rather than a changing Configuration object.

Detailed health can use the established caller and its existing authorization
predicate. Probes and Metrics retain their own access rules. For mTLS, ordinary
probes need a separate restricted operational listener or a supported certificate
probe; HTTP middleware cannot exempt a probe from a TLS client-certificate
requirement. The operational listener must provide no business-route bypass.

Register a custom `IAuthorizationMiddlewareResultHandler` **before**
`AddCisSecurity`. Core preserves it as a private keyed native descriptor with the
same instance/factory/type lifetime and ownership, observes the final composite
result, and delegates challenge/forbid/next behavior. A later replacement fails
closed during pipeline/startup validation instead of silently dropping security
observations. Extra policies may not introduce another authentication scheme.
Generated `cis.security.*` profile policies retain their actual complete-grant
requirement and configured additional native requirements. Core verifies its own
check completed on the current actual target before allowing protected execution;
an earlier check or a late same-scheme policy replacement cannot supply that proof.
Custom native policy providers delegate to, or combine with, the generated profile
policy rather than rebuilding a weaker definition. Caller-added native checks
remain valid when the selected profile policy is retained.

## Authorize the actual operation

Capabilities are registered lowercase ASCII names, for example `country.read`.
On a mapped native endpoint, declare one complete set and one actual target:

```csharp
web.MapGet("/countries", ReadCountries)
    .RequireCisCapabilities("selected-profile",
        new CapabilityTarget("country-service", "Production"), "country.read");
```

The route delegate, service identity and profile are application-owned values in
this API example. A resource resolver overload receives `HttpContext` and the
operation cancellation token and returns the authoritative `CapabilityTarget`.
If resource data is not available until the body loads it, perform the final
resource-specific imperative check before its protected effect:

```csharp
AuthorizationDecision decision = await authorizer.AuthorizeAsync(
    ["country.read", "country.select"],
    new CapabilityTarget("country-service", "Production", "country", country.Code),
    cancellationToken);
```

`authorizer` is the scoped `ICapabilityAuthorizer`; it reads the already established
caller. It does not accept caller data as a substitute credential. All requested
capabilities must pass against this same target, together with the profile's
`AdditionalAuthorizationPolicy` when configured. That optional named native policy
is applied to both endpoints and imperative checks. Other endpoint-native policies
also retain their normal combined requirements.

One whole subject or mapped-role grant must match the capability, target
application, target environment and resource shape. `AllOfType` remains within
one explicit application/environment/type; it is not an administrator wildcard.
Unknown capabilities and missing mappings deny. One complete declaration per
endpoint prevents repeated metadata from silently replacing an earlier required
capability set; combining anonymous access with protected metadata fails closed.

Native `IAuthorizationService` remains the evaluator. Core's shared requirement
handler supplies complete-grant semantics; its imperative wrapper does not add
a second evaluator or timeout owner. Cancellation is checked before/after native
evaluation, passed to Core handlers through the resource, and never substitutes
a canceled wait for actual evaluator settlement.

## Scope, outcomes and safe observations

`ICallerContextAccessor.Current` is read-only, scoped and absent without trusted
projection. Disposal/failure restores the previous scope. There is no ambient
`AsyncLocal` request authority for background work. A trusted native host can use
`SecurityHostContextScope.CreateAsync(root, profile, validatedNativeResult)` to
create an explicit independent scope from already validated native evidence.
Check its `Caller`/`Failure`, use its `Services`, join owned work and await disposal.
This is a hosting integration API, never an endpoint accepting serialized caller
contexts. Retained caller/audit data remains provenance, not delegated authority.

`SecurityFailure` publishes stable `security.*` categories, safe text and optional
actual protocol facts. `RestAuthenticationContext.FailWithSecurity` retains the
existing REST outer kind `Authentication` and code `client.authentication_failed`.
`RestResultContext.SecurityFailure` is additive detail; `WithSecurityFailure`
copies failed context without changing its established constructor. Arbitrary
HTTP 401/403 or TLS failures do not acquire an invented precise diagnosis.
Caller cancellation, deadlines, stopping and programming faults retain their
existing distinct behavior. Credential recheck does not trigger hidden replay.

`ISecurityAuditSink` receives the final composite authorization result once and
the operation owner's separate `SecurityOperationAudit`. An allowed operation
that later fails is not successful completion. Awaited audit-hook faults remain
observable; they are not converted into ordinary denial or a successful receipt.
The hook supplies no durable store/queue/delivery guarantee. An application that
requires durable audit selects that explicit operation policy separately.

Adapters publish `SecurityProfileStatus` through `ISecurityProfileStatusReporter`;
`ISecurityProfileStatusSource` copies bounded owned state without I/O. Only known
registered aliases are admitted. The optional `ISecurityCredentialControl`
requests a coalesced source recheck; it does not promise a newly minted token or
certificate. Credential acquisition/renewal and real startup/stopping stay with
their provider/host owners, including actual transport/body drain.

When the owning Metrics collector explicitly selects security Core sources, the
module attaches exactly three instruments: composite authorization decisions,
material certificate reload transitions and remaining active leaf seconds.
Counters use only `outcome`/registered `profile`; the gauge uses only `profile`,
performs no source I/O, omits unavailable leaf evidence and clamps expiry to zero.
The collector retains central application/environment labels and existing global
limits. Disabled module collection leaves this instrumentation inert; metric/log
failures cannot change authorization or successful business results. Native HTTP
duration/attempt observations are reused rather than duplicated.

Each provider guide and the current CountryData handoff must identify its actual
native evidence. Real AKS federation and cert-manager issuance/mount/renewal need
authorized infrastructure; local signed-token/TLS/file tests cannot replace those
acceptance requirements or establish complete RM-01 delivery.
