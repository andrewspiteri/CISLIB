# CISLIB RM-01 — Application identity and security

## Implementation specification

| Document field | Value |
|---|---|
| Version | 1.0 |
| Date | 19 November 2026 |
| Status | Specification for implementation; this document does not claim that the feature has been implemented or deployed |
| Initial implementation | C# on the repository's supported .NET 10 baseline |
| Portable scope | Identity, trust, capability, lifecycle and result semantics for future language bindings |
| Repository reviewed | `andrewspiteri/CISLIB`, `main` at `31739a247efba609f27dd50caab1312bb775bd79` |
| Roadmap dependencies | Existing application, configuration and HTTP foundations |
| Related roadmap work | RM-07 management contracts may proceed alongside this work |

**Purpose.** Provide one consistent authentication and authorization integration for applications, management APIs and later service communication. Reuse CISLIB's existing application/environment identity, hosting, configuration and HTTP ownership. Integrate directly with native .NET security facilities, while defining a common contract that another language can implement using its own supported libraries.

**Normative language.** MUST, MUST NOT and REQUIRED state implementation requirements. SHOULD states a preferred implementation with a documented reason required for deviation. Examples illustrate the specified behavior; deployment identifiers are sample values. New API names and packages below are specification proposals to implement, not claims about APIs already present in the reviewed repository.

## 1. Decisions and delivery boundary

### 1.1 Three required profiles

| Profile | Intended deployment | Authentication mechanism | Credential owner |
|---|---|---|---|
| `EntraWorkloadIdentity` | Azure Kubernetes Service using Microsoft Entra ID | Entra access token obtained through AKS workload identity; API validates bearer token | Azure Identity SDK and the configured workload federation |
| `ClientCertificate` | Kubernetes or other hosting independent of Entra | Direct mutual TLS with an application client certificate | Deployment PKI; cert-manager is the optional Kubernetes issuance/renewal integration |
| `LocalDevelopment` | A developer machine or explicitly configured private development network | The same certificate integration, using an isolated development CA and per-application credentials | A small local setup/reset tool and the certificate adapter |

All three profiles MUST be usable in the first implementation. Interfaces, simulated successes or a production provider deferred behind a stub do not satisfy this requirement.

The profiles share identity mapping, capability authorization, caller context, result semantics and documentation conventions. They do not require a common credential representation: a client certificate is used by TLS, whereas a bearer token is attached to an HTTP request.

Installing a package MUST NOT select a profile. Each outbound client and inbound protected surface selects its permitted profile explicitly. No failed production authentication may fall back to another profile or to development credentials.

Profile activation declares inbound, outbound or both roles. Validate only the inputs required for enabled roles: a receiving API does not need an outbound workload credential, and an outbound-only worker does not need an inbound authentication scheme.

### 1.2 Required outcomes

A representative application MUST be able to:

1. Authenticate to a protected service with each profile.
2. Perform an operation for which its authenticated identity has a complete capability grant.
3. Receive a defined denial for an otherwise valid identity without the required grant.
4. Handle unavailable, invalid and expired credentials without executing protected work.
5. Acquire replacement credentials and adopt renewed certificates without changing the application's business code.
6. Propagate a trusted, bounded caller context into the operation and its audit hook.
7. Run the local development example without an Entra tenant, Kubernetes cluster or hosted identity provider.

The receiving service MUST enforce its own trust and grants. A client's local configuration, claimed application name or expectation of permission cannot grant access.

### 1.3 Included and deferred work

Included: workload authentication; native authentication/authorization integration; trusted application/environment mapping; honest instance provenance; capability checks; credential acquisition and renewal hooks; direct mTLS; local credential setup; lifecycle integration; bounded diagnostics; consumer documentation; focused tests; a checked-in security skill for future agents.

A validated human principal can be projected into the shared caller model through an explicit native integration. Human sign-in screens, account storage, OIDC browser flows, consent, multifactor authentication and delegation/token-exchange flows are outside the three reference workload profiles. An application certificate MUST NOT be presented as proof of a human operator.

This delivery does not create an identity server, certificate-authority service, token issuer, service mesh, general policy language, rule engine, separate HTTP stack, background scheduler, durable audit store or central management dependency. It does not implement other language bindings. It preserves the portable behavior they will need.

RM-07 may later supply trusted identity/grant configuration and consume audit/security status. RM-01 MUST operate using existing trusted configuration before that management deployment exists.

## 2. Repository integration and compatibility

### 2.1 Existing foundations

The reviewed repository already provides the following integration points. The implementation agent MUST inspect the current checkout and reconcile later changes before editing.

| Foundation | Existing contract and required use |
|---|---|
| Identity | Static `CIS.EnvironmentOptions.ApplicationName`, `EnvironmentName` and `InstanceId`; project these values instead of configuring a second identity source. [R2] |
| Application hosting | `CISApplication` and its native host/integration lifecycle; retain one application service provider. [R3] |
| Configuration | Existing configuration sources and validation; use trusted profile/grant snapshots and references to credentials. Configuration storage scope remains separate from application identity. |
| Outgoing authentication | `RestClientOptions.Authenticate` and `RestAuthenticationContext.Fail(...)`; reuse the final authentication callback for Entra. [R4] |
| HTTP ownership | `RestHttpTransport` owns a native `SocketsHttpHandler`, connection pools and response leases. Add the constrained certificate seam here. [R5] |
| Request safety | The existing finalized-request `AllowedOrigins` guard; require it for all security-bound clients, including mTLS. [R6] |
| Native security | ASP.NET Core authentication schemes and `IAuthorizationService`; maintain a thin CIS integration over them. [S1][S2] |
| Testing and metrics | Existing `CIS.CommonTesting`, native test workflow and Metrics contracts; reuse their conventions. [R7][R8] |

`EnvironmentOptions` is resolved once. Its `InstanceId` identifies the local process instance; it does not establish remote attestation. `EnvironmentKind` describes Console/Service/Web/Function and MUST NOT be interpreted as Development/Production. The native host environment may be used as an additional development gate, but MUST NOT replace canonical `EnvironmentName`. [R2]

### 2.2 Package boundary

| Location | Responsibility |
|---|---|
| Existing `CIS.Core`, new `CIS.Security` namespace | Portable caller/capability/result contracts; native authorization bridge available through existing framework support; common configuration validation; constrained extension to the existing REST transport |
| New optional `CIS.Security.Entra` package | Entra workload acquisition with Azure Identity and native JWT bearer integration |
| New optional `CIS.Security.Certificates` package | Native certificate authentication, certificate source/loading, renewal adoption and mTLS transport binding |
| Development tooling and the certificate package | Local CA/client/server credential setup and reset; no separate development authentication engine |
| Existing `CIS.CommonTesting` | Small reusable security test doubles where consumers need them; no production bypass |

Certificate-only consumers MUST NOT acquire Azure SDK dependencies. Core MUST retain its approved runtime dependency closure unless a separate necessary change is explicitly handled through the repository's existing decision process. Optional security packages require compatible, centrally pinned dependencies, source mappings and generated restore locks. Do not guess future package versions or treat a Microsoft package prefix as automatic dependency approval. The existing `Microsoft.AspNetCore.App` framework reference can be reused. [R9]

Registration and constructors MUST remain free of credential network calls, service-provider construction and premature service resolution. New integration must preserve existing application, configuration, logging, HTTP and shutdown behavior.

## 3. Identity and trust model

### 3.1 Local identity

The local application's application, environment and instance values come only from the existing environment contract. A security-facing immutable projection may expose them without duplicating storage or resolution.

For an active outbound security binding, configuration MUST map those local application/environment values to the intended credential identity. An unknown, missing or mismatched outbound mapping fails profile validation. An inbound-only service uses local identity for its target/hosting context and validates remote callers; it does not need its own outbound application credential. The existing environment resolver's defaults remain unchanged; a value such as `unknown` does not authorize a development mode or a production trust relationship.

The authenticated identity a remote service sees is established by the credential and that service's trusted mapping. It need not be the literal text of the local application's metadata.

### 3.2 Remote identity mapping

An inbound application identity MUST be constructed only after:

1. Native authentication validates the presented credential using the selected profile.
2. The validated issuer/trust domain and stable provider subject match an explicit trusted mapping.
3. The mapping identifies the permitted CIS application/environment and applicable grant sources.
4. Any required application-role, certificate-usage or profile-specific restrictions pass.

Issuer qualification is mandatory. An identical subject string from a different tenant, issuer, CA or development trust domain is a different identity.

For certificates, issuer identity means the configured trusted chain/issuer relationship, not merely a caller-provided issuer distinguished-name string. For Entra, use a validated tenant/issuer and stable service-principal identity; client application identifiers are checked consistently with the accepted token version.

Application, environment, user or instance headers MUST NOT override authenticated identity. Forwarded tracing and correlation data remain metadata. The same rule applies to serialized caller contexts: receiving a context object is not authentication.

### 3.3 Caller context

The new `CallerContext` contract MUST be immutable and bounded. It contains:

| Field | Meaning |
|---|---|
| Profile and authentication method | The configured integration that actually authenticated the caller |
| Subject | Stable provider-qualified subject |
| Actor kind | Application or User |
| Application/environment | Trusted mapping for the calling application where established |
| Human subject | Optional independently validated human actor; never inferred from an application credential |
| Instance evidence | Optional verified instance binding; separately identified reported instance metadata |
| Authentication evidence | Validation time and relevant credential expiry/identifier, without credential material |
| Policy version | The trusted mapping/grant snapshot used for the operation |
| Verified delegation | Optional provenance only when the integration actually validates delegation |

The context MUST NOT include access tokens, private keys, passwords, an unrestricted claims dictionary, mutable `ClaimsPrincipal` state or a retained `HttpContext`.

Shared AKS service-account identities and shared application certificates ordinarily identify an application deployment, not a particular replica. A forwarded `InstanceId` therefore remains `Reported` unless the credential/session is independently bound to that instance. The first implementation MUST represent `NotProvided`, `Reported` and `Verified` distinctly; it MUST NOT invent an instance-attestation service.

Use a scoped, read-only `ICallerContextAccessor`. Only the authentication/host integration establishes the trusted scope. Concurrent requests MUST remain isolated. Disposal restores the previous scope, including on failure. Background work establishes an explicit host workload context or uses a separately designed delegation integration; it MUST NOT inherit ambient request authority by retaining a context or `HttpContext`.

A provenance record attached to a job explains who initiated it. It does not, by itself, delegate that person's authority to later execution.

## 4. Capability authorization

### 4.1 Capability and target

A capability names an action, for example `configuration.read`, `configuration.update` or `management.status.read`. A target names what that action affects:

- Application.
- Environment.
- Optional resource type and resource identifier.

Application/environment refer to the target being accessed. They may differ from the caller's application/environment only when an explicit grant permits that access.

Capability names MUST be registered, at most 128 characters, and use lowercase ASCII segments separated by dots. Segments begin with a letter and otherwise contain letters, digits or hyphens. Unknown names deny access. No implicit wildcard or administrative capability is defined.

Existing application/environment names retain their current format and bounds. Security matching uses ordinal, case-sensitive comparison of the resolved values. Resource identifiers and provider subjects are opaque, bounded values; the integration MUST reject malformed/oversized input rather than silently truncate, trim or case-fold it into a different identity.

### 4.2 Complete grants

A `CapabilityGrant` contains a complete relationship:

> subject or mapped role + capability + target application + target environment + resource selection.

A resource selection is one of:

| Selection | Match |
|---|---|
| `TargetOnly` | An operation on the application/environment itself, with no resource type/id |
| `ExactResource` | The exact configured resource type and identifier |
| `AllOfType` | Any identifier of one explicitly configured resource type |

Application and environment remain explicit in all selections. `AllOfType` does not mean all applications or environments. RM-01 does not add arbitrary scripts, regular expressions or permission inheritance.

**An operation is allowed only if one complete applicable grant matches the capability AND target application AND target environment AND every resource constraint.** Matching fragments from different grants MUST NOT be combined.

For example:

| Grant | Capability | Application | Environment |
|---|---|---|---|
| A | `rules.read` | ApplicationA | Development |
| B | `rules.publish` | ApplicationB | Production |

These grants permit neither `rules.publish/ApplicationA/Development` nor `rules.read/ApplicationB/Production`.

For an operation requiring several capabilities, every required capability MUST pass against the same authoritative target. Multiple complete grants may satisfy different required capabilities; their scope dimensions cannot be recombined.

### 4.3 Grant sources and defaults

Initial grants come from trusted configuration and explicit mappings of validated provider roles to complete grant templates. Entra application roles and delegated human scopes have different meanings and MUST NOT be treated as interchangeable strings. The workload reference requires the configured application role for each mapped capability; an authenticated, roleless application receives no capability by default. [S5]

Certificate grants come from the trusted issuer/subject mapping. A CA signature alone does not grant a capability. Local application names, certificate display names and caller-supplied capability lists do not create grants.

Missing identity mappings, unknown capabilities, incomplete targets, failed native requirements and unavailable required validation all deny. There is no default administrator, implicit same-environment permission or fail-open behavior.

The receiver derives the target from route binding and authoritative resource data. If resolving that data requires a preliminary check, the final resource-specific check MUST still authorize the actual object before protected work begins. Do not authorize one target and execute against another.

### 4.4 Native .NET evaluation

Use native `IAuthorizationService`, authorization policies, requirements and handlers. The new `ICapabilityAuthorizer` delegates to the same handler used by endpoint authorization; it MUST NOT implement a second evaluator. Resource authorization uses the native resource-based pattern. [S2][S3]

Endpoint metadata declares required capabilities. Imperative checks supply a typed target for operations that cannot resolve their resource before endpoint execution. Native policies may add additional requirements; all required checks must pass.

The native `IAuthorizationService.AuthorizeAsync` API has no cancellation-token parameter. The CIS wrapper checks cancellation before/after evaluation and makes the token available to its own handlers through the operation context when needed. It MUST await actual native evaluation settlement rather than abandon the task to simulate cancellation or create another timeout owner. [S16]

The portable `AuthorizationDecision` reports Allow or Deny plus a stable reason. Unexpected programming faults remain faults. It MUST NOT return a partially allowed capability set as authorization to execute a multi-capability operation.

## 5. Shared contracts and configuration

### 5.1 Required consumer responsibilities

The C# binding MUST expose the following small set of responsibilities using CIS-owned types. Exact signatures follow repository conventions and MUST be published in the implemented contract.

| Contract | Required behavior |
|---|---|
| `CallerContext` / `ICallerContextAccessor` | Immutable trusted identity; read-only scoped access; absent context is not authenticated |
| `CapabilityTarget` / `CapabilityGrant` | Explicit target and complete scoped grants |
| `ICapabilityAuthorizer` | Asynchronous capability check with cancellation, backed by native authorization |
| `AuthorizationDecision` | Allow/Deny and bounded stable reason; no provider-specific exception contract |
| `SecurityFailure` | Known category, stable code, safe message and optional actual transport/status information |
| Credential profile/status contract | Named profile, Available/Unavailable/Expired/Stopping state, safe reason and known expiry |
| Outbound integration | Bind a named security profile to an existing named REST client and, when needed, its native transport |
| Audit integration hook | Trusted caller, requested action/target, authorization decision and separate operation outcome |

The portable contract MUST NOT expose `ClaimsPrincipal`, `HttpContext`, Azure SDK types, `X509Certificate2`, `HttpClient` or native handler types. Platform-specific registration APIs and adapter internals may use the native types they integrate with.

Do not force all profiles behind a public method that returns a token string. Acquisition is protocol-specific and remains adapter-owned. The common hooks define cancellation, availability/status, expected failure, source recheck and lifecycle ownership. A source-recheck request is not a promise to mint a new token or renew a certificate: the SDK/issuer owns those operations.

Provide a small versioned portable schema/fixture set for caller provenance, grant matching and result categories under `contracts/security/`. These fixtures do not turn caller-context serialization into a wire authentication mechanism.

### 5.2 Configuration ownership

Use the existing Configuration module. Profile options contain trusted issuer/audience/CA selections, identity mappings, grant templates, destination origins and credential-source references. Do not store private keys, bearer tokens or passwords in ordinary checked-in configuration.

Trust anchors, accepted issuers, identity mappings, capability registrations, grants and profile selection are validated **startup snapshots** in RM-01. A change requires replacement/restart of the affected instance. The instance MUST NOT partially mutate a live policy from an asynchronously changing configuration object.

Credential leaf material is the deliberate dynamic exception: the certificate adapter supports validated reload as defined in section 9. Azure SDK token acquisition/renewal remains SDK-owned. Updating credential material MUST NOT change the profile's trusted subject mapping or grant policy.

Policy versions are deployment-provided non-secret identifiers or deterministic digests of canonical non-secret policy data. They are diagnostic/audit data, not unbounded metric labels.

Startup validation MUST enforce finite configuration. Initial defaults are at most 16 named security profiles, 256 registered capabilities and 4,096 total grants. Limits may be deliberately configured higher within the existing bounded-resource policy; input beyond a configured limit fails validation before activation. Subject/issuer/resource identifiers are limited to 1,024 characters; profile aliases to 64.

### 5.3 Deployment inputs

The inbound and outbound columns apply independently. Enabling both roles requires both sets of inputs.

| Profile | Inbound inputs | Outbound inputs |
|---|---|---|
| Entra | Accepted tenant/issuer; API audience/token version; registered service-principal/client identities; application-role and grant mappings | Workload tenant/client identity; federated Kubernetes subject and projected assertion source; intended API scopes; local identity binding; allowed HTTPS origins |
| Client certificate | Native server TLS identity; trusted client issuer/CA bundle; exact identity URI mappings/grants; expected usage and revocation policy; issuance authorization | Client certificate/key source and expected local identity; client issuer/usage/lifetime policy; server trust policy; renewal source; allowed HTTPS origins |
| Local development | Explicit development activation; canonical development environment; isolated server/client trust and server credential; caller mappings/grants | Explicit development activation; per-application development credential; isolated server trust; loopback or explicitly configured private origins |

Tenant IDs, hostnames, CA choices and production application-role names are deployment values, not unresolved architectural choices. The delivered guides MUST show where each value is obtained and how it is validated.

## 6. Native authentication and hosting

### 6.1 Scheme selection

Each protected endpoint/listener MUST specify its allowed native authentication integration. When a service accepts both bearer and certificates, the reference configuration uses separate listener/hostname bindings for mTLS and bearer traffic. Globally requiring a client certificate would exclude bearer-only clients.

If a consumer deliberately combines schemes on a surface, selection MUST be explicit and deterministic. An unvalidated token header may only select a handler; it cannot establish trust. Reject ambiguous combinations or select one configured identity without merging unrelated principals or grants. Do not rely on a multi-scheme policy that unintentionally unions claims.

Authentication alone does not protect an endpoint. The appropriate native authorization policy and capability requirements MUST also run.

### 6.2 Middleware order

The current facade assembles routing, integration hooks, health handling, the business readiness gate and consumer route setup. The implementation MUST provide the smallest additive hook needed for this explicit order. [R3]

1. Routing and trusted proxy processing, if configured.
2. Native authentication and trusted caller projection.
3. Existing health/metrics branches with their own access rules.
4. Existing business readiness gate.
5. Business authorization and protected endpoint execution.

Detailed health access can use the authenticated caller and its existing authorization predicate. Liveness/readiness probes MUST retain their established semantics and MUST NOT accidentally acquire a global authentication requirement. Preserve the Metrics endpoint's existing access policy.

A mandatory client-certificate requirement runs before HTTP middleware. The mTLS reference therefore exposes ordinary probes through a separate internal operational listener, or uses an explicitly supported certificate-capable probe mechanism. The separate listener exposes only its intended operational endpoints and MUST NOT provide a business-route bypass. Enforce this using the actual listener binding, not an untrusted Host header. Test real TLS and listener behavior; middleware order alone cannot exempt a probe from mTLS.

Do not depend on implicit middleware insertion to establish this security boundary. Test the actual facade path as well as direct native-host integration.

### 6.3 Lifecycle

Profile validation occurs before a profile is activated. Loading local certificate material and any required warmup belongs to existing startup ownership, not DI construction. A network-dependent credential check affects readiness only when configured as an actual required dependency.

A process can remain live while a required credential is unavailable; protected work still fails closed. Readiness MUST distinguish an essential unavailable credential from an unrelated optional integration.

An inbound-only profile reports the availability of its configured receiving integration and trust policy; absence of an unused outbound credential is not a health failure.

On stopping, stop admission, signal owned cancellation, settle credential work, drain transport/response owners and then dispose owned credentials and handlers. Preserve existing REST admission cutoff and Heartbeat/Configuration shutdown ordering. Do not dispose borrowed SDK/transport resources. A cancelled wait is not proof that the underlying operation has finished cleanup.

## 7. Entra Workload Identity profile

### 7.1 Acquisition

Use `Azure.Identity.WorkloadIdentityCredential` explicitly for the AKS reference. Consume the configured tenant/client identity and projected federated-token file through the supported SDK integration. AKS workload identity supplies the Kubernetes-to-Entra federation; CISLIB does not issue or renew that projected assertion. [S4]

Do not use an unrestricted `DefaultAzureCredential` chain in the production reference. A missing workload identity MUST produce a defined failure, not silently authenticate as an operator's CLI account or another machine identity. Reuse SDK credential instances for bounded named profiles and let the SDK manage its token cache and refresh behavior. [S6]

The outbound API resource/scopes are trusted configuration, normally the intended API resource's `/.default` scope for application permissions. They MUST NOT come from an arbitrary request parameter. The API's accepted token audience is configured separately: a scope URI and the token's `aud` value are not assumed to be identical. [S5]

Acquire credentials within the existing REST operation's cancellation/deadline budget. Use the existing final authentication callback after destination and request content are finalized. Add the bearer credential only for the configured HTTPS origins.

CISLIB MUST NOT add a separate token cache, token refresh scheduler or background token-minting loop. The SDK may return an appropriate cached token. The adapter MUST reject a known expired token rather than extend its lifetime or send it under a custom grace period.

### 7.2 Receiving API

Use the native JWT bearer handler with explicit accepted issuer/tenant, audience and supported token version. Validate signature, token lifetime and the relevant native validation parameters. Do not accept tokens merely because they decode successfully. Disable implicit claim-name remapping or otherwise document and test one consistent mapping. [S7]

The initial reference uses one explicitly configured tenant and API audience per scheme. Multi-tenant acceptance requires an explicit allowlist and mappings; it MUST NOT accept any tenant from a generic authority by default.

Native JWT validation clock skew defaults to 30 seconds in the CIS profile and MUST be explicitly configured. A deployment can choose a smaller value; values greater than 120 seconds are rejected by profile validation. This native validation tolerance is not an outbound credential-renewal grace period.

Map the validated tenant/service principal and appropriate client-application claim to the trusted application/environment. Match the configured application roles to complete grants. Never substitute user delegated `scp` claims for application `roles` without a separately configured human/delegation integration.

The workload mapping MUST match the exact configured tenant, service-principal object identifier and token-version-appropriate client application identifier. A user is not a workload merely because a token contains the same role name or client identifier. Reject delegated/user tokens in this scheme, including tokens containing delegated `scp`; enforce `idtyp=app` where that claim is configured. Document the selected app-only checks and their consistency with the registered principal. Human tokens use a separate explicitly configured integration.

Signing-key discovery/refresh remains native-handler-owned. Provider outages may still permit validation with usable cached metadata and keys, or acquisition using a still-valid SDK-cached token. Missing required validation material fails closed. Neither outage handling nor role changes guarantee immediate revocation of an already issued valid token.

### 7.3 Reference deployment guidance

The delivered guide MUST show:

- Enabling the AKS OIDC/workload identity prerequisites.
- A dedicated Kubernetes service account and the exact federated subject relationship.
- The Entra application/managed identity and the intended API application-role assignment.
- Required pod/service-account configuration and the projected token source.
- The protected API's issuer/audience/role mapping.
- The outbound client's API scope and destination allowlist.
- A successful permitted request and a valid-but-unpermitted request.
- Missing federation, wrong audience and expired/invalid credential behavior.

Do not commit tenant secrets or require a client secret for the AKS reference. Deployment examples use placeholders for tenant-specific identifiers and explain their replacement.

## 8. ClientCertificate profile

### 8.1 Direct mTLS and stable identity

Use native TLS client authentication and the ASP.NET Core certificate handler. The handler maps a validated client certificate to a native principal; the CIS integration supplies the additional identity and capability mapping. Certificate authentication occurs at TLS and application-handler boundaries, not through an Authorization header. [S8]

Each workload certificate in the initial adapter MUST include an issuer-authorized stable identity as an exact URI subject alternative name under an organization-controlled namespace. Other subject identity formats are outside this initial adapter. For example:

```text
https://identity.example.test/applications/country-worker/environments/production
```

This is an opaque identity value, not a URL that the service fetches. The trusted mapping contains the exact accepted URI and associated application/environment. Do not parse arbitrary URI fragments into privileges. Multiple conflicting identity SANs are rejected.

Use the trusted issuer relationship plus stable identity as the mapping key. Leaf thumbprints and serial numbers are suitable for credential diagnostics or explicit credential rejection, but MUST NOT be the sole stable grant key because renewal changes them.

### 8.2 Certificate validation

The receiving integration MUST validate:

1. A chain terminating at explicitly configured application trust anchors, using native custom-root trust where applicable.
2. The accepted issuing chain, signatures, constraints and current validity.
3. A non-CA leaf, explicit client-authentication EKU and appropriate digital-signature key usage.
4. The permitted stable identity SAN and trusted application/environment mapping.
5. The selected revocation policy.
6. Current expiry at every protected operation boundary, including operations arriving on an existing TLS connection.

Do not use all system public roots as implicit authority to issue CIS workload identities. Do not accept a missing client-authentication EKU simply because a native default considers an unrestricted certificate suitable for all usages.

Server TLS authentication remains mandatory on outbound calls: validate the server chain and hostname. Client authentication is additional. Neither production nor development may use an accept-any-certificate callback.

Keep server trust separate from the roots permitted to issue client application identities. Outbound server trust uses the native system trust by default, or an explicitly configured private server trust bundle. LocalDevelopment uses its isolated server trust. Implement this within the constrained native TLS options, for example through `SslClientAuthenticationOptions.CertificateChainPolicy`, without bypassing hostname or server-usage validation. [S15]

Do not add a principal/authorization cache in RM-01. Native chain/metadata caches may operate according to their own validity rules, but MUST NOT bypass per-operation leaf expiry or the active mapping/grant snapshot.

### 8.3 Revocation policies

These are policies within the certificate profile, not additional identity profiles.

| Policy | Requirement |
|---|---|
| `Online` — production default | Native online revocation validation. Unknown or unavailable required revocation status fails closed. Valid native revocation-cache information can be used according to the platform's rules. |
| `ShortLived` — explicit production selection | Native chain validation without online revocation, plus enforcement that the actual leaf interval `NotAfter - NotBefore` is at most 24 hours. The deployment may configure a smaller maximum. |
| `Development` | Available only through LocalDevelopment with isolated trust; no production acceptance is implied. Network revocation is not required. |

A failed Online check MUST NOT trigger a retry using ShortLived or Development. A requested issuance duration is not evidence of the actual certificate lifetime: the receiver validates the issued leaf.

ShortLived does not provide immediate certificate revocation. Renewing or replacing a certificate, changing a mounted Secret or deleting a Secret does not invalidate copies already held elsewhere. The 24-hour ceiling bounds one leaf's validity; it does not bound compromise of a CA or continuing access to newly issued credentials.

Trust/mapping/grant changes take effect on replacement instances. During rolling replacement, old instances retain their previous snapshot. Emergency removal is complete only after every serving instance with superseded trust or grants has stopped admitting new work. Operations already admitted retain their existing deadline and ownership; RM-01 does not promise to cancel them all.

The guide MUST state the chosen deployment policy, its failure behavior, the issuer's actual revocation support and the operational removal procedure. Online validation also has cache/propagation limits and MUST NOT be described as universally instantaneous revocation.

### 8.4 Issuance authorization

Possession of a certificate from a trusted CA is insufficient if any application can request any identity. The deployment MUST restrict who can obtain each application/environment SAN.

The reference Kubernetes setup gives deployment automation ownership of Certificate/Issuer/policy resources. Workloads can read only their own credential material and cannot modify issuer policy, obtain CA private keys or request an arbitrary application's production SAN.

An approval controller may enforce this boundary, but the documentation MUST explain which requester/namespace/resource relationship it actually validates. Do not assume that a controller-created CertificateRequest proves the original workload's service-account identity.

Private-key custody, CA administration and issuer availability remain deployment responsibilities. CISLIB MUST NOT become a CA service.

## 9. Certificate sources, renewal and HTTP ownership

### 9.1 Supported sources and acquisition

The first certificate adapter MUST support a PEM certificate chain plus private-key file, including the layout produced by cert-manager. PFX/PKCS#12 file loading may be supplied through the same native loader; if included, password retrieval uses a secret reference and must be tested/documented. Additional secret-store integrations are extension points, not required new providers.

Source configuration identifies paths/references and expected identity. Native cryptographic APIs load and validate material. Validate that certificate and private key match, the chain/usage is acceptable, and the credential still represents the configured local application/environment before activation.

Use bounded file sizes and chain length, with documented defaults. Do not log private-key material, PFX passwords, full certificate dumps or raw parser input.

### 9.2 Reload contract

A certificate source MUST support initial load, a coalesced request to recheck, safe status reporting and stopping/disposal. The Kubernetes reference MUST adopt changed mounted material without requiring business-code changes.

Use the existing lifecycle/scheduling facilities for a bounded periodic source check; default 30 seconds. File notifications may shorten detection time but are not the only correctness mechanism, because projected mounts can change through replacement/symlinks. Do not introduce a new scheduler or an unbounded notification queue.

Reload proceeds as follows:

1. Read one consistent certificate/key generation; detect an inconsistent pair and retry on a later source check.
2. Fully validate the candidate, including expected identity and remaining validity.
3. Reserve capacity for a generation, then create a new native transport generation bound to that credential and the same destination policy.
4. Publish the new generation atomically for new operations.
5. Stop admitting operations to the retired generation and drain its actual in-flight owners/response leases.
6. Dispose the retired handler and key only after those owners settle.

A malformed or unauthorized replacement MUST NOT replace a valid active credential. The last valid generation may continue only while it remains valid under the configured policy. Emit a bounded degraded-status transition and reject new work once no usable credential remains. Never extend expiry because renewal failed.

At most two HTTP generations per certificate/destination binding may be live by default: one current and one draining. Coalesce later changes into a recheck of the newest source; do not accumulate handlers, private keys or a queue of historical candidates. If both slots are occupied, defer additional adoption before constructing another handler and retain only bounded state. If the active credential expires before adoption is possible, new operations fail with a defined unavailable/expired result. Existing owners are not disposed underneath active work.

### 9.3 Existing transport boundary

The reviewed `RestHttpTransport` has no client-certificate configuration seam. Add a constrained seam inside that transport; a separate `AddHttpClient` wrapper or independently sending handler would bypass established ownership. [R5]

Each pool is bound to one credential identity/generation and one immutable destination policy. Do not mutate a shared handler's client-certificate collection per request, and do not reuse a certificate-bearing transport under a client with broader origin permissions. .NET's native pool belongs to its underlying handler, which makes handler generation ownership relevant to rotation. [S9]

Preserve the exact root `IMeterFactory`, existing dispatch boundary, native timeout policy, disabled redirects/cookies/automatic credentials/proxies, response leases and borrowed/owned resource rules. The existing REST operation owns retries, total deadlines and cancellation classification.

All security-bound clients MUST have nonempty HTTPS `AllowedOrigins`. Reuse the existing finalized-request guard for every request, including absolute destination overrides. There is no automatic redirect-based credential forwarding.

For a security-bound client, reject request options that disable authentication or replace the bound profile, explicitly including the existing per-request `RestRequestOptions.Authenticate` override. Reject TRACE and any equivalent request mode whose existing semantics would imply stripping credentials while TLS still authenticates the caller. An explicitly anonymous client uses a separately configured credential-free transport. Ordinary unbound REST clients retain their existing override behavior.

Keep connection lifetime bounded; preserve the transport's existing finite default unless a justified profile setting changes it. Check credential usability when selecting a generation for every primary dispatch, including a retry already owned by REST; operation entry alone is insufficient if the credential expires while the call waits. A response lease retains its selected generation until settlement. On the receiving side, per-operation authentication/expiry checks prevent an established TLS connection from admitting work after leaf expiry. Work already admitted before expiry can complete under its existing operation ownership.

Each actual exchange attempt atomically selects and leases the current generation. A later permitted retry selects the then-current generation within the original operation deadline. Retirement prevents new selections without invalidating acquired leases; release follows actual send/response/body settlement. No naked generation reference may survive a retry delay. Verify rotation during that delay and concurrent selection/retirement.

### 9.4 Trust-anchor rollover

Leaf renewal and CA trust rotation are different operations. The deployment guide MUST describe a staged old/new trust bundle, issuance under the new chain, completion of workload rollout and removal of the old trust.

Trust-bundle changes use the startup-snapshot replacement rule. Do not automatically trust a root included by a client or copied from a changing leaf Secret. Root/intermediate expiry and issuer migration require explicit deployment management.

## 10. cert-manager deployment integration

cert-manager is the reference Kubernetes credential delivery option. It requests certificates and updates Secret material; application adoption remains the adapter's responsibility. Its CA issuer does not itself operate CRL/OCSP services or automatically rotate its CA certificate. [S10][S11]

The implementation MUST supply a documented deployment example covering:

| Area | Required example |
|---|---|
| Issuer | A named Issuer/ClusterIssuer reference and how it is administered |
| Identity | Approved URI SAN for the intended application/environment |
| Usage | Explicit client-authentication and digital-signature usages |
| Renewal | Explicit requested duration, renewal window and private-key rotation policy |
| Secret delivery | Read-only mounted certificate/key files; no update-blocking mount pattern |
| Trust delivery | Separate controlled trust-bundle configuration, with rollover instructions |
| Authorization | Deployment/workload RBAC and any approval policy actually used |
| Adoption | Observed credential generation change and new authenticated connection |
| Failure | Missing, inconsistent, expired and unauthorized replacement material |
| Removal | Loss of issuer access, application mapping/grant removal and replacement of affected instances |

An illustrative short-lived certificate resource is:

```yaml
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: country-worker-client
  namespace: country-demo
spec:
  secretName: country-worker-client-tls
  duration: 24h
  renewBefore: 8h
  isCA: false
  uris:
    - https://identity.example.test/applications/country-worker/environments/production
  usages:
    - digital signature
    - client auth
  privateKey:
    algorithm: ECDSA
    size: 256
    rotationPolicy: Always
  issuerRef:
    name: workload-issuer
    kind: Issuer
    group: cert-manager.io
```

This is a deployment shape, not a complete trusted PKI. The named issuer, issuance restrictions, trust distribution and receiving policy must also exist. If an issuer backdates a certificate or otherwise produces an actual validity interval longer than the ShortLived maximum, choose a shorter requested duration; do not weaken receiver validation to fit the example.

Set rotation policy explicitly rather than depending on a cert-manager version default. Pin/test a supported cert-manager version in the deployment guide at implementation time. The guide must not copy its workload private keys or CA keys into the repository.

Direct TLS or TLS pass-through is the reference path. Forwarded-certificate integration is opt-in: only a configured validating proxy on an authenticated/protected backend route may supply identity information; it must strip/replace public identity headers and the application must reject direct bypass. Untrusted certificate headers cannot authenticate a caller. Proxy-specific production recipes beyond the selected reference are not required for RM-01. [S8][S12]

## 11. LocalDevelopment profile

LocalDevelopment reuses the certificate source, validation, caller mapping, authorization and transport lifecycle. It does not introduce an accept-all scheme or a different permission engine.

### 11.1 Activation and isolation

Activation requires all of:

- An explicit `LocalDevelopment` security profile.
- A native host environment explicitly permitted for development.
- Canonical `EnvironmentOptions.EnvironmentName` matching the configured development environment.
- A development-only CA/trust bundle, identity mappings and grants.
- Loopback origins by default; an explicitly configured private development network for container scenarios.

A development flag alone does not establish isolation. Production configurations MUST exclude the development trust anchor, profile and mappings. Merely setting a request header or changing a claimed environment MUST NOT make a development credential acceptable.

### 11.2 Setup and reset

Deliver one small documented local setup/reset entry point using supported native cryptographic facilities or existing platform tooling. It MUST:

1. Generate an isolated development CA and separate client credentials for each sample application.
2. Generate or configure a server certificate with the required development hostnames.
3. Write untracked, access-restricted local files and a non-secret configuration example.
4. Register only explicit development capability grants.
5. Support renewal/recreation and deliberate reset of the isolated trust.
6. Explain how to start the caller and protected service and observe permit/deny behavior.

Default development leaves last seven days and the local CA thirty days; generated leaves MUST not outlive the CA. These are local setup defaults and do not loosen the production ShortLived policy. Use per-workspace/per-developer trust; do not ship a shared private key or a universal development credential.

Prefer process-specific trust configuration. The setup MUST NOT silently install a CA into the machine-wide trust store. File permissions/ACLs follow the operating system and credentials remain outside source control.

`dotnet dev-certs` provides development HTTPS server-certificate tooling; it is not the application identity issuer for this profile. The consumer guide must keep server TLS trust and client application identity distinct. [S13]

Resetting a CA invalidates credentials only for processes that adopt the replacement trust. Explain restart/reconfiguration of the running sample rather than claiming that deleting local files revokes remote copies.

An operator may deliberately test a real Entra integration from a development environment with separately configured credentials. That does not change LocalDevelopment or introduce an automatic credential chain.

## 12. Result and failure semantics

### 12.1 Portable classification

Known security failures MUST have bounded, stable categories. The implemented contract publishes exact codes with at least the following meanings:

| Category | Meaning |
|---|---|
| `credential_unavailable` | Required source/provider cannot provide a usable credential |
| `credential_invalid` | Credential material or provider response is known to be invalid |
| `credential_expired` | Expiry is established from validated/local evidence |
| `unauthenticated` | No acceptable authenticated principal was established |
| `identity_unmapped` | Native authentication succeeded but required trusted application mapping is absent |
| `capability_denied` | The authorization evaluator denied the actual requested capability/target |
| `validation_unavailable` | Required trust/revocation/validation evidence cannot be obtained |
| `authentication_rejected` | A remote authentication rejection is known, but a more specific cause is not established |
| `cause_unknown` | The available transport/status information does not establish a security-specific cause |

Diagnostic detail may include a known provider error code and safe message. Do not expose credential material, full claims, internal exception dumps or unnecessary identity/grant details to an unauthenticated caller.

Expected credential/authorization failures return the defined result. Caller cancellation, total deadline and host stopping preserve their existing distinct semantics. Unexpected programming or invariant faults MUST NOT be swallowed and converted into an ordinary permission denial.

### 12.2 Preserve native protocol results

| Observation | Required interpretation |
|---|---|
| Bearer authentication missing/invalid | Normally native HTTP 401 with the appropriate challenge |
| Authenticated caller lacks a capability | Native authorization forbid, normally HTTP 403 |
| Certificate handler rejects authentication | May be HTTP 403 even though authentication failed |
| TLS handshake fails | May produce no HTTP response at all |
| Arbitrary remote 401/403 or TLS exception | Preserve actual status/transport data; classify further only with reliable evidence |

These differences are part of the contract. ASP.NET Core's certificate handler can return 403 for authentication failure; TLS can reject the connection earlier. Do not interpret every 403 as a capability denial or every TLS failure as an expired certificate. [S8][S14]

If a service returns a structured security error, the receiving adapter may use it only according to the documented service contract. Unknown/missing remote detail remains unknown. Do not fabricate a precise failure from a status code.

### 12.3 Existing REST results

Known Entra acquisition failures use the existing `RestAuthenticationContext.Fail` path. Preserve the established `RestFailureKind.Authentication` and `client.authentication_failed` outer result. If consumers need the portable classification, add a backward-compatible typed detail to the existing result/context rather than replace its public shape. A locally generated explanation MUST NOT be mislabeled as a remote server diagnosis. [R4]

Certificate failures preserve the native transport result and any reliably known local credential detail. Existing deadline/cancellation/dispatch precedence and cleanup MUST remain unchanged.

No adapter may automatically replay a protected operation after a 401, 403, credential refresh or certificate reload. The existing REST retry policy remains the only retry owner, and its default authentication/authorization classification is non-retryable. A later operation can acquire current credentials normally. No permission-denial refresh loop is permitted.

## 13. Operations, audit and instrumentation

### 13.1 Trusted context at operation boundaries

HTTP and imperative capability checks expose the same trusted context to the protected operation. The operation owner attaches that context to its audit event together with capability, actual target, decision, correlation identifier and policy version.

Authorization success and business-operation success are separate facts. An allowed operation that subsequently fails MUST NOT be audited as successfully completed. A denied operation MUST NOT run the protected body.

On an outgoing call, use the selected workload credential. The receiving service independently authenticates it and creates its own caller context. Do not propagate an unrestricted principal or assume that a copied human/instance header establishes delegation.

### 13.2 Audit hook

Provide an integration hook for authorization decisions and operation completion, without adding a durable store, delivery queue or new audit service. Preserve bounded trusted identifiers and clearly identify reported metadata. Credential material is always excluded.

The native authentication adapter may report a rejected authentication with a safe profile/reason, but it cannot invent a trusted caller for that event. Diagnostics for malformed or anonymous input must remain bounded and resistant to log flooding.

Audit-sink behavior belongs to its configured owner. A hook failure must be observable and cannot silently become a successful audit receipt. RM-01 does not guarantee durable audit delivery. If a protected business operation requires durable audit before execution, that requirement belongs to its explicit operation policy and existing audit integration.

### 13.3 Metrics contract

Reuse native HTTP/authentication instruments where their actual measurement boundaries meet the requirement. Do not record a second incoming request duration or a second native transport attempt for security. Inspect the pinned framework's instrument names before subscribing.

Add only these initial CIS-specific measurements, using the existing Metrics facade and exporter. The names below are the required proposed catalog entries; verify the actual exported exposition in the implementation.

| Logical name / exported name | Kind and unit | Dimensions | Boundary and use |
|---|---|---|---|
| `cis.security.authorization.decisions` / `cis_security_authorization_decisions_total` | Monotonic counter; Count | `profile`, `outcome=allow\|deny` | Once per completed composite authorization evaluation, not once per requirement handler; query denial rate |
| `cis.security.certificate.reloads` / `cis_security_certificate_reloads_total` | Monotonic counter; Count | `profile`, `outcome=adopted\|rejected\|deferred` | A changed credential candidate's terminal adoption result; query renewal failures |
| `cis.security.certificate.remaining` / `cis_security_certificate_remaining_seconds` | Observable gauge; Seconds (`s`) | `profile` | Remaining active leaf validity, clamped to zero when expired; omit when no leaf is available; alert on expiry risk |

The `profile` label is a registered alias from the finite startup profile catalog; use one reserved `unresolved` category where appropriate. Do not add subject, user, instance, resource identifier, raw issuer URL, token ID, serial/thumbprint, policy version or arbitrary exception text as metric dimensions.

A certificate candidate's repeated unchanged failure/defer checks MUST NOT produce an unbounded stream of equivalent reload events. Record state transitions or a materially new candidate once; a later successful adoption is a new transition. Native token-cache hits MUST NOT be labeled as token renewals, because the adapter may not know whether renewal occurred.

Counters are cumulative for the provider lifetime; derive rates in the monitoring backend. The gauge reads already-owned state and performs no I/O during a scrape. No histogram or new measurement queue is required by RM-01.

Every exported sample, including native and overflow samples, retains the centrally supplied `application` and `environment` labels from `EnvironmentOptions`. Reuse Metrics' configured stream/cardinality limits and no-op behavior. Reserve capacity for these bounded instruments; telemetry failure MUST NOT change authorization or successful business outcomes. [R8]

### 13.4 Diagnostics and health

Log bounded profile availability/recovery, rejected replacement and trust/mapping configuration failures. Do not log a success per cached credential acquisition or emit a warning on every periodic source check.

Health reports profile alias, safe availability reason, known expiry and whether an essential dependency affects readiness. It must not return credentials, unrestricted claims, tenant inventory or complete grants. Keep detailed health access under its established authorization rule.

## 14. Acceptance and verification

Use the repository's native test runner, fixtures and focused integration facilities. Add meaningful behavior tests to the owning implementation unit. Reuse `CIS.CommonTesting` clock/recording facilities where suitable; add small consumer-facing doubles only when needed.

A fake credential provider is useful for cancellation/failure tests. It cannot prove JWT signature validation, TLS certificate authentication, AKS federation or cert-manager renewal. Those boundaries require native integration evidence.

| ID | Scenario | Required evidence |
|---|---|---|
| AC-01 | Shared identity contract | Local identity exactly reuses EnvironmentOptions; reported replica ID is not promoted to verified identity |
| AC-02 | Entra permitted call | Representative AKS workload acquires a real Entra API token and reaches its permitted operation |
| AC-03 | Certificate permitted call | Real TLS client certificate maps to the intended application/environment and reaches the same operation |
| AC-04 | Local development | Documented setup starts caller/service without Azure/Kubernetes or machine-wide CA installation; per-app certificate, server trust and grants work |
| AC-05 | Scoped denial | A valid caller is denied the wrong capability, target application, environment and resource |
| AC-06 | Grant cross-product | The two grants in section 4.2 cannot produce either unauthorized mixed permission |
| AC-07 | Multiple requirements | Every required capability is checked against the same actual target; an incomplete set executes no protected work |
| AC-08 | Invalid credentials | Missing, invalid signature/chain, wrong issuer/audience/SAN/usage and expired credentials fail at the appropriate native boundary |
| AC-09 | Caller spoofing | Application/environment/user/instance headers and serialized contexts cannot replace authenticated identity |
| AC-10 | Scheme isolation | Unrelated bearer/certificate identities are not merged; a production failure never selects LocalDevelopment |
| AC-11 | Development isolation | A development leaf is rejected by production trust even when it claims a production-like name |
| AC-12 | Certificate adoption | A real changed certificate/key pair is adopted; atomic attempt leases preserve old owners; rotation during retry delay selects the current generation safely |
| AC-13 | Bad renewal | Mismatched, malformed, wrong-identity or expired replacement is not adopted; last valid material receives no expiry extension |
| AC-14 | Renewal bounds | Repeated changes during an in-flight response cannot create unbounded handlers, retained keys or work queues |
| AC-15 | Revocation policies | Online fails closed on unavailable required status; ShortLived rejects an actual interval over 24 hours; neither falls back |
| AC-16 | Existing connection expiry | An already established TLS connection cannot start a protected operation after its client leaf expires |
| AC-17 | Destination/profile safety | Absolute-URL overrides, broader borrowed-client origins, DisableAuthentication and TRACE cannot leak/switch the bound credential |
| AC-18 | Cancellation and replay | Acquisition obeys the existing deadline/cancellation owner; denial/refresh causes no hidden replay; faults and cleanup retain their contract |
| AC-19 | Context lifecycle | Concurrent requests and background execution do not leak caller identity or inherit authority accidentally |
| AC-20 | Host integration | Authenticated detailed health works; real mTLS/operational listeners preserve probes without a business bypass; receiver-only activation needs no outbound credential; one-provider lifecycle remains intact |
| AC-21 | Configuration replacement | New instances enforce the new trust/grant snapshot; old instances retain theirs until replaced; no partial live policy mutation |
| AC-22 | Observability | No secrets in logs/context/results; actual failure category preserved; a real scrape has bounded labels and required application/environment |
| AC-23 | Packaged consumption | Core-only, certificate-only and Entra consumers restore/use the intended optional packages without unintended runtime dependencies |
| AC-24 | cert-manager recipe | In an authorized Kubernetes reference environment, issuance/mount/renewal leads to observed application adoption and a new authenticated connection |
| AC-25 | Consumer handoff | Current compiling example, three provider guides, portable fixtures, checked-in skill and AGENTS routing point to real implemented APIs |

Group closely related cases into ordinary tests. Exercise Windows/Linux differences only where the supported binding, certificate store/loader, TLS behavior or filesystem handling creates a concrete risk.

The complete Entra and cert-manager deployment proofs require suitable authorized infrastructure. If credentials or infrastructure are unavailable, implementation work and local native tests can proceed, but AC-02/AC-24 remain explicitly unverified. Record the exact missing prerequisite and the reproducible command/steps; do not replace real federation/renewal evidence with a simulated pass or mark RM-01 complete.

Do not add a bespoke verification harness, broad benchmark campaign, mutation campaign, repeated full-suite reruns or separate evidence archive merely for this feature. Run the current required repository gates and broaden verification only for a concrete unresolved risk. A source change affecting a contract reruns the impacted tests; unchanged earlier evidence remains valid.

**Roadmap completion requires the implemented three profiles, the applicable acceptance evidence and usable consumer guidance.** This specification's publication alone does not satisfy roadmap completion.

## 15. Implementation units and repository deliverables

### 15.1 Coherent implementation units

Use the existing CIS workflow to import this feature input, establish eligible work and decompose it into bounded implementation units. The following grouping identifies outcomes, not a mandatory new planning system.

| Unit | Outcome | Dependencies and ownership |
|---|---|---|
| A — common security contract | Caller model, complete-grant authorization, native policy bridge, trusted snapshots and minimal host integration | Own Core security contracts and host order; establish shared semantics first |
| B — REST security binding | Profile/origin enforcement, constrained TLS seam, credential generation/response-lease ownership and result detail | After shared contracts; one owner for REST internals |
| C — Entra integration | Explicit workload acquisition, native JWT validation, role mapping and AKS consumer guide | Can proceed alongside B after A's contracts stabilize |
| D — certificate integration | Native mTLS, source validation/reload, revocation policies and cert-manager recipe | Depends on B's stable transport seam; coordinate certificate/native TLS files with B |
| E — development and consumer proof | Isolated setup/reset, representative three-profile example, portable fixtures and packaged-consumer proof | Reuse C/D; update shared example only under one file owner |

Each unit includes its affected tests, documentation and completion evidence. Do not turn every test, review or document edit into a separate work item without a separate deliverable. If a unit exceeds the repository's permitted implementation size, split it along a concrete independently reviewable outcome.

Parallel work is appropriate after contracts stabilize. Assign nonoverlapping file ownership; one coordinator owns central package/solution/lock files and shared build outputs. Avoid having independent agents edit the same transport or tutorial simultaneously.

### 15.2 Required repository assets

Use current repository conventions, with the following proposed destinations:

| Destination | Deliverable |
|---|---|
| `planning/security/sources/` | This supplied specification preserved as feature input, following the installed import workflow |
| `contracts/security/README.md` | Portable semantics, identity provenance, complete-grant rules, result meanings and conformance version |
| `contracts/security/` | Small machine-readable grant/caller/result fixtures for future bindings |
| `docs/modules/security/README.md` | Consumer entry point and actual published C# API |
| `docs/modules/security/` | Three profile guides, credential/trust lifecycle, deployment inputs and troubleshooting |
| Existing C# module/package structure | Core additions and the two optional integration packages |
| Existing CountryData example | Minimal protected service/caller demonstration with selectable profiles; preserve one maintained tutorial |
| `.github/skills/cislib-security/SKILL.md` | Checked-in instructions for implementing, consuming and reviewing the module |
| Root `AGENTS.md` | Short routing link to the security skill and authoritative contract |
| Existing metric/dependency catalogs | New security instruments and approved optional package closure |

If a destination already exists, update it rather than create a competing guide or sample tree. Inspect current implemented names before writing examples. Documentation must distinguish a logical portable configuration example from an actual copy-and-run C# configuration.

Use readable C# with full braces and logical spacing, following the current repository conventions. Do not make broad unrelated formatting changes.

### 15.3 Consumer guide requirements

Each profile guide MUST include prerequisites, exact registration against implemented APIs, configuration ownership, a minimal permitted call, a denied call, acquisition/renewal behavior, failure interpretation and deployment cleanup.

The shared guide MUST explain target scope, cross-environment grants, instance provenance, endpoint versus imperative authorization, background execution, server/client certificate distinction, origin restrictions and absence of automatic authentication replay.

The example should expose a harmless read capability and a separate restricted change capability. Production example configuration must not grant every application every capability. Include one resource-specific denial and one clearly visible business effect that never happens on denied requests.

## 16. Security skill and future-agent instructions

The implementation MUST install/update a repository skill using the current repository skill convention. This section is the required content basis; it is not a claim that a skill has already been installed.

The skill's activation scope is implementing, consuming or reviewing CISLIB authentication, caller context, capability authorization, credential renewal and its native .NET adapters. It MUST direct future agents to:

1. Read root instructions, the actual implementation, portable contract and current provider guide before changing code.
2. Reuse EnvironmentOptions; distinguish configured local metadata, authenticated application identity and reported instance metadata.
3. Use native authentication and IAuthorizationService; maintain one complete-grant evaluator for endpoints and imperative checks.
4. Keep provider/native types out of portable contracts and dependencies in the correct optional package.
5. Select a profile explicitly and preserve production/development trust isolation.
6. Acquire Entra credentials through the SDK and attach them through the existing final REST authentication hook.
7. Bind client certificates to the existing native transport generation and immutable origins; preserve response-lease drain and bounded rotation.
8. Validate issuer, identity, usage, expiry and the selected revocation policy; never fall back after failure.
9. Preserve actual TLS/HTTP results, cancellation ownership and absence of automatic authentication replay.
10. Keep caller context scoped and immutable; do not turn provenance or serialized contexts into authority.
11. Reuse bounded metrics, secret-safe diagnostics, existing lifecycle and native testing.
12. Update the compiling example, affected docs and focused evidence with every contract change.

The skill MUST link to actual repository paths and contain concise current examples, not stale APIs copied from this proposal. Validate it with the installed repository tooling; do not introduce an external skill service or modify unrelated skills.

Add a short root AGENTS routing section outside any managed instruction block. Its substance is:

> When implementing or consuming CISLIB security, read the checked-in cislib-security skill, contracts/security and docs/modules/security. Use the implemented API and selected profile guide. Preserve existing environment identity, native authentication/authorization, complete scoped grants, explicit trust, credential lifecycle and REST ownership. Do not invent a second identity provider, policy engine, transport or development bypass.

## 17. Implementation-agent handoff

Use this prompt together with the supplied specification:

> Implement CISLIB RM-01 Application identity and security from this specification. Inspect the current checkout, root AGENTS.md, dependency policy, native workflow and actual EnvironmentOptions/Application/Configuration/RestClient/Metrics/CommonTesting contracts first. Preserve the supplied specification as feature input in the existing product; reconcile later repository changes without discarding accepted semantics.
>
> Deliver all three profiles: Entra Workload Identity for AKS; direct client-certificate mTLS with optional cert-manager deployment integration; and isolated local development certificates using the same mTLS path. Reuse native .NET authentication and authorization, the existing environment identity and one host. Add only the constrained native REST transport support required for certificate identity and bounded renewal.
>
> Establish the portable caller, target, grant and result contracts first. Keep complete-grant matching, scheme isolation, truthful instance provenance, explicit revocation policy and startup trust/grant snapshots. Preserve deadline/cancellation/retry ownership and actual HTTP/TLS failure semantics. Do not create an identity server, CA service, rule engine, new scheduler or parallel HTTP stack.
>
> Follow the installed CIS workflow for feature input, bounded work, execution ownership and native verification. Keep tests, documentation and completion evidence with coherent implementation outcomes. Parallelize only with explicit file ownership after shared contracts stabilize. Reuse accepted session authorization and do not reopen completed historical campaigns. Handle genuinely new permissions or repository-required decisions through their actual workflow; this document does not override access controls.
>
> Deliver the current compiling example, profile/deployment guides, portable fixtures, security metric catalog, checked-in security skill and root routing link. Run focused native and packaged-consumer checks plus required repository gates. Record real AKS/cert-manager evidence when authorized infrastructure exists. If it does not, name those acceptance gaps precisely while completing the independently executable work. Do not claim implementation, provider validation, publication or installation before each has occurred.

## 18. Source references and interpretation

Repository references identify the reviewed commit. External references were checked during specification preparation. Native SDK/framework and cert-manager versions MUST be reconciled with the versions actually selected for implementation; documentation defaults are not permission to override this specification's explicit settings.

Requirements introduced by this document—such as the three-profile selection, 24-hour ShortLived maximum, startup policy snapshots, grant matching and bounded transport generations—are CISLIB design decisions. Citations establish the underlying native behavior and existing integration points; they do not imply that vendors prescribe every CISLIB requirement.

### Repository baseline

- [R1] Root repository instructions and native implementation workflow.
- [R2] EnvironmentOptions implementation and authoritative identity fields.
- [R3] CISApplication hosting and middleware assembly.
- [R4] Existing REST authentication/result context.
- [R5] RestHttpTransport native pool and lifetime ownership.
- [R6] Finalized-request URI/origin enforcement.
- [R7] Optional CommonTesting package and test integration.
- [R8] Portable Metrics contract and cardinality/identity conventions.
- [R9] C# dependency policy.

### Native platform and provider documentation

- [S1] ASP.NET Core authentication concepts and schemes.
- [S2] ASP.NET Core policy-based authorization.
- [S3] ASP.NET Core resource-based authorization.
- [S4] AKS workload identity overview.
- [S5] Entra client-credentials flow and application permissions.
- [S6] Azure Identity authentication best practices for .NET.
- [S7] Configuring JWT bearer validation in ASP.NET Core.
- [S8] ASP.NET Core certificate authentication, including proxy and failure behavior.
- [S9] .NET HttpClient lifetime and connection-pool guidance.
- [S10] cert-manager Certificate resource, key rotation and Secret adoption.
- [S11] cert-manager CA issuer limitations and revocation responsibilities.
- [S12] ASP.NET Core proxy/forwarded-header configuration.
- [S13] dotnet dev-certs development HTTPS tooling.
- [S14] OAuth bearer-token protocol error semantics.
- [S15] Native outbound TLS certificate-chain policy.
- [S16] Native IAuthorizationService asynchronous signatures.

[R1]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/AGENTS.md
[R2]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs
[R3]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.Core/Application/CISApplication.cs
[R4]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.Core/RestClient/Contracts/RestContexts.cs
[R5]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.Core/RestClient/Http/RestHttpTransport.cs
[R6]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.Core/RestClient/Extensions/RestUri.cs
[R7]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/implementations/csharp/src/CIS.CommonTesting/CIS.CommonTesting.csproj
[R8]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/contracts/metrics/README.md
[R9]: https://github.com/andrewspiteri/CISLIB/blob/31739a247efba609f27dd50caab1312bb775bd79/docs/architecture/csharp-dependency-policy.md
[S1]: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0
[S2]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0
[S3]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0
[S4]: https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview
[S5]: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow
[S6]: https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices
[S7]: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0
[S8]: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0
[S9]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
[S10]: https://cert-manager.io/docs/usage/certificate/
[S11]: https://cert-manager.io/docs/configuration/ca/
[S12]: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
[S13]: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs
[S14]: https://www.rfc-editor.org/rfc/rfc6750.html
[S15]: https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions.certificatechainpolicy?view=net-10.0
[S16]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationservice.authorizeasync?view=aspnetcore-10.0



